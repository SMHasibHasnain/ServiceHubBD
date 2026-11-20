using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Features.Billing;

namespace ServiceHub.Api.Features.Businesses;

public record CreateBusinessRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [Range(1, int.MaxValue)] int CategoryId,
    [RegularExpression("^[a-z0-9-]{3,170}$")] string? Slug,
    [StringLength(300)] string? ShortDescription,
    string? Description,
    int? ZoneId,
    string? AddressLine,
    string? Phone,
    [EmailAddress] string? Email,
    bool OffersHomeService = false,
    bool OffersEmergencyService = false);

public record UpdateBusinessRequest(
    [StringLength(150, MinimumLength = 2)] string? Name,
    int? CategoryId,
    [StringLength(200)] string? Tagline,
    [StringLength(300)] string? ShortDescription,
    string? Description,
    short? EstablishedYear,
    string? LogoUrl,
    string? CoverUrl,
    string? Phone,
    [EmailAddress] string? Email,
    string? WebsiteUrl,
    string? FacebookUrl,
    string? AddressLine,
    int? ZoneId,
    decimal? Latitude,
    decimal? Longitude,
    string? CoverageNote,
    List<string>? Languages,
    bool? OffersHomeService,
    bool? OffersEmergencyService,
    [Range(1, 9)] short? OnboardingStep);

public record HourDto([Range(0, 6)] short DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt);

public record BookingSettingsDto(
    bool AutoConfirm,
    [Range(5, 240)] short SlotIntervalMinutes,
    [Range(0, 10080)] int MinNoticeMinutes,
    [Range(1, 365)] short MaxAdvanceDays,
    [Range(0, 240)] short BufferMinutes,
    [Range(1, 100)] short MaxConcurrentBookings,
    string? CancellationPolicy);

public record ClosureRequest(DateOnly ClosedOn, TimeOnly? StartTime, TimeOnly? EndTime, [StringLength(150)] string? Reason);

public record ClosureDto(Guid Id, DateOnly ClosedOn, TimeOnly? StartTime, TimeOnly? EndTime, string? Reason);

public record DocumentRequest(DocumentType DocumentType, [Required, Url] string FileUrl, string? FileName, string? MimeType, int? FileSizeBytes);

public record SubmitVerificationRequest([Required, MinLength(1)] List<DocumentRequest> Documents);

public record VerificationSummaryDto(Guid Id, short AttemptNo, VerificationStatus Status, bool RequiresInspection,
    string? DecisionReason, DateTime SubmittedAt, DateTime? DecidedAt, List<DocumentType> DocumentTypes);

public record BusinessDto(
    Guid Id, string Name, string Slug, int CategoryId, string CategoryName, string? Tagline, string? ShortDescription,
    string? Description, short? EstablishedYear, string? LogoUrl, string? CoverUrl, string? Phone, string? Email,
    string? WebsiteUrl, string? FacebookUrl, string? AddressLine, int? ZoneId, string? ZoneName, decimal? Latitude,
    decimal? Longitude, string? CoverageNote, List<string> Languages, bool OffersHomeService, bool OffersEmergencyService,
    BusinessStatus Status, VerificationStatus VerificationStatus, short OnboardingStep, decimal AvgRating, int ReviewCount,
    int CompletedBookingCount, List<HourDto> Hours, BookingSettingsDto BookingSettings, string MyRole, string[] MyPermissions,
    string? StaffRole = null);

[ApiController]
[Authorize]
[Route("api/businesses")]
public class BusinessesController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit, EntitlementService plans,
    Page.UploadCleanup cleanup) : ControllerBase
{
    private static readonly VerificationStatus[] EditableForSubmit =
        [VerificationStatus.Draft, VerificationStatus.Rejected, VerificationStatus.InfoRequested];

    /// <summary>Creates a draft business page owned by the current user (who becomes its owner member).</summary>
    [HttpPost]
    public async Task<ActionResult<BusinessDto>> Create(CreateBusinessRequest req)
    {
        var userId = me.RequireId();
        await EnsureCategoryAsync(req.CategoryId);
        if (req.ZoneId is not null) await EnsureZoneAsync(req.ZoneId.Value);

        var slug = req.Slug ?? Slugs.Create(req.Name);
        if (await db.Businesses.IgnoreQueryFilters().AnyAsync(b => b.Slug == slug))
            throw new ConflictException("এই লিংকটি আগেই ব্যবহৃত হয়েছে। অন্য একটি দিন।", "slug_taken");

        var ownerRoleId = await RoleIdAsync(R.BusinessOwner);
        var business = new Business
        {
            OwnerId = userId,
            Name = req.Name.Trim(),
            Slug = slug,
            CategoryId = req.CategoryId,
            ShortDescription = req.ShortDescription,
            Description = req.Description,
            ZoneId = req.ZoneId,
            AddressLine = req.AddressLine,
            Phone = Phone.NormalizeBd(req.Phone) ?? req.Phone,
            Email = req.Email,
            OffersHomeService = req.OffersHomeService,
            OffersEmergencyService = req.OffersEmergencyService,
            Languages = ["bn"],
            BookingSettings = new BusinessBookingSettings(),
        };
        business.Members.Add(new BusinessMember { UserId = userId, RoleId = ownerRoleId, JoinedAt = DateTime.UtcNow });
        db.Businesses.Add(business);
        audit.Log("business.create", "business", business.Id, businessId: business.Id, scope: RoleScope.Business, roleId: ownerRoleId);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = business.Id }, await LoadDtoAsync(business.Id));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BusinessDto>> Get(Guid id)
    {
        await access.RequireBusinessOrStaffAsync(id, P.BusinessView);
        return await LoadDtoAsync(id);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<BusinessDto>> Update(Guid id, UpdateBusinessRequest req)
    {
        var member = await access.RequireBusinessOrStaffAsync(id, P.BusinessUpdate);
        var b = await db.Businesses.FirstAsync(x => x.Id == id);
        var (oldLogo, oldCover) = (b.LogoUrl, b.CoverUrl);
        ImageUrls.Ensure(req.LogoUrl);
        ImageUrls.Ensure(req.CoverUrl);
        if (req.CategoryId is not null) { await EnsureCategoryAsync(req.CategoryId.Value); b.CategoryId = req.CategoryId.Value; }
        if (req.ZoneId is not null) { await EnsureZoneAsync(req.ZoneId.Value); b.ZoneId = req.ZoneId; }
        if (req.Name is not null) b.Name = req.Name.Trim();
        if (req.Tagline is not null) b.Tagline = req.Tagline;
        if (req.ShortDescription is not null) b.ShortDescription = req.ShortDescription;
        if (req.Description is not null) b.Description = req.Description;
        if (req.EstablishedYear is not null) b.EstablishedYear = req.EstablishedYear;
        if (req.LogoUrl is not null) b.LogoUrl = req.LogoUrl;
        if (req.CoverUrl is not null) b.CoverUrl = req.CoverUrl;
        if (req.Phone is not null) b.Phone = Phone.NormalizeBd(req.Phone) ?? req.Phone;
        if (req.Email is not null) b.Email = req.Email;
        if (req.WebsiteUrl is not null) b.WebsiteUrl = req.WebsiteUrl;
        if (req.FacebookUrl is not null) b.FacebookUrl = req.FacebookUrl;
        if (req.AddressLine is not null) b.AddressLine = req.AddressLine;
        if (req.Latitude is not null) b.Latitude = req.Latitude;
        if (req.Longitude is not null) b.Longitude = req.Longitude;
        if (req.CoverageNote is not null) b.CoverageNote = req.CoverageNote;
        if (req.Languages is not null) b.Languages = req.Languages;
        if (req.OffersHomeService is not null) b.OffersHomeService = req.OffersHomeService.Value;
        if (req.OffersEmergencyService is not null) b.OffersEmergencyService = req.OffersEmergencyService.Value;
        if (req.OnboardingStep is not null) b.OnboardingStep = req.OnboardingStep.Value;
        audit.Log("business.update", "business", id, businessId: id, scope: member.Scope, roleId: member.RoleId);
        await db.SaveChangesAsync();
        await cleanup.DeleteIfUnusedAsync(id, oldLogo, oldCover);
        return await LoadDtoAsync(id);
    }

    /// <summary>Replaces the weekly opening hours. Several intervals per day are allowed (split shifts).</summary>
    [HttpPut("{id:guid}/hours")]
    public async Task<ActionResult<List<HourDto>>> SetHours(Guid id, List<HourDto> hours)
    {
        await access.RequireBusinessOrStaffAsync(id, P.BusinessUpdate);
        foreach (var day in hours.GroupBy(h => h.DayOfWeek))
        {
            var ordered = day.OrderBy(h => h.OpensAt).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].ClosesAt <= ordered[i].OpensAt)
                    throw new BadRequestException("বন্ধের সময় খোলার সময়ের পরে হতে হবে।", "invalid_hours");
                if (i > 0 && ordered[i].OpensAt < ordered[i - 1].ClosesAt)
                    throw new BadRequestException("একই দিনের সময়গুলো একটির সাথে আরেকটি মিলে যাচ্ছে।", "overlapping_hours");
            }
        }
        // A staff edit already runs in its own transaction (see StaffEdit); join it instead of opening a second one.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null;
        await db.BusinessHours.Where(h => h.BusinessId == id).ExecuteDeleteAsync();
        db.BusinessHours.AddRange(hours.Select(h => new BusinessHour { BusinessId = id, DayOfWeek = h.DayOfWeek, OpensAt = h.OpensAt, ClosesAt = h.ClosesAt }));
        await db.SaveChangesAsync();
        if (tx is not null) await tx.CommitAsync();
        return hours.OrderBy(h => h.DayOfWeek).ThenBy(h => h.OpensAt).ToList();
    }

    [HttpPut("{id:guid}/booking-settings")]
    public async Task<ActionResult<BookingSettingsDto>> SetBookingSettings(Guid id, BookingSettingsDto req)
    {
        await access.RequireBusinessOrStaffAsync(id, P.BusinessUpdate);
        if (req.AutoConfirm) await plans.RequireFeatureAsync(id, E.AutoConfirm);
        var s = await db.BusinessBookingSettings.FirstAsync(x => x.BusinessId == id);
        s.AutoConfirm = req.AutoConfirm;
        s.SlotIntervalMinutes = req.SlotIntervalMinutes;
        s.MinNoticeMinutes = req.MinNoticeMinutes;
        s.MaxAdvanceDays = req.MaxAdvanceDays;
        s.BufferMinutes = req.BufferMinutes;
        s.MaxConcurrentBookings = req.MaxConcurrentBookings;
        s.CancellationPolicy = req.CancellationPolicy;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return req;
    }

    [HttpGet("{id:guid}/closures")]
    public async Task<ActionResult<List<ClosureDto>>> Closures(Guid id)
    {
        await access.RequireBusinessOrStaffAsync(id, P.BusinessView);
        return await db.BusinessClosures.Where(c => c.BusinessId == id && c.ClosedOn >= BdTime.Today())
            .OrderBy(c => c.ClosedOn)
            .Select(c => new ClosureDto(c.Id, c.ClosedOn, c.StartTime, c.EndTime, c.Reason)).ToListAsync();
    }

    [HttpPost("{id:guid}/closures")]
    public async Task<ActionResult<ClosureDto>> AddClosure(Guid id, ClosureRequest req)
    {
        await access.RequireBusinessOrStaffAsync(id, P.BusinessUpdate);
        if ((req.StartTime is null) != (req.EndTime is null) || (req.StartTime is not null && req.EndTime <= req.StartTime))
            throw new BadRequestException("পুরো দিন অথবা সঠিক শুরু ও শেষ সময় দিন।", "invalid_closure");
        var c = new BusinessClosure { BusinessId = id, ClosedOn = req.ClosedOn, StartTime = req.StartTime, EndTime = req.EndTime, Reason = req.Reason };
        db.BusinessClosures.Add(c);
        await db.SaveChangesAsync();
        return new ClosureDto(c.Id, c.ClosedOn, c.StartTime, c.EndTime, c.Reason);
    }

    [HttpDelete("{id:guid}/closures/{closureId:guid}")]
    public async Task<IActionResult> DeleteClosure(Guid id, Guid closureId)
    {
        await access.RequireBusinessOrStaffAsync(id, P.BusinessUpdate);
        await db.BusinessClosures.Where(c => c.Id == closureId && c.BusinessId == id).ExecuteDeleteAsync();
        return NoContent();
    }

    /// <summary>
    /// Submits (or resubmits) the page for verification. A frozen snapshot is stored so moderators
    /// review exactly what was submitted.
    /// </summary>
    [HttpPost("{id:guid}/verification")]
    public async Task<ActionResult<VerificationSummaryDto>> SubmitVerification(Guid id, SubmitVerificationRequest req)
    {
        var member = await access.RequireBusinessAsync(id, P.VerificationSubmit);
        var b = await db.Businesses.Include(x => x.Hours).Include(x => x.Category).Include(x => x.Zone)
            .Include(x => x.Services.Where(s => s.Status == RecordStatus.Active))
            .FirstAsync(x => x.Id == id);

        if (!EditableForSubmit.Contains(b.VerificationStatus))
            throw new ConflictException("যাচাইকরণের আবেদন ইতিমধ্যে জমা আছে।", "verification_in_progress");
        var missing = new List<string>();
        if (b.ZoneId is null || string.IsNullOrWhiteSpace(b.AddressLine)) missing.Add("ঠিকানা ও এলাকা");
        if (string.IsNullOrWhiteSpace(b.Description) && string.IsNullOrWhiteSpace(b.ShortDescription)) missing.Add("ব্যবসার বিবরণ");
        if (b.Hours.Count == 0) missing.Add("কাজের সময়");
        if (b.Services.Count == 0) missing.Add("অন্তত একটি সেবা");
        if (missing.Count > 0)
            throw new BadRequestException("জমা দেওয়ার আগে সম্পূর্ণ করুন: " + string.Join(", ", missing), "profile_incomplete");

        var attempt = (short)(await db.VerificationRequests.CountAsync(v => v.BusinessId == id) + 1);
        var snapshot = JsonSerializer.SerializeToDocument(new
        {
            b.Name, b.Slug, Category = b.Category.NameBn, b.ShortDescription, b.Description, b.AddressLine,
            Zone = b.Zone?.NameBn, ZonePath = b.Zone?.Path.ToString(), b.Phone, b.Email, b.WebsiteUrl, b.FacebookUrl,
            b.OffersHomeService, b.OffersEmergencyService, b.EstablishedYear,
            Hours = b.Hours.OrderBy(h => h.DayOfWeek).Select(h => new { h.DayOfWeek, h.OpensAt, h.ClosesAt }),
            Services = b.Services.Select(s => new { s.Name, s.PriceType, s.Price, s.DurationMinutes }),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var request = new VerificationRequest
        {
            BusinessId = id,
            SubmittedBy = me.RequireId(),
            AttemptNo = attempt,
            BusinessSnapshot = snapshot,
            SubmittedAt = DateTime.UtcNow,
            Documents = req.Documents.Select(d => new VerificationDocument
            {
                DocumentType = d.DocumentType, FileUrl = d.FileUrl, FileName = d.FileName, MimeType = d.MimeType,
                FileSizeBytes = d.FileSizeBytes, UploadedAt = DateTime.UtcNow,
            }).ToList(),
        };
        db.VerificationRequests.Add(request);
        b.VerificationStatus = VerificationStatus.Submitted;
        audit.Log("business.verification.submit", "verification_request", request.Id, businessId: id,
            scope: RoleScope.Business, roleId: member.RoleId);
        await db.SaveChangesAsync();
        return ToSummary(request);
    }

    [HttpGet("{id:guid}/verification")]
    public async Task<ActionResult<List<VerificationSummaryDto>>> VerificationHistory(Guid id)
    {
        await access.RequireBusinessOrStaffAsync(id, P.BusinessView);
        var list = await db.VerificationRequests.Include(v => v.Documents).Where(v => v.BusinessId == id)
            .OrderByDescending(v => v.AttemptNo).ToListAsync();
        return list.Select(ToSummary).ToList();
    }

    /// <summary>Soft-deletes the business. Bookings and reviews are retained for history.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var member = await access.RequireBusinessOrStaffAsync(id, P.BusinessDelete);
        var hasUpcoming = await db.Bookings.AnyAsync(x => x.BusinessId == id && x.StartAt > DateTime.UtcNow &&
            (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed));
        if (hasUpcoming)
            throw new ConflictException("আসন্ন বুকিং থাকা অবস্থায় ব্যবসা মুছে ফেলা যাবে না। আগে সেগুলো বাতিল করুন।", "has_upcoming_bookings");
        var b = await db.Businesses.FirstAsync(x => x.Id == id);
        b.DeletedAt = DateTime.UtcNow;
        b.Status = BusinessStatus.Deactivated;
        await db.Users.Where(u => u.LastActiveBusinessId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveBusinessId, (Guid?)null)
                .SetProperty(u => u.LastWorkspaceType, WorkspaceType.Customer));
        audit.Log("business.delete", "business", id, businessId: id, scope: member.Scope, roleId: member.RoleId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------- helpers ----------------

    private static VerificationSummaryDto ToSummary(VerificationRequest v) => new(v.Id, v.AttemptNo, v.Status,
        v.RequiresInspection, v.DecisionReason, v.SubmittedAt, v.DecidedAt, v.Documents.Select(d => d.DocumentType).ToList());

    private async Task<BusinessDto> LoadDtoAsync(Guid id)
    {
        var userId = me.RequireId();
        var b = await db.Businesses.AsNoTracking()
            .Include(x => x.Category).Include(x => x.Zone).Include(x => x.Hours).Include(x => x.BookingSettings)
            .FirstAsync(x => x.Id == id);
        var myRole = await db.BusinessMembers.Where(m => m.BusinessId == id && m.UserId == userId && m.Status != MemberStatus.Removed)
            .Select(m => m.Role.Key).FirstOrDefaultAsync();
        // Platform staff opening a business they're not a member of: their granted permissions and role name.
        string? staffRole = null;
        var perms = myRole is not null ? await access.GetBusinessPermissionsAsync(id, userId) : await access.StaffPermissionsAsync(id);
        if (myRole is null) staffRole = (await access.StaffRoleAsync(id, P.BusinessView))?.NameBn;
        var s = b.BookingSettings!;
        return new BusinessDto(b.Id, b.Name, b.Slug, b.CategoryId, b.Category.NameBn, b.Tagline, b.ShortDescription,
            b.Description, b.EstablishedYear, b.LogoUrl, b.CoverUrl, b.Phone, b.Email, b.WebsiteUrl, b.FacebookUrl,
            b.AddressLine, b.ZoneId, b.Zone?.NameBn, b.Latitude, b.Longitude, b.CoverageNote, b.Languages,
            b.OffersHomeService, b.OffersEmergencyService, b.Status, b.VerificationStatus, b.OnboardingStep, b.AvgRating,
            b.ReviewCount, b.CompletedBookingCount,
            b.Hours.OrderBy(h => h.DayOfWeek).ThenBy(h => h.OpensAt).Select(h => new HourDto(h.DayOfWeek, h.OpensAt, h.ClosesAt)).ToList(),
            new BookingSettingsDto(s.AutoConfirm, s.SlotIntervalMinutes, s.MinNoticeMinutes, s.MaxAdvanceDays,
                s.BufferMinutes, s.MaxConcurrentBookings, s.CancellationPolicy),
            myRole ?? "", perms, staffRole);
    }

    private async Task<short> RoleIdAsync(string key) =>
        await db.Roles.Where(r => r.Key == key && r.BusinessId == null).Select(r => r.Id).FirstAsync();

    private async Task EnsureCategoryAsync(int id)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == id && c.Status == RecordStatus.Active))
            throw new BadRequestException("ক্যাটাগরি পাওয়া যায়নি।", "invalid_category");
    }

    private async Task EnsureZoneAsync(int id)
    {
        if (!await db.GeoZones.AnyAsync(z => z.Id == id && z.IsActive))
            throw new BadRequestException("এলাকাটি পাওয়া যায়নি।", "invalid_zone");
    }
}
