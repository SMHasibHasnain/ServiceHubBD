using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Features.Billing;

namespace ServiceHub.Api.Features.Page;

public record NoticeDto(Guid Id, string Title, string Body, bool IsPinned, bool IsPublished, DateTime? ExpiresAt,
    DateTime CreatedAt, DateTime UpdatedAt, bool IsLive);

public record FaqDto(Guid? Id, [Required, StringLength(200, MinimumLength = 2)] string Question,
    [Required, StringLength(1000, MinimumLength = 1)] string Answer);

public record HighlightDto(Guid? Id, [Required] string IconKey, [Required, StringLength(60, MinimumLength = 2)] string Title,
    [StringLength(160)] string? Description);

public record GalleryItemDto(Guid? Id, [Required, StringLength(500)] string ImageUrl, [StringLength(150)] string? Caption);

public record TeamMemberDto(Guid MemberId, string Name, string? Title, string? AvatarUrl, string RoleName, bool ShowOnProfile);

public record PageDto(string ThemeKey, string HeaderStyle, List<PageSection> Sections, List<NoticeDto> Notices,
    List<FaqDto> Faqs, List<HighlightDto> Highlights, List<GalleryItemDto> Gallery, List<TeamMemberDto> Team);

public record AppearanceRequest([Required] string ThemeKey, [Required] string HeaderStyle);

public record LayoutRequest([Required] List<PageSection> Sections);

public record NoticeRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Title,
    [Required, StringLength(1000, MinimumLength = 2)] string Body,
    bool IsPinned = false,
    bool IsPublished = true,
    DateTime? ExpiresAt = null);

public record TeamRequest([Required] List<Guid> MemberIds);

public record UploadResult(string Url);

/// <summary>
/// The owner's controls for the public page: look, section order and visibility, notice board, FAQs,
/// highlights, gallery, team showcase, and image uploads. Changes go live immediately; page text may not
/// carry phone numbers (bookings stay on the platform).
/// </summary>
[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}")]
public class PageController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit,
    EntitlementService plans, IFileStorage storage, IOptions<StorageOptions> storageOptions,
    Businesses.PublicProfileBuilder profiles, UploadCleanup cleanup, Notifier notifier) : ControllerBase
{
    [HttpGet("page")]
    public async Task<ActionResult<PageDto>> Get(Guid businessId)
    {
        await access.RequireBusinessAsync(businessId, P.BusinessView);
        return await LoadAsync(businessId);
    }

    /// <summary>
    /// The public page exactly as customers would see it, built by the same code as the public endpoint,
    /// for any status (drafts and pages under review included). Members only.
    /// </summary>
    [HttpGet("page/preview")]
    public async Task<ActionResult<Businesses.PublicBusinessDto>> Preview(Guid businessId)
    {
        await access.RequireBusinessAsync(businessId, P.BusinessView);
        return (await profiles.BuildAsync(x => x.Id == businessId))!;
    }

    [HttpPut("page/appearance")]
    public async Task<ActionResult<PageDto>> SetAppearance(Guid businessId, AppearanceRequest req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        if (!PageCatalog.Themes.Contains(req.ThemeKey) || !PageCatalog.HeaderStyles.Contains(req.HeaderStyle))
            throw new BadRequestException("থিম বা হেডারের ধরনটি সঠিক নয়।", "invalid_theme");
        var b = await db.Businesses.FirstAsync(x => x.Id == businessId);
        b.ThemeKey = req.ThemeKey;
        b.HeaderStyle = req.HeaderStyle;
        Audit("appearance", businessId, member);
        await db.SaveChangesAsync();
        return await LoadAsync(businessId);
    }

    /// <summary>
    /// Shows/hides optional sections (every plan) and reorders sections (plans with page.layout).
    /// A layout saved on a bigger plan keeps working after a downgrade; only changing the order is blocked.
    /// </summary>
    [HttpPut("page/layout")]
    public async Task<ActionResult<PageDto>> SetLayout(Guid businessId, LayoutRequest req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        foreach (var s in req.Sections)
        {
            if (!PageCatalog.DefaultOrder.Contains(s.Key))
                throw new BadRequestException("অজানা সেকশন।", "invalid_section");
            if (!s.Visible && !PageCatalog.Hideable.Contains(s.Key))
                throw new BadRequestException("সেবা, রিভিউ ও পরিচিতি সেকশন লুকানো যায় না।", "section_required");
        }
        var b = await db.Businesses.FirstAsync(x => x.Id == businessId);
        var next = PageCatalog.Normalize(req.Sections);
        var current = PageCatalog.Normalize(b.PageLayout);
        if (!next.Select(s => s.Key).SequenceEqual(current.Select(s => s.Key)))
            await plans.RequireFeatureAsync(businessId, E.PageLayout);
        b.PageLayout = next;
        Audit("layout", businessId, member);
        await db.SaveChangesAsync();
        return await LoadAsync(businessId);
    }

    // ---------------- notice board ----------------

    [HttpPost("page/notices")]
    public async Task<ActionResult<NoticeDto>> AddNotice(Guid businessId, NoticeRequest req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        var notice = new BusinessNotice { BusinessId = businessId, CreatedBy = me.RequireId() };
        await ApplyNoticeAsync(notice, req, wasLive: false);
        db.BusinessNotices.Add(notice);
        Audit("notice.create", businessId, member, notice.Id);
        await NotifyFollowersAsync(notice);
        await db.SaveChangesAsync();
        return ToDto(notice, DateTime.UtcNow);
    }

    [HttpPut("page/notices/{noticeId:guid}")]
    public async Task<ActionResult<NoticeDto>> UpdateNotice(Guid businessId, Guid noticeId, NoticeRequest req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        var notice = await FindNoticeAsync(businessId, noticeId);
        await ApplyNoticeAsync(notice, req, wasLive: IsLive(notice, DateTime.UtcNow));
        Audit("notice.update", businessId, member, notice.Id);
        await NotifyFollowersAsync(notice);
        await db.SaveChangesAsync();
        return ToDto(notice, DateTime.UtcNow);
    }

    [HttpDelete("page/notices/{noticeId:guid}")]
    public async Task<IActionResult> DeleteNotice(Guid businessId, Guid noticeId)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        db.BusinessNotices.Remove(await FindNoticeAsync(businessId, noticeId));
        Audit("notice.delete", businessId, member, noticeId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------- FAQs, highlights, gallery, team ----------------

    [HttpPut("page/faqs")]
    public async Task<ActionResult<PageDto>> SetFaqs(Guid businessId, List<FaqDto> faqs)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        if (faqs.Count > PageCatalog.MaxFaqs)
            throw new BadRequestException($"সর্বোচ্চ {EntitlementService.Bn(PageCatalog.MaxFaqs)}টি প্রশ্ন রাখা যায়।", "too_many_faqs");
        ContactGuard.Ensure(faqs.SelectMany(f => new[] { f.Question, f.Answer }).ToArray());
        db.BusinessFaqs.RemoveRange(await db.BusinessFaqs.Where(f => f.BusinessId == businessId).ToListAsync());
        db.BusinessFaqs.AddRange(faqs.Select((f, i) => new BusinessFaq
        {
            BusinessId = businessId, Question = f.Question.Trim(), Answer = f.Answer.Trim(), SortOrder = (short)i,
        }));
        Audit("faqs", businessId, member);
        await db.SaveChangesAsync();
        return await LoadAsync(businessId);
    }

    [HttpPut("page/highlights")]
    public async Task<ActionResult<PageDto>> SetHighlights(Guid businessId, List<HighlightDto> highlights)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        if (highlights.Count > PageCatalog.MaxHighlights)
            throw new BadRequestException($"সর্বোচ্চ {EntitlementService.Bn(PageCatalog.MaxHighlights)}টি বিশেষত্ব রাখা যায়।", "too_many_highlights");
        if (highlights.Any(h => !PageCatalog.HighlightIcons.Contains(h.IconKey)))
            throw new BadRequestException("আইকনটি সঠিক নয়।", "invalid_icon");
        ContactGuard.Ensure(highlights.SelectMany(h => new[] { h.Title, h.Description }).ToArray());
        db.BusinessHighlights.RemoveRange(await db.BusinessHighlights.Where(h => h.BusinessId == businessId).ToListAsync());
        db.BusinessHighlights.AddRange(highlights.Select((h, i) => new BusinessHighlight
        {
            BusinessId = businessId, IconKey = h.IconKey, Title = h.Title.Trim(), Description = h.Description?.Trim(), SortOrder = (short)i,
        }));
        Audit("highlights", businessId, member);
        await db.SaveChangesAsync();
        return await LoadAsync(businessId);
    }

    /// <summary>Replaces the gallery (order = list order). Removed uploads nothing else uses are deleted from storage.</summary>
    [HttpPut("page/gallery")]
    public async Task<ActionResult<PageDto>> SetGallery(Guid businessId, List<GalleryItemDto> items)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        var existing = await db.GalleryItems.Where(g => g.BusinessId == businessId).ToListAsync();
        // Over-limit galleries kept from a bigger plan stay editable; they just can't grow.
        if (items.Count > existing.Count)
            await plans.RequireCapacityAsync(businessId, E.GalleryMax, existing.Count, items.Count - existing.Count);
        foreach (var item in items) ImageUrls.Ensure(item.ImageUrl);
        ContactGuard.Ensure(items.Select(i => i.Caption).ToArray());

        db.GalleryItems.RemoveRange(existing);
        db.GalleryItems.AddRange(items.Select((g, i) => new GalleryItem
        {
            BusinessId = businessId, ImageUrl = g.ImageUrl, Caption = string.IsNullOrWhiteSpace(g.Caption) ? null : g.Caption.Trim(), SortOrder = (short)i,
        }));
        Audit("gallery", businessId, member);
        await db.SaveChangesAsync();
        await cleanup.DeleteIfUnusedAsync(businessId, existing.Select(e => e.ImageUrl).ToArray());
        return await LoadAsync(businessId);
    }

    /// <summary>Chooses which active members appear in the public "team" section.</summary>
    [HttpPut("page/team")]
    public async Task<ActionResult<PageDto>> SetTeam(Guid businessId, TeamRequest req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        var members = await db.BusinessMembers.Where(m => m.BusinessId == businessId && m.Status == MemberStatus.Active).ToListAsync();
        if (req.MemberIds.Any(id => members.All(m => m.Id != id)))
            throw new BadRequestException("সদস্যটি পাওয়া যায়নি।", "member_not_found");
        foreach (var m in members) m.ShowOnProfile = req.MemberIds.Contains(m.Id);
        Audit("team", businessId, member);
        await db.SaveChangesAsync();
        return await LoadAsync(businessId);
    }

    // ---------------- uploads ----------------

    /// <summary>
    /// Uploads one image (JPG, PNG or WebP, checked by content) for this business's page: logo, cover,
    /// gallery or a service photo. Returns its URL, which the caller then saves on the right field.
    /// </summary>
    [HttpPost("uploads")]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<UploadResult>> Upload(Guid businessId, CancellationToken ct)
    {
        if (!await access.HasBusinessPermissionAsync(businessId, P.ServicesManage))
            await access.RequireBusinessAsync(businessId, P.BusinessUpdate);
        var (url, _) = await storage.SaveAsync(Request, Folder(businessId), storageOptions.Value.MaxImageBytes, ct);
        return new UploadResult(url);
    }

    // ---------------- helpers ----------------

    public static string Folder(Guid businessId) => businessId.ToString();

    public static bool IsLive(BusinessNotice n, DateTime now) => n.IsPublished && (n.ExpiresAt == null || n.ExpiresAt > now);

    public static NoticeDto ToDto(BusinessNotice n, DateTime now) =>
        new(n.Id, n.Title, n.Body, n.IsPinned, n.IsPublished, n.ExpiresAt, n.CreatedAt, n.UpdatedAt, IsLive(n, now));

    private async Task ApplyNoticeAsync(BusinessNotice notice, NoticeRequest req, bool wasLive)
    {
        var now = DateTime.UtcNow;
        ContactGuard.Ensure(req.Title, req.Body);
        if (req.ExpiresAt is { } exp && exp <= now && req.IsPublished)
            throw new BadRequestException("মেয়াদ শেষের সময় ভবিষ্যতে হতে হবে।", "invalid_expiry");

        notice.Title = req.Title.Trim();
        notice.Body = req.Body.Trim();
        notice.IsPublished = req.IsPublished;
        notice.ExpiresAt = req.ExpiresAt?.ToUniversalTime();
        notice.IsPinned = req.IsPinned;

        // Only notices that become live count against the plan; already-live ones stay editable after a downgrade.
        if (IsLive(notice, now) && !wasLive)
        {
            var live = await db.BusinessNotices.CountAsync(n => n.BusinessId == notice.BusinessId && n.Id != notice.Id &&
                n.IsPublished && (n.ExpiresAt == null || n.ExpiresAt > now));
            await plans.RequireCapacityAsync(notice.BusinessId, E.NoticesMax, live);
        }
        if (notice.IsPinned)
        {
            var pinned = await db.BusinessNotices.Where(n => n.BusinessId == notice.BusinessId && n.IsPinned && n.Id != notice.Id).ToListAsync();
            foreach (var n in pinned) n.IsPinned = false;
        }
    }

    /// <summary>
    /// Tells customers who saved this business about a notice the first time it is live on a published page with a
    /// visible notice board. Each notice notifies once, so edits don't spam followers.
    /// </summary>
    private async Task NotifyFollowersAsync(BusinessNotice notice)
    {
        if (notice.NotifiedAt is not null || !IsLive(notice, DateTime.UtcNow)) return;
        var b = await db.Businesses.AsNoTracking().Where(x => x.Id == notice.BusinessId)
            .Select(x => new { x.Name, x.Slug, x.Status, x.PageLayout }).FirstAsync();
        if (b.Status != BusinessStatus.Published || !PageCatalog.IsVisible(PageCatalog.Normalize(b.PageLayout), PageCatalog.Notices)) return;

        var members = db.BusinessMembers.Where(m => m.BusinessId == notice.BusinessId).Select(m => m.UserId);
        var followers = await db.Favourites.Where(f => f.BusinessId == notice.BusinessId && !members.Contains(f.UserId))
            .Select(f => f.UserId).ToListAsync();
        foreach (var userId in followers)
            notifier.Notify(userId, WorkspaceType.Customer, null, "business.notice", $"{b.Name}: {notice.Title}",
                notice.Body.Length > 140 ? notice.Body[..140] + "…" : notice.Body, $"/b/{b.Slug}");
        notice.NotifiedAt = DateTime.UtcNow;
    }

    private async Task<BusinessNotice> FindNoticeAsync(Guid businessId, Guid noticeId) =>
        await db.BusinessNotices.FirstOrDefaultAsync(n => n.Id == noticeId && n.BusinessId == businessId)
        ?? throw new NotFoundException("নোটিশটি পাওয়া যায়নি।", "notice_not_found");

    private void Audit(string what, Guid businessId, BusinessMember member, Guid? entityId = null) =>
        audit.Log($"business.page.{what}", entityId is null ? "business" : "business_notice", entityId ?? businessId,
            businessId: businessId, scope: RoleScope.Business, roleId: member.RoleId);

    private async Task<PageDto> LoadAsync(Guid businessId)
    {
        var now = DateTime.UtcNow;
        var b = await db.Businesses.AsNoTracking().FirstAsync(x => x.Id == businessId);
        var notices = await db.BusinessNotices.AsNoTracking().Where(n => n.BusinessId == businessId)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt).ToListAsync();
        var faqs = await db.BusinessFaqs.AsNoTracking().Where(f => f.BusinessId == businessId).OrderBy(f => f.SortOrder)
            .Select(f => new FaqDto(f.Id, f.Question, f.Answer)).ToListAsync();
        var highlights = await db.BusinessHighlights.AsNoTracking().Where(h => h.BusinessId == businessId).OrderBy(h => h.SortOrder)
            .Select(h => new HighlightDto(h.Id, h.IconKey, h.Title, h.Description)).ToListAsync();
        var gallery = await db.GalleryItems.AsNoTracking().Where(g => g.BusinessId == businessId).OrderBy(g => g.SortOrder)
            .Select(g => new GalleryItemDto(g.Id, g.ImageUrl, g.Caption)).ToListAsync();
        var team = await db.BusinessMembers.AsNoTracking().Where(m => m.BusinessId == businessId && m.Status == MemberStatus.Active)
            .OrderByDescending(m => m.Role.Rank).ThenBy(m => m.JoinedAt)
            .Select(m => new TeamMemberDto(m.Id, m.User.FullName, m.Title, m.User.AvatarUrl, m.Role.NameBn, m.ShowOnProfile)).ToListAsync();
        return new PageDto(b.ThemeKey ?? "blue", b.HeaderStyle ?? "cover", PageCatalog.Normalize(b.PageLayout),
            notices.Select(n => ToDto(n, now)).ToList(), faqs, highlights, gallery, team);
    }
}
