using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Inspections;

/// <summary>
/// Creates inspections and routes them to the least-loaded inspector whose zone subtree contains
/// the business. With no matching inspector the inspection waits unassigned in the zone queue.
/// </summary>
public class InspectionRouter(AppDbContext db, ICurrentUser me, Notifier notifier)
{
    public async Task<Inspection> CreateAsync(Business business, InspectionType type, Guid? verificationRequestId, Guid? reportId,
        DateTime? scheduledFor)
    {
        if (business.ZoneId is null)
            throw new BadRequestException("ব্যবসার এলাকা নির্ধারণ করা নেই, তাই পরিদর্শক নির্ধারণ করা যাচ্ছে না।", "business_zone_missing");

        var zonePath = await db.GeoZones.Where(z => z.Id == business.ZoneId).Select(z => z.Path).FirstAsync();
        var now = DateTime.UtcNow;
        var members = db.BusinessMembers.Where(m => m.BusinessId == business.Id && m.Status != MemberStatus.Removed).Select(m => m.UserId);
        var inspectorId = await db.PlatformRoleAssignments
            .Where(a => a.RevokedAt == null && a.ValidFrom <= now && (a.ValidUntil == null || a.ValidUntil > now) &&
                        a.ZoneId != null && zonePath.IsDescendantOf(a.Zone!.Path) &&
                        a.Role.RolePermissions.Any(rp => rp.Permission.Key == P.InspectionsPerform) &&
                        !members.Contains(a.UserId) && a.User.Status == AccountStatus.Active)
            .Select(a => a.UserId)
            .Distinct()
            .OrderBy(uid => db.Inspections.Count(i => i.InspectorId == uid &&
                (i.Status == InspectionStatus.Scheduled || i.Status == InspectionStatus.InProgress)))
            .Select(uid => (Guid?)uid)
            .FirstOrDefaultAsync();

        var inspection = new Inspection
        {
            BusinessId = business.Id, ZoneId = business.ZoneId.Value, Type = type, VerificationRequestId = verificationRequestId,
            ReportId = reportId, InspectorId = inspectorId, RequestedBy = me.RequireId(), ScheduledFor = scheduledFor,
        };
        db.Inspections.Add(inspection);
        if (inspectorId is not null)
            notifier.Notify(inspectorId.Value, WorkspaceType.Inspection, null, "inspection.assigned",
                "আপনার জন্য একটি নতুন পরিদর্শন নির্ধারিত হয়েছে।", business.Name,
                data: new() { ["inspectionId"] = inspection.Id.ToString() });
        return inspection;
    }
}

public record StartInspectionRequest(decimal? Latitude, decimal? Longitude);

public record SubmitInspectionRequest(
    InspectionOutcome Outcome,
    [Required, StringLength(4000, MinimumLength = 10)] string Findings,
    Dictionary<string, bool>? Checklist,
    decimal? Latitude,
    decimal? Longitude);

public record InspectionDto(Guid Id, InspectionType Type, InspectionStatus Status, InspectionOutcome? Outcome, Guid BusinessId,
    string BusinessName, string? AddressLine, string? BusinessPhone, int ZoneId, string ZoneName, Guid? VerificationRequestId,
    Guid? ReportId, Guid? InspectorId, DateTime? ScheduledFor, DateTime? VisitedAt, string? Findings,
    Dictionary<string, bool>? Checklist, DateTime CreatedAt);

/// <summary>Inspector workspace: field visits within the inspector's zone(s). Inspectors report; moderators decide.</summary>
[ApiController]
[Authorize]
[Route("api/inspections")]
public class InspectionsController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit, Notifier notifier) : ControllerBase
{
    /// <summary>My assigned inspections plus unassigned ones inside my zones.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<InspectionDto>>> List([FromQuery] InspectionStatus? status, [FromQuery] PageQuery paging)
    {
        await access.RequirePlatformAsync(P.InspectionsPerform);
        var userId = me.RequireId();
        var assignments = access.ActiveAssignments(userId, P.InspectionsPerform);
        var q = db.Inspections.Where(i => i.InspectorId == userId ||
            (i.InspectorId == null && assignments.Any(a => a.ZoneId != null && i.Zone.Path.IsDescendantOf(a.Zone!.Path))));
        q = status is null
            ? q.Where(i => i.Status == InspectionStatus.Scheduled || i.Status == InspectionStatus.InProgress)
            : q.Where(i => i.Status == status);
        var total = await q.CountAsync();
        var items = await Project(q.OrderBy(i => i.ScheduledFor ?? i.CreatedAt).Skip(paging.Skip).Take(paging.SafeSize)).ToListAsync();
        return new PagedResult<InspectionDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InspectionDto>> Get(Guid id)
    {
        await LoadForInspectorAsync(id, allowUnassigned: true);
        return await Project(db.Inspections.Where(i => i.Id == id)).FirstAsync();
    }

    [HttpPost("{id:guid}/claim")]
    public async Task<ActionResult<InspectionDto>> Claim(Guid id)
    {
        var i = await LoadForInspectorAsync(id, allowUnassigned: true);
        if (i.InspectorId is not null && i.InspectorId != me.RequireId())
            throw new ConflictException("এই পরিদর্শনটি অন্য একজন নিয়েছেন।", "already_claimed");
        i.InspectorId = me.RequireId();
        await db.SaveChangesAsync();
        return await Project(db.Inspections.Where(x => x.Id == id)).FirstAsync();
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<InspectionDto>> Start(Guid id, StartInspectionRequest req)
    {
        var i = await LoadForInspectorAsync(id, allowUnassigned: false);
        if (i.Status != InspectionStatus.Scheduled) throw new ConflictException("পরিদর্শনটি ইতিমধ্যে শুরু হয়েছে।", "already_started");
        i.Status = InspectionStatus.InProgress;
        i.VisitLatitude = req.Latitude;
        i.VisitLongitude = req.Longitude;
        await db.SaveChangesAsync();
        return await Project(db.Inspections.Where(x => x.Id == id)).FirstAsync();
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult<InspectionDto>> Submit(Guid id, SubmitInspectionRequest req)
    {
        var i = await LoadForInspectorAsync(id, allowUnassigned: false);
        if (i.Status is InspectionStatus.Completed or InspectionStatus.Cancelled)
            throw new ConflictException("পরিদর্শনটি ইতিমধ্যে বন্ধ হয়েছে।", "inspection_closed");
        i.Status = InspectionStatus.Completed;
        i.Outcome = req.Outcome;
        i.Findings = req.Findings.Trim();
        i.Checklist = req.Checklist;
        i.VisitedAt = DateTime.UtcNow;
        i.VisitLatitude = req.Latitude ?? i.VisitLatitude;
        i.VisitLongitude = req.Longitude ?? i.VisitLongitude;
        audit.Log("inspection.submit", "inspection", i.Id, businessId: i.BusinessId, zoneId: i.ZoneId, scope: RoleScope.Zone,
            changes: new() { ["outcome"] = SnakeCase.FromEnum(req.Outcome) });
        notifier.Notify(i.RequestedBy, WorkspaceType.Moderation, null, "inspection.completed",
            "মাঠ পরিদর্শনের প্রতিবেদন জমা হয়েছে।", i.Business.Name, data: new() { ["inspectionId"] = i.Id.ToString() });
        await db.SaveChangesAsync();
        return await Project(db.Inspections.Where(x => x.Id == id)).FirstAsync();
    }

    private async Task<Inspection> LoadForInspectorAsync(Guid id, bool allowUnassigned)
    {
        var userId = me.RequireId();
        var i = await db.Inspections.Include(x => x.Business).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("পরিদর্শনটি পাওয়া যায়নি।", "inspection_not_found");
        var mine = i.InspectorId == userId;
        var claimable = allowUnassigned && i.InspectorId == null && await access.IsInspectorForZoneAsync(userId, i.ZoneId);
        if (!mine && !claimable) throw new NotFoundException("পরিদর্শনটি পাওয়া যায়নি।", "inspection_not_found");
        if (!await access.IsInspectorForZoneAsync(userId, i.ZoneId)) throw new ForbiddenException();
        if (await db.BusinessMembers.AnyAsync(m => m.BusinessId == i.BusinessId && m.UserId == userId && m.Status != MemberStatus.Removed))
            throw new ForbiddenException("নিজের সাথে যুক্ত ব্যবসায় পরিদর্শন করা যাবে না।", "conflict_of_interest");
        return i;
    }

    private IQueryable<InspectionDto> Project(IQueryable<Inspection> q) => q.Select(i => new InspectionDto(i.Id, i.Type, i.Status,
        i.Outcome, i.BusinessId, i.Business.Name, i.Business.AddressLine, i.Business.Phone, i.ZoneId, i.Zone.NameBn,
        i.VerificationRequestId, i.ReportId, i.InspectorId, i.ScheduledFor, i.VisitedAt, i.Findings, i.Checklist, i.CreatedAt));
}
