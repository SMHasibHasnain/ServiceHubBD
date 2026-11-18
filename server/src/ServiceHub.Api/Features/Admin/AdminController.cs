using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Admin;

public record AdminUserDto(Guid Id, string FullName, string Phone, string? Email, AccountStatus Status, DateTime CreatedAt,
    DateTime? LastLoginAt, int BusinessesOwned, string[] PlatformRoles);

public record AssignRoleRequest(Guid UserId, [Required] string RoleKey, int? ZoneId, int? CategoryId, DateTime? ValidUntil);

public record AssignmentDto(Guid Id, Guid UserId, string UserName, string RoleKey, string RoleName, int? ZoneId, string? ZoneName,
    int? CategoryId, string? CategoryName, DateTime ValidFrom, DateTime? ValidUntil, DateTime? RevokedAt);

public record ReasonBody([Required, StringLength(1000, MinimumLength = 5)] string Reason);

public record AuditLogDto(long Id, Guid? ActorId, string? ActorName, RoleScope? Scope, Guid? BusinessId, int? ZoneId, string Action,
    string EntityType, string EntityId, Dictionary<string, string?>? Changes, string? Reason, DateTime CreatedAt);

public record PlatformStatsDto(int TotalUsers, int TotalBusinesses, int PublishedBusinesses, int PendingVerifications,
    int BookingsToday, int NewRegistrationsToday, int OpenReports);

/// <summary>Platform control center. Sensitive actions require a reason and are written to the audit log.</summary>
[ApiController]
[Authorize]
[Route("api/admin")]
public class AdminController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<PlatformStatsDto>> Stats()
    {
        await access.RequireGlobalAsync(P.UsersView);
        var start = BdTime.ToUtc(BdTime.Today(), TimeOnly.MinValue);
        return new PlatformStatsDto(
            await db.Users.CountAsync(),
            await db.Businesses.CountAsync(),
            await db.Businesses.CountAsync(b => b.Status == BusinessStatus.Published),
            await db.VerificationRequests.CountAsync(v => v.Status == VerificationStatus.Submitted || v.Status == VerificationStatus.UnderReview),
            await db.Bookings.CountAsync(b => b.CreatedAt >= start),
            await db.Users.CountAsync(u => u.CreatedAt >= start),
            await db.Reports.CountAsync(r => r.Status == ReportStatus.Open || r.Status == ReportStatus.Escalated));
    }

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> Users([FromQuery] string? q, [FromQuery] AccountStatus? status,
        [FromQuery] PageQuery paging)
    {
        await access.RequireGlobalAsync(P.UsersView);
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            var phone = Phone.NormalizeBd(q);
            query = query.Where(u => EF.Functions.ILike(u.FullName, like) || (u.Email != null && EF.Functions.ILike(u.Email, like)) ||
                                     (phone != null && u.Phone == phone));
        }
        if (status is not null) query = query.Where(u => u.Status == status);
        var now = DateTime.UtcNow;
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(u => u.CreatedAt).Skip(paging.Skip).Take(paging.SafeSize)
            .Select(u => new AdminUserDto(u.Id, u.FullName, u.Phone, u.Email, u.Status, u.CreatedAt, u.LastLoginAt,
                db.Businesses.Count(b => b.OwnerId == u.Id),
                u.RoleAssignments.Where(a => a.RevokedAt == null && (a.ValidUntil == null || a.ValidUntil > now))
                    .Select(a => a.Role.Key).ToArray()))
            .ToListAsync();
        return new PagedResult<AdminUserDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    [HttpPost("users/{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, ReasonBody req)
    {
        await access.RequireGlobalAsync(P.UsersManage);
        if (id == me.RequireId()) throw new BadRequestException("নিজের অ্যাকাউন্ট স্থগিত করা যায় না।", "cannot_suspend_self");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException("ব্যবহারকারী পাওয়া যায়নি।");
        await EnsureNotHigherAdminAsync(id);
        user.Status = AccountStatus.Suspended;
        await db.UserSessions.Where(s => s.UserId == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        audit.Log("user.suspend", "user", id, req.Reason, scope: RoleScope.Platform);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("users/{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, ReasonBody req)
    {
        await access.RequireGlobalAsync(P.UsersManage);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException("ব্যবহারকারী পাওয়া যায়নি।");
        user.Status = AccountStatus.Active;
        audit.Log("user.activate", "user", id, req.Reason, scope: RoleScope.Platform);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("role-assignments")]
    public async Task<ActionResult<List<AssignmentDto>>> Assignments([FromQuery] Guid? userId, [FromQuery] string? roleKey,
        [FromQuery] bool includeRevoked = false)
    {
        await access.RequireGlobalAsync(P.UsersView);
        var q = db.PlatformRoleAssignments.AsQueryable();
        if (userId is not null) q = q.Where(a => a.UserId == userId);
        if (roleKey is not null) q = q.Where(a => a.Role.Key == roleKey);
        if (!includeRevoked) q = q.Where(a => a.RevokedAt == null);
        return await ProjectAssignments(q.OrderByDescending(a => a.CreatedAt)).ToListAsync();
    }

    /// <summary>
    /// Grants a platform/zone role. Zone roles (inspector) need a zone and cover its whole subtree;
    /// platform roles may optionally be narrowed to a zone and/or category.
    /// </summary>
    [HttpPost("role-assignments")]
    public async Task<ActionResult<AssignmentDto>> Assign(AssignRoleRequest req)
    {
        await access.RequireGlobalAsync(P.RolesAssign);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == req.RoleKey && r.BusinessId == null && r.Scope != RoleScope.Business)
                   ?? throw new BadRequestException("ভূমিকাটি পাওয়া যায়নি।", "invalid_role");
        if (role.Key is R.Admin or R.SuperAdmin) await access.RequireGlobalAsync(P.RolesAssignAdmin);
        if (role.Scope == RoleScope.Zone && req.ZoneId is null)
            throw new BadRequestException("এই ভূমিকার জন্য এলাকা নির্বাচন করুন।", "zone_required");
        if (role.Key is R.Admin or R.SuperAdmin && (req.ZoneId is not null || req.CategoryId is not null))
            throw new BadRequestException("এ্যাডমিন ভূমিকা এলাকা বা ক্যাটাগরিতে সীমাবদ্ধ করা যায় না।", "admin_unscoped");
        if (!await db.Users.AnyAsync(u => u.Id == req.UserId && u.Status == AccountStatus.Active))
            throw new BadRequestException("ব্যবহারকারী পাওয়া যায়নি।", "invalid_user");
        if (req.ZoneId is not null && !await db.GeoZones.AnyAsync(z => z.Id == req.ZoneId))
            throw new BadRequestException("এলাকাটি পাওয়া যায়নি।", "invalid_zone");
        if (req.CategoryId is not null && !await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            throw new BadRequestException("ক্যাটাগরি পাওয়া যায়নি।", "invalid_category");
        if (req.ValidUntil is not null && req.ValidUntil <= DateTime.UtcNow)
            throw new BadRequestException("মেয়াদ ভবিষ্যতের সময় হতে হবে।", "invalid_valid_until");

        var existing = await db.PlatformRoleAssignments.FirstOrDefaultAsync(a =>
            a.UserId == req.UserId && a.RoleId == role.Id && a.ZoneId == req.ZoneId && a.CategoryId == req.CategoryId);
        if (existing is not null && existing.RevokedAt is null)
            throw new ConflictException("এই ভূমিকা ইতিমধ্যে দেওয়া আছে।", "already_assigned");

        var a = existing ?? db.PlatformRoleAssignments.Add(new PlatformRoleAssignment
        {
            UserId = req.UserId, RoleId = role.Id, ZoneId = req.ZoneId, CategoryId = req.CategoryId,
        }).Entity;
        a.GrantedBy = me.RequireId();
        a.ValidFrom = DateTime.UtcNow;
        a.ValidUntil = req.ValidUntil;
        a.RevokedAt = null;
        a.RevokedBy = null;
        audit.Log("role.assign", "platform_role_assignment", a.Id, zoneId: req.ZoneId, scope: RoleScope.Platform,
            changes: new() { ["user"] = req.UserId.ToString(), ["role"] = role.Key });
        await db.SaveChangesAsync();
        return await ProjectAssignments(db.PlatformRoleAssignments.Where(x => x.Id == a.Id)).FirstAsync();
    }

    [HttpPost("role-assignments/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, ReasonBody req)
    {
        await access.RequireGlobalAsync(P.RolesAssign);
        var a = await db.PlatformRoleAssignments.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id && x.RevokedAt == null)
                ?? throw new NotFoundException("ভূমিকা পাওয়া যায়নি।", "assignment_not_found");
        if (a.Role.Key is R.Admin or R.SuperAdmin) await access.RequireGlobalAsync(P.RolesAssignAdmin);
        if (a.UserId == me.RequireId() && a.Role.Key == R.SuperAdmin)
            throw new BadRequestException("নিজের সুপার এ্যাডমিন ভূমিকা বাতিল করা যায় না।", "cannot_revoke_self");
        a.RevokedAt = DateTime.UtcNow;
        a.RevokedBy = me.RequireId();
        audit.Log("role.revoke", "platform_role_assignment", a.Id, req.Reason, zoneId: a.ZoneId, scope: RoleScope.Platform,
            changes: new() { ["user"] = a.UserId.ToString(), ["role"] = a.Role.Key });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> AuditLogs([FromQuery] string? entityType, [FromQuery] string? entityId,
        [FromQuery] Guid? actorId, [FromQuery] Guid? businessId, [FromQuery] PageQuery paging)
    {
        await access.RequireGlobalAsync(P.AuditView);
        var q = db.AuditLogs.AsQueryable();
        if (entityType is not null) q = q.Where(l => l.EntityType == entityType);
        if (entityId is not null) q = q.Where(l => l.EntityId == entityId);
        if (actorId is not null) q = q.Where(l => l.ActorId == actorId);
        if (businessId is not null) q = q.Where(l => l.BusinessId == businessId);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(l => l.Id).Skip(paging.Skip).Take(paging.SafeSize)
            .Select(l => new AuditLogDto(l.Id, l.ActorId, db.Users.Where(u => u.Id == l.ActorId).Select(u => u.FullName).FirstOrDefault(),
                l.Scope, l.BusinessId, l.ZoneId, l.Action, l.EntityType, l.EntityId, l.Changes, l.Reason, l.CreatedAt))
            .ToListAsync();
        return new PagedResult<AuditLogDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    /// <summary>Only a super admin may act on another admin account.</summary>
    private async Task EnsureNotHigherAdminAsync(Guid targetUserId)
    {
        var targetIsAdmin = await db.PlatformRoleAssignments.AnyAsync(a => a.UserId == targetUserId && a.RevokedAt == null &&
            (a.Role.Key == R.Admin || a.Role.Key == R.SuperAdmin));
        if (targetIsAdmin) await access.RequireGlobalAsync(P.RolesAssignAdmin);
    }

    private static IQueryable<AssignmentDto> ProjectAssignments(IQueryable<PlatformRoleAssignment> q) => q.Select(a => new AssignmentDto(
        a.Id, a.UserId, a.User.FullName, a.Role.Key, a.Role.NameBn, a.ZoneId, a.Zone != null ? a.Zone.NameBn : null, a.CategoryId,
        a.Category != null ? a.Category.NameBn : null, a.ValidFrom, a.ValidUntil, a.RevokedAt));
}
