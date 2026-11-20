using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Features.Billing;

namespace ServiceHub.Api.Features.Services;

public record BookingFieldDto(Guid? Id, [Required, StringLength(120)] string Label, BookingFieldType FieldType,
    List<string>? Options, string? Placeholder, bool IsRequired, int SortOrder);

public record UpsertServiceRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [Range(1, int.MaxValue)] int CategoryId,
    [StringLength(300)] string? ShortDescription,
    string? Description,
    PriceType PriceType,
    [Range(0, 10_000_000)] decimal? Price,
    [Range(0, 10_000_000)] decimal? PriceMax,
    [Range(5, 1440)] short DurationMinutes,
    ServiceAvailability Availability = ServiceAvailability.Available,
    RecordStatus Status = RecordStatus.Active,
    bool IsFeatured = false,
    string? PreparationNotes = null,
    string? Policies = null,
    string? InternalNotes = null,
    List<BookingFieldDto>? BookingFields = null,
    [StringLength(500)] string? ImageUrl = null);

public record ServiceDto(Guid Id, Guid BusinessId, string Name, string Slug, int CategoryId, string? ShortDescription,
    string? Description, PriceType PriceType, decimal? Price, decimal? PriceMax, short DurationMinutes,
    ServiceAvailability Availability, RecordStatus Status, bool IsFeatured, string? PreparationNotes, string? Policies,
    string? InternalNotes, int BookingCount, decimal AvgRating, DateTime? LastBookedAt, List<BookingFieldDto> BookingFields,
    string? ImageUrl);

[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/services")]
public class ServicesController(AppDbContext db, AccessService access, AuditLogger audit, EntitlementService plans,
    Page.UploadCleanup cleanup) : ControllerBase
{
    private const int MaxFeatured = 3;

    public static ServiceDto ToDto(Service s, bool includeInternal) => new(s.Id, s.BusinessId, s.Name, s.Slug, s.CategoryId,
        s.ShortDescription, s.Description, s.PriceType, s.Price, s.PriceMax, s.DurationMinutes, s.Availability, s.Status,
        s.IsFeatured, s.PreparationNotes, s.Policies, includeInternal ? s.InternalNotes : null, s.BookingCount, s.AvgRating,
        s.LastBookedAt,
        s.BookingFields.OrderBy(f => f.SortOrder)
            .Select(f => new BookingFieldDto(f.Id, f.Label, f.FieldType, f.Options, f.Placeholder, f.IsRequired, f.SortOrder)).ToList(),
        s.ImageUrl);

    [HttpGet]
    public async Task<ActionResult<List<ServiceDto>>> List(Guid businessId, [FromQuery] RecordStatus? status)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.BusinessView);
        var q = db.Services.Include(s => s.BookingFields).Where(s => s.BusinessId == businessId);
        q = status is null ? q.Where(s => s.Status != RecordStatus.Archived) : q.Where(s => s.Status == status);
        var list = await q.OrderByDescending(s => s.IsFeatured).ThenBy(s => s.FeaturedOrder).ThenBy(s => s.Name).ToListAsync();
        return list.Select(s => ToDto(s, includeInternal: true)).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServiceDto>> Get(Guid businessId, Guid id)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.BusinessView);
        return ToDto(await LoadAsync(businessId, id), includeInternal: true);
    }

    [HttpPost]
    public async Task<ActionResult<ServiceDto>> Create(Guid businessId, UpsertServiceRequest req)
    {
        var member = await access.RequireBusinessOrStaffAsync(businessId, P.ServicesManage);
        await plans.RequireCapacityAsync(businessId, E.ServicesMax, await db.Services.CountAsync(x => x.BusinessId == businessId));
        await plans.RequireCapacityAsync(businessId, E.BookingFieldsMax, 0, req.BookingFields?.Count ?? 0);
        var s = new Service { BusinessId = businessId, Slug = await UniqueSlugAsync(businessId, req.Name, null) };
        await ApplyAsync(s, req);
        db.Services.Add(s);
        audit.Log("service.create", "service", s.Id, businessId: businessId, scope: member.Scope, roleId: member.RoleId);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { businessId, id = s.Id }, ToDto(s, includeInternal: true));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ServiceDto>> Update(Guid businessId, Guid id, UpsertServiceRequest req)
    {
        var member = await access.RequireBusinessOrStaffAsync(businessId, P.ServicesManage);
        var s = await LoadAsync(businessId, id);
        // Over-limit forms kept from a bigger plan stay editable; they just can't grow.
        var fields = req.BookingFields?.Count ?? 0;
        if (fields > s.BookingFields.Count) await plans.RequireCapacityAsync(businessId, E.BookingFieldsMax, 0, fields);
        if (s.Name != req.Name) s.Slug = await UniqueSlugAsync(businessId, req.Name, s.Id);
        var oldImage = s.ImageUrl;
        await ApplyAsync(s, req);
        audit.Log("service.update", "service", s.Id, businessId: businessId, scope: member.Scope, roleId: member.RoleId);
        await db.SaveChangesAsync();
        await cleanup.DeleteIfUnusedAsync(businessId, oldImage);
        return ToDto(s, includeInternal: true);
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<ServiceDto>> Duplicate(Guid businessId, Guid id)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.ServicesManage);
        await plans.RequireCapacityAsync(businessId, E.ServicesMax, await db.Services.CountAsync(x => x.BusinessId == businessId));
        var src = await LoadAsync(businessId, id);
        var name = src.Name + " (কপি)";
        var copy = new Service
        {
            BusinessId = businessId, CategoryId = src.CategoryId, Name = name, Slug = await UniqueSlugAsync(businessId, name, null),
            ShortDescription = src.ShortDescription, Description = src.Description, PriceType = src.PriceType, Price = src.Price,
            PriceMax = src.PriceMax, DurationMinutes = src.DurationMinutes, Availability = src.Availability,
            Status = RecordStatus.Inactive, PreparationNotes = src.PreparationNotes, Policies = src.Policies,
            InternalNotes = src.InternalNotes, ImageUrl = src.ImageUrl,
            BookingFields = src.BookingFields.Select(f => new ServiceBookingField
            {
                Label = f.Label, FieldType = f.FieldType, Options = f.Options, Placeholder = f.Placeholder,
                IsRequired = f.IsRequired, SortOrder = f.SortOrder,
            }).ToList(),
        };
        db.Services.Add(copy);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { businessId, id = copy.Id }, ToDto(copy, includeInternal: true));
    }

    /// <summary>Soft-deletes a service. Past bookings keep their snapshot of it.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid businessId, Guid id)
    {
        var member = await access.RequireBusinessOrStaffAsync(businessId, P.ServicesManage);
        var s = await LoadAsync(businessId, id);
        var upcoming = await db.Bookings.AnyAsync(b => b.ServiceId == id && b.StartAt > DateTime.UtcNow &&
            (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed));
        if (upcoming)
            throw new ConflictException("এই সেবার আসন্ন বুকিং আছে। আগে সেগুলো সম্পন্ন বা বাতিল করুন, অথবা সেবাটি আর্কাইভ করুন।", "has_upcoming_bookings");
        s.DeletedAt = DateTime.UtcNow;
        s.Status = RecordStatus.Archived;
        audit.Log("service.delete", "service", s.Id, businessId: businessId, scope: member.Scope, roleId: member.RoleId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task ApplyAsync(Service s, UpsertServiceRequest req)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId && c.Status == RecordStatus.Active))
            throw new BadRequestException("ক্যাটাগরি পাওয়া যায়নি।", "invalid_category");
        if (req.PriceType != PriceType.ContactForPrice && req.Price is null)
            throw new BadRequestException("মূল্য লিখুন।", "price_required");
        if (req.PriceType == PriceType.Range && (req.PriceMax is null || req.PriceMax < req.Price))
            throw new BadRequestException("সর্বোচ্চ মূল্য সর্বনিম্ন মূল্যের চেয়ে বেশি হতে হবে।", "invalid_price_range");
        ImageUrls.Ensure(req.ImageUrl);
        if (req.IsFeatured && !s.IsFeatured)
        {
            var featured = await db.Services.CountAsync(x => x.BusinessId == s.BusinessId && x.IsFeatured && x.Id != s.Id);
            if (featured >= MaxFeatured)
                throw new ConflictException("সর্বোচ্চ ৩টি সেবা ফিচার্ড করা যায়।", "featured_limit");
            s.FeaturedOrder = (short)(featured + 1);
        }
        if (!req.IsFeatured) s.FeaturedOrder = null;

        s.Name = req.Name.Trim();
        s.CategoryId = req.CategoryId;
        s.ShortDescription = req.ShortDescription;
        s.Description = req.Description;
        s.PriceType = req.PriceType;
        s.Price = req.PriceType == PriceType.ContactForPrice ? null : req.Price;
        s.PriceMax = req.PriceType == PriceType.Range ? req.PriceMax : null;
        s.DurationMinutes = req.DurationMinutes;
        s.Availability = req.Availability;
        s.Status = req.Status;
        s.IsFeatured = req.IsFeatured;
        s.PreparationNotes = req.PreparationNotes;
        s.Policies = req.Policies;
        s.InternalNotes = req.InternalNotes;
        s.ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl;

        if (req.BookingFields is not null)
        {
            foreach (var f in req.BookingFields)
                if (f.FieldType is BookingFieldType.Select or BookingFieldType.MultiSelect && (f.Options is null || f.Options.Count == 0))
                    throw new BadRequestException($"“{f.Label}” প্রশ্নের জন্য অপশন দিন।", "options_required");
            var keep = req.BookingFields.Where(f => f.Id != null).Select(f => f.Id!.Value).ToHashSet();
            s.BookingFields.RemoveAll(f => !keep.Contains(f.Id));
            foreach (var f in req.BookingFields)
            {
                var field = f.Id is null ? null : s.BookingFields.FirstOrDefault(x => x.Id == f.Id);
                if (field is null) { field = new ServiceBookingField(); s.BookingFields.Add(field); }
                field.Label = f.Label;
                field.FieldType = f.FieldType;
                field.Options = f.Options;
                field.Placeholder = f.Placeholder;
                field.IsRequired = f.IsRequired;
                field.SortOrder = f.SortOrder;
            }
        }
    }

    private async Task<Service> LoadAsync(Guid businessId, Guid id) =>
        await db.Services.Include(s => s.BookingFields).FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == businessId)
        ?? throw new NotFoundException("সেবাটি পাওয়া যায়নি।", "service_not_found");

    private async Task<string> UniqueSlugAsync(Guid businessId, string name, Guid? selfId)
    {
        var baseSlug = Slugs.Create(name, withSuffix: false);
        if (baseSlug.Length == 0) baseSlug = "service";
        var slug = baseSlug;
        for (var i = 2; await db.Services.IgnoreQueryFilters().AnyAsync(s => s.BusinessId == businessId && s.Slug == slug && s.Id != selfId); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }
}
