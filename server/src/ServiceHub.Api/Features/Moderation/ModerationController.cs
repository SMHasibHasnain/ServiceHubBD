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
using ServiceHub.Api.Features.Inspections;
using ServiceHub.Api.Features.Reviews;
using ServiceHub.Api.Features.Billing;

namespace ServiceHub.Api.Features.Moderation;

public record DecisionRequest([Required, StringLength(2000, MinimumLength = 5)] string Reason);

public record InspectionRequestBody([Required, StringLength(2000, MinimumLength = 5)] string Reason, DateTime? ScheduledFor,
    PriorityLevel Priority = PriorityLevel.Normal);

public record QueueItemDto(Guid Id, Guid BusinessId, string BusinessName, string? LogoUrl, string OwnerName, string CategoryName,
    string? ZoneName, short AttemptNo, VerificationStatus Status, PriorityLevel Priority, bool RequiresInspection,
    Guid? AssignedModeratorId, DateTime SubmittedAt);

public record DocumentDto(Guid Id, DocumentType DocumentType, string FileUrl, string? FileName, DocumentStatus Status, string? ReviewerNote);

public record InspectionBriefDto(Guid Id, InspectionStatus Status, InspectionOutcome? Outcome, string? Findings,
    string? InspectorName, DateTime? VisitedAt, List<Inspections.InspectionPhotoDto> Photos);

public record ActionDto(Guid Id, ModerationActionType Action, string Reason, string ModeratorName, DateTime CreatedAt);

public record VerificationDetailDto(QueueItemDto Request, JsonDocument Snapshot, string? DecisionReason, List<DocumentDto> Documents,
    List<InspectionBriefDto> Inspections, List<QueueItemDto> PreviousAttempts, List<ActionDto> History);

public record ResolveReportRequest(
    [Required, StringLength(2000, MinimumLength = 5)] string Reason,
    ReportResolution Resolution);

public enum ReportResolution { Dismiss, Resolve, HideReview, RemoveReview, RemoveContent, SuspendBusiness, Escalate, RequestInspection }

public record ReportDto(Guid Id, ReportTarget TargetType, Guid? TargetBusinessId, string? TargetBusinessName, Guid? TargetReviewId,
    string? ReviewComment, Guid? TargetUserId, ReportReason Reason, string? Description, PriorityLevel Severity,
    ReportStatus Status, int SameTargetReports, DateTime CreatedAt, ReportContentDto? Content = null);

/// <summary>The reported page item as it is now. <c>Removed</c> when the owner (or a moderator) already deleted it.</summary>
public record ReportContentDto(string Type, Guid Id, bool Removed, string? Title, string? Text, string? ImageUrl);

/// <summary>
/// Moderators review, approve, reject or request changes — they never edit business data.
/// Every decision needs a reason and is recorded in moderation_actions + audit_logs.
/// </summary>
[ApiController]
[Authorize]
[Route("api/moderation")]
public class ModerationController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit, Notifier notifier,
    InspectionRouter router, SubscriptionService subscriptions, Page.UploadCleanup cleanup) : ControllerBase
{
    [HttpGet("verifications")]
    public async Task<ActionResult<PagedResult<QueueItemDto>>> Queue([FromQuery] VerificationStatus? status,
        [FromQuery] bool assignedToMe, [FromQuery] PageQuery paging)
    {
        await access.RequirePlatformAsync(P.VerificationReview);
        var userId = me.RequireId();
        var scoped = access.ScopedBusinesses(P.VerificationReview).Select(b => b.Id);
        var mine = db.BusinessMembers.Where(m => m.UserId == userId && m.Status != MemberStatus.Removed).Select(m => m.BusinessId);
        var q = db.VerificationRequests.Where(v => scoped.Contains(v.BusinessId) && !mine.Contains(v.BusinessId));
        q = status is null
            ? q.Where(v => v.Status == VerificationStatus.Submitted || v.Status == VerificationStatus.UnderReview)
            : q.Where(v => v.Status == status);
        if (assignedToMe) q = q.Where(v => v.AssignedModeratorId == userId);
        var total = await q.CountAsync();
        var items = await Project(q.OrderByDescending(v => v.Priority).ThenBy(v => v.SubmittedAt).Skip(paging.Skip).Take(paging.SafeSize))
            .ToListAsync();
        return new PagedResult<QueueItemDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    [HttpGet("verifications/{id:guid}")]
    public async Task<ActionResult<VerificationDetailDto>> Detail(Guid id)
    {
        var v = await LoadAsync(id);
        var item = await Project(db.VerificationRequests.Where(x => x.Id == id)).FirstAsync();
        var previous = await Project(db.VerificationRequests.Where(x => x.BusinessId == v.BusinessId && x.Id != id)
            .OrderByDescending(x => x.AttemptNo)).ToListAsync();
        var inspections = await db.Inspections.Where(i => i.VerificationRequestId == id || (i.BusinessId == v.BusinessId && i.Type == InspectionType.VerificationVisit))
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InspectionBriefDto(i.Id, i.Status, i.Outcome, i.Findings,
                db.Users.Where(u => u.Id == i.InspectorId).Select(u => u.FullName).FirstOrDefault(), i.VisitedAt,
                i.Photos.OrderBy(p => p.CreatedAt).Select(p => new Inspections.InspectionPhotoDto(p.Id, p.ImageUrl, p.Caption)).ToList()))
            .ToListAsync();
        var history = await db.ModerationActions.Where(a => a.BusinessId == v.BusinessId).OrderByDescending(a => a.CreatedAt)
            .Join(db.Users, a => a.ModeratorId, u => u.Id, (a, u) => new ActionDto(a.Id, a.Action, a.Reason, u.FullName, a.CreatedAt))
            .ToListAsync();
        return new VerificationDetailDto(item, v.BusinessSnapshot, v.DecisionReason,
            v.Documents.Select(d => new DocumentDto(d.Id, d.DocumentType, d.FileUrl, d.FileName, d.Status, d.ReviewerNote)).ToList(),
            inspections, previous, history);
    }

    /// <summary>Claims a request: moves it to under_review and assigns it to me.</summary>
    [HttpPost("verifications/{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id)
    {
        var v = await LoadAsync(id);
        if (v.Status != VerificationStatus.Submitted && !(v.Status == VerificationStatus.UnderReview && v.AssignedModeratorId == null))
            throw new ConflictException("এই আবেদনটি ইতিমধ্যে পর্যালোচনায় আছে।", "already_in_review");
        v.Status = VerificationStatus.UnderReview;
        v.AssignedModeratorId = me.RequireId();
        v.ReviewStartedAt = DateTime.UtcNow;
        v.Business.VerificationStatus = VerificationStatus.UnderReview;
        Record(ModerationActionType.StartReview, "পর্যালোচনা শুরু", v);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("verifications/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, DecisionRequest req)
    {
        var v = await DecidableAsync(id);
        var pending = await db.Inspections.AnyAsync(i => i.VerificationRequestId == id &&
            (i.Status == InspectionStatus.Scheduled || i.Status == InspectionStatus.InProgress));
        if (pending) throw new ConflictException("মাঠ পরিদর্শনের ফলাফলের অপেক্ষায় আছে।", "inspection_pending");

        v.Status = VerificationStatus.Approved;
        v.DecisionReason = req.Reason;
        v.DecidedAt = DateTime.UtcNow;
        v.Business.VerificationStatus = VerificationStatus.Approved;
        v.Business.Status = BusinessStatus.Published;
        v.Business.PublishedAt ??= DateTime.UtcNow;
        foreach (var d in v.Documents.Where(d => d.Status == DocumentStatus.Pending)) d.Status = DocumentStatus.Accepted;
        Record(ModerationActionType.ApproveBusiness, req.Reason, v);
        notifier.Notify(v.Business.OwnerId, WorkspaceType.Business, v.BusinessId, "verification.approved",
            "অভিনন্দন! আপনার ব্যবসা যাচাইকৃত হয়েছে।", "আপনার পেজ এখন সবার জন্য প্রকাশিত।");
        await subscriptions.StartTrialAsync(v.Business);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("verifications/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, DecisionRequest req)
    {
        var v = await DecidableAsync(id);
        v.Status = VerificationStatus.Rejected;
        v.DecisionReason = req.Reason;
        v.DecidedAt = DateTime.UtcNow;
        v.Business.VerificationStatus = VerificationStatus.Rejected;
        Record(ModerationActionType.RejectBusiness, req.Reason, v);
        notifier.Notify(v.Business.OwnerId, WorkspaceType.Business, v.BusinessId, "verification.rejected",
            "কিছু তথ্য সংশোধনের প্রয়োজন রয়েছে।", req.Reason);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("verifications/{id:guid}/request-info")]
    public async Task<IActionResult> RequestInfo(Guid id, DecisionRequest req)
    {
        var v = await DecidableAsync(id);
        v.Status = VerificationStatus.InfoRequested;
        v.DecisionReason = req.Reason;
        v.DecidedAt = DateTime.UtcNow;
        v.Business.VerificationStatus = VerificationStatus.InfoRequested;
        Record(ModerationActionType.RequestInformation, req.Reason, v);
        notifier.Notify(v.Business.OwnerId, WorkspaceType.Business, v.BusinessId, "verification.info_requested",
            "যাচাইকরণের জন্য আরও তথ্য প্রয়োজন।", req.Reason);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Asks the zone inspector to visit before deciding. Routed to an inspector covering the business's zone.</summary>
    [HttpPost("verifications/{id:guid}/request-inspection")]
    public async Task<ActionResult<Guid>> RequestInspection(Guid id, InspectionRequestBody req)
    {
        var v = await DecidableAsync(id);
        await access.RequireOnBusinessAsync(P.InspectionsRequest, v.BusinessId);
        var inspection = await router.CreateAsync(v.Business, InspectionType.VerificationVisit, v.Id, null, req.ScheduledFor);
        v.RequiresInspection = true;
        v.Priority = req.Priority;
        Record(ModerationActionType.RequestInspection, req.Reason, v, inspectionId: inspection.Id);
        await db.SaveChangesAsync();
        return inspection.Id;
    }

    // ---------------- reports ----------------

    [HttpGet("reports")]
    public async Task<ActionResult<PagedResult<ReportDto>>> Reports([FromQuery] ReportStatus? status, [FromQuery] PageQuery paging)
    {
        await access.RequirePlatformAsync(P.ReportsReview);
        var scoped = access.ScopedBusinesses(P.ReportsReview).Select(b => b.Id);
        var global = await access.HasGlobalPermissionAsync(P.ReportsReview);
        var q = db.Reports.Where(r =>
            (r.TargetBusinessId != null && scoped.Contains(r.TargetBusinessId.Value)) ||
            (r.TargetReviewId != null && scoped.Contains(r.TargetReview!.BusinessId)) ||
            (r.TargetUserId != null && global));
        q = status is null ? q.Where(r => r.Status == ReportStatus.Open || r.Status == ReportStatus.UnderReview || r.Status == ReportStatus.Escalated)
                           : q.Where(r => r.Status == status);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.Severity).ThenBy(r => r.CreatedAt).Skip(paging.Skip).Take(paging.SafeSize)
            .Select(r => new ReportDto(r.Id, r.TargetType, r.TargetBusinessId, r.TargetBusiness != null ? r.TargetBusiness.Name : null,
                r.TargetReviewId, r.TargetReview != null ? r.TargetReview.Comment : null, r.TargetUserId, r.Reason, r.Description,
                r.Severity, r.Status,
                db.Reports.Count(o => (o.TargetBusinessId != null && o.TargetBusinessId == r.TargetBusinessId) ||
                                      (o.TargetReviewId != null && o.TargetReviewId == r.TargetReviewId) ||
                                      (o.TargetUserId != null && o.TargetUserId == r.TargetUserId)),
                r.CreatedAt))
            .ToListAsync();
        // Reported page items (notice / gallery photo), loaded in two small queries.
        var targets = await db.Reports.Where(r => items.Select(i => i.Id).Contains(r.Id) && r.TargetContentId != null)
            .Select(r => new { r.Id, r.TargetContentType, ContentId = r.TargetContentId!.Value }).ToListAsync();
        var ids = targets.Select(t => t.ContentId).ToList();
        var notices = await db.BusinessNotices.Where(n => ids.Contains(n.Id)).ToDictionaryAsync(n => n.Id);
        var photos = await db.GalleryItems.Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id);
        items = items.Select(i =>
        {
            var t = targets.FirstOrDefault(x => x.Id == i.Id);
            if (t is null) return i;
            ReportContentDto content = t.TargetContentType == Page.PageCatalog.ContentNotice
                ? notices.TryGetValue(t.ContentId, out var n) ? new(t.TargetContentType, t.ContentId, false, n.Title, n.Body, null) : new(t.TargetContentType, t.ContentId, true, null, null, null)
                : photos.TryGetValue(t.ContentId, out var g) ? new(t.TargetContentType!, t.ContentId, false, g.Caption, null, g.ImageUrl) : new(t.TargetContentType!, t.ContentId, true, null, null, null);
            return i with { Content = content };
        }).ToList();
        return new PagedResult<ReportDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    [HttpPost("reports/{id:guid}/resolve")]
    public async Task<IActionResult> ResolveReport(Guid id, ResolveReportRequest req)
    {
        var report = await db.Reports.Include(r => r.TargetReview).FirstOrDefaultAsync(r => r.Id == id)
                     ?? throw new NotFoundException("রিপোর্টটি পাওয়া যায়নি।", "report_not_found");
        var businessId = report.TargetBusinessId ?? report.TargetReview?.BusinessId;
        if (businessId is not null) await access.RequireOnBusinessAsync(P.ReportsReview, businessId.Value);
        else await access.RequireGlobalAsync(P.ReportsReview);
        if (report.Status is ReportStatus.Resolved or ReportStatus.Dismissed)
            throw new ConflictException("রিপোর্টটি ইতিমধ্যে নিষ্পত্তি হয়েছে।", "report_closed");

        var action = ModerationActionType.ResolveReport;
        Guid? inspectionId = null;
        string? removedFile = null;
        switch (req.Resolution)
        {
            case ReportResolution.Dismiss:
                report.Status = ReportStatus.Dismissed;
                action = ModerationActionType.DismissReport;
                break;
            case ReportResolution.Escalate:
                report.Status = ReportStatus.Escalated;
                action = ModerationActionType.EscalateReport;
                break;
            case ReportResolution.HideReview or ReportResolution.RemoveReview:
                if (report.TargetReview is null) throw new BadRequestException("এই রিপোর্টটি কোনো রিভিউ নিয়ে নয়।", "not_a_review_report");
                await access.RequireOnBusinessAsync(P.ReviewsModerate, report.TargetReview.BusinessId);
                report.TargetReview.Status = req.Resolution == ReportResolution.HideReview ? ReviewStatus.Hidden : ReviewStatus.Removed;
                action = req.Resolution == ReportResolution.HideReview ? ModerationActionType.HideReview : ModerationActionType.RemoveReview;
                report.Status = ReportStatus.Resolved;
                await db.SaveChangesAsync();
                await ReviewsController.RecalculateRatingsAsync(db, report.TargetReview.BusinessId, report.TargetReview.ServiceId);
                break;
            case ReportResolution.RemoveContent:
                if (report.TargetContentId is null || businessId is null)
                    throw new BadRequestException("এই রিপোর্টটি কোনো নোটিশ বা ছবি নিয়ে নয়।", "not_a_content_report");
                removedFile = await RemoveContentAsync(businessId.Value, await ContentAsync(report), req.Reason);
                report.Status = ReportStatus.Resolved;
                action = ModerationActionType.RemoveContent;
                break;
            case ReportResolution.SuspendBusiness:
                if (businessId is null) throw new BadRequestException("এই রিপোর্টটি কোনো ব্যবসা নিয়ে নয়।", "not_a_business_report");
                await SuspendAsync(businessId.Value, req.Reason);
                report.Status = ReportStatus.Resolved;
                action = ModerationActionType.SuspendBusiness;
                break;
            case ReportResolution.RequestInspection:
                if (businessId is null) throw new BadRequestException("এই রিপোর্টটি কোনো ব্যবসা নিয়ে নয়।", "not_a_business_report");
                await access.RequireOnBusinessAsync(P.InspectionsRequest, businessId.Value);
                var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
                inspectionId = (await router.CreateAsync(business, InspectionType.ComplaintFollowup, null, report.Id, null)).Id;
                report.Status = ReportStatus.UnderReview;
                action = ModerationActionType.RequestInspection;
                break;
            default:
                report.Status = ReportStatus.Resolved;
                break;
        }
        report.AssignedModeratorId = me.RequireId();
        report.ResolutionNote = req.Reason;
        if (report.Status is ReportStatus.Resolved or ReportStatus.Dismissed) report.ResolvedAt = DateTime.UtcNow;
        db.ModerationActions.Add(new ModerationAction
        {
            ModeratorId = me.RequireId(), Action = action, Reason = req.Reason, ReportId = report.Id, BusinessId = businessId,
            ReviewId = report.TargetReviewId, InspectionId = inspectionId,
        });
        audit.Log($"report.{SnakeCase.FromEnum(req.Resolution)}", "report", report.Id, req.Reason, businessId, scope: RoleScope.Platform);
        await db.SaveChangesAsync();
        if (removedFile is not null && businessId is not null) await cleanup.DeleteIfUnusedAsync(businessId.Value, removedFile);
        return NoContent();
    }

    private async Task<object?> ContentAsync(Report report) => report.TargetContentType switch
    {
        Page.PageCatalog.ContentNotice => await db.BusinessNotices.FirstOrDefaultAsync(n => n.Id == report.TargetContentId && n.BusinessId == report.TargetBusinessId),
        Page.PageCatalog.ContentGalleryItem => await db.GalleryItems.FirstOrDefaultAsync(g => g.Id == report.TargetContentId && g.BusinessId == report.TargetBusinessId),
        _ => null,
    };

    /// <summary>
    /// Takes a reported notice or gallery photo off the page (in the caller's unit of work) and tells the owner why.
    /// An item the owner already deleted is fine. Returns the photo URL to clean up after saving, if any.
    /// </summary>
    private async Task<string?> RemoveContentAsync(Guid businessId, object? item, string reason)
    {
        string what, tab;
        string? file = null;
        switch (item)
        {
            case BusinessNotice notice:
                db.BusinessNotices.Remove(notice);
                what = $"নোটিশ “{notice.Title}”";
                tab = "notices";
                break;
            case GalleryItem photo:
                db.GalleryItems.Remove(photo);
                file = photo.ImageUrl;
                what = "গ্যালারির একটি ছবি";
                tab = "gallery";
                break;
            default:
                return null;
        }
        var ownerId = await db.Businesses.Where(b => b.Id == businessId).Select(b => b.OwnerId).FirstAsync();
        notifier.Notify(ownerId, WorkspaceType.Business, businessId, "business.content_removed",
            $"মডারেটর আপনার পেজ থেকে {what} সরিয়েছেন।", reason, $"/w/{businessId}/page/{tab}");
        return file;
    }

    [HttpPost("businesses/{businessId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid businessId, DecisionRequest req)
    {
        await SuspendAsync(businessId, req.Reason);
        db.ModerationActions.Add(new ModerationAction { ModeratorId = me.RequireId(), Action = ModerationActionType.SuspendBusiness, Reason = req.Reason, BusinessId = businessId });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("businesses/{businessId:guid}/reinstate")]
    public async Task<IActionResult> Reinstate(Guid businessId, DecisionRequest req)
    {
        await access.RequireOnBusinessAsync(P.BusinessesSuspend, businessId);
        var b = await db.Businesses.FirstAsync(x => x.Id == businessId);
        if (b.Status != BusinessStatus.Suspended) throw new ConflictException("ব্যবসাটি স্থগিত নয়।", "not_suspended");
        b.Status = b.VerificationStatus == VerificationStatus.Approved ? BusinessStatus.Published : BusinessStatus.Draft;
        db.ModerationActions.Add(new ModerationAction { ModeratorId = me.RequireId(), Action = ModerationActionType.ReinstateBusiness, Reason = req.Reason, BusinessId = businessId });
        audit.Log("business.reinstate", "business", businessId, req.Reason, businessId, scope: RoleScope.Platform);
        notifier.Notify(b.OwnerId, WorkspaceType.Business, businessId, "business.reinstated", "আপনার ব্যবসা পুনরায় সক্রিয় করা হয়েছে।", req.Reason);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("actions")]
    public async Task<ActionResult<PagedResult<ActionDto>>> History([FromQuery] bool mine, [FromQuery] PageQuery paging)
    {
        await access.RequirePlatformAsync(P.VerificationReview);
        var userId = me.RequireId();
        var q = db.ModerationActions.AsQueryable();
        if (mine || !await access.HasGlobalPermissionAsync(P.VerificationReview)) q = q.Where(a => a.ModeratorId == userId);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.CreatedAt).Skip(paging.Skip).Take(paging.SafeSize)
            .Join(db.Users, a => a.ModeratorId, u => u.Id, (a, u) => new ActionDto(a.Id, a.Action, a.Reason, u.FullName, a.CreatedAt))
            .ToListAsync();
        return new PagedResult<ActionDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    // ---------------- helpers ----------------

    private async Task SuspendAsync(Guid businessId, string reason)
    {
        await access.RequireOnBusinessAsync(P.BusinessesSuspend, businessId);
        var b = await db.Businesses.FirstOrDefaultAsync(x => x.Id == businessId) ?? throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।");
        if (b.Status == BusinessStatus.Suspended) throw new ConflictException("ব্যবসাটি ইতিমধ্যে স্থগিত।", "already_suspended");
        b.Status = BusinessStatus.Suspended;
        audit.Log("business.suspend", "business", businessId, reason, businessId, scope: RoleScope.Platform);
        notifier.Notify(b.OwnerId, WorkspaceType.Business, businessId, "business.suspended", "আপনার ব্যবসা সাময়িকভাবে স্থগিত করা হয়েছে।", reason);
    }

    private void Record(ModerationActionType action, string reason, VerificationRequest v, Guid? inspectionId = null)
    {
        db.ModerationActions.Add(new ModerationAction
        {
            ModeratorId = me.RequireId(), Action = action, Reason = reason, VerificationRequestId = v.Id, BusinessId = v.BusinessId,
            InspectionId = inspectionId,
        });
        audit.Log($"verification.{SnakeCase.FromEnum(action)}", "verification_request", v.Id, reason, v.BusinessId, scope: RoleScope.Platform);
    }

    private async Task<VerificationRequest> LoadAsync(Guid id)
    {
        var v = await db.VerificationRequests.Include(x => x.Business).Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("আবেদনটি পাওয়া যায়নি।", "verification_not_found");
        await access.RequireOnBusinessAsync(P.VerificationReview, v.BusinessId);
        return v;
    }

    /// <summary>Only the moderator who claimed the request decides it.</summary>
    private async Task<VerificationRequest> DecidableAsync(Guid id)
    {
        var v = await LoadAsync(id);
        if (v.Status != VerificationStatus.UnderReview)
            throw new ConflictException("আগে পর্যালোচনা শুরু করুন।", "not_under_review");
        if (v.AssignedModeratorId != me.RequireId())
            throw new ForbiddenException("এই আবেদনটি অন্য একজন মডারেটর পর্যালোচনা করছেন।", "assigned_to_other");
        return v;
    }

    private IQueryable<QueueItemDto> Project(IQueryable<VerificationRequest> q) => q.Select(v => new QueueItemDto(v.Id, v.BusinessId,
        v.Business.Name, v.Business.LogoUrl, v.Business.Owner.FullName, v.Business.Category.NameBn,
        v.Business.Zone != null ? v.Business.Zone.NameBn : null, v.AttemptNo, v.Status, v.Priority, v.RequiresInspection,
        v.AssignedModeratorId, v.SubmittedAt));
}
