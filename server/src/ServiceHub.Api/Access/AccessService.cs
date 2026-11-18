using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Access;

/// <summary>
/// Resolves permissions for the current user at three scopes:
/// business (business_members), platform (assignment without zone) and zone
/// (assignment whose geo_zones subtree contains the target business).
/// </summary>
public class AccessService(AppDbContext db, ICurrentUser me)
{
    // ---------------- business scope ----------------

    /// <summary>
    /// Ensures the current user is an active member of the business whose role grants <paramref name="permission"/>.
    /// Non-members get 404 so that private workspaces don't leak their existence.
    /// </summary>
    public async Task<BusinessMember> RequireBusinessAsync(Guid businessId, string permission)
    {
        var userId = me.RequireId();
        var member = await db.BusinessMembers
            .Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.BusinessId == businessId && m.UserId == userId && m.Status == MemberStatus.Active)
            ?? throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");

        var allowed = await db.RolePermissions
            .AnyAsync(rp => rp.RoleId == member.RoleId && rp.Permission.Key == permission);
        if (!allowed) throw new ForbiddenException();
        return member;
    }

    public async Task<bool> HasBusinessPermissionAsync(Guid businessId, string permission)
    {
        var userId = me.RequireId();
        return await db.BusinessMembers.AnyAsync(m =>
            m.BusinessId == businessId && m.UserId == userId && m.Status == MemberStatus.Active &&
            m.Role.RolePermissions.Any(rp => rp.Permission.Key == permission));
    }

    public async Task<string[]> GetBusinessPermissionsAsync(Guid businessId, Guid userId) =>
        await db.BusinessMembers
            .Where(m => m.BusinessId == businessId && m.UserId == userId && m.Status == MemberStatus.Active)
            .SelectMany(m => m.Role.RolePermissions.Select(rp => rp.Permission.Key))
            .OrderBy(k => k)
            .ToArrayAsync();

    // ---------------- platform / zone scope ----------------

    /// <summary>Active assignments of <paramref name="userId"/> whose role grants <paramref name="permission"/>.</summary>
    public IQueryable<PlatformRoleAssignment> ActiveAssignments(Guid userId, string permission)
    {
        var now = DateTime.UtcNow;
        return db.PlatformRoleAssignments.Where(a =>
            a.UserId == userId && a.RevokedAt == null && a.ValidFrom <= now &&
            (a.ValidUntil == null || a.ValidUntil > now) &&
            a.Role.RolePermissions.Any(rp => rp.Permission.Key == permission));
    }

    /// <summary>True when the user holds the permission anywhere (any zone/category).</summary>
    public Task<bool> HasPlatformPermissionAsync(string permission) =>
        ActiveAssignments(me.RequireId(), permission).AnyAsync();

    /// <summary>True only for an unscoped (whole-platform) grant.</summary>
    public Task<bool> HasGlobalPermissionAsync(string permission) =>
        ActiveAssignments(me.RequireId(), permission).AnyAsync(a => a.ZoneId == null && a.CategoryId == null);

    public async Task RequirePlatformAsync(string permission)
    {
        if (!await HasPlatformPermissionAsync(permission)) throw new ForbiddenException();
    }

    public async Task RequireGlobalAsync(string permission)
    {
        if (!await HasGlobalPermissionAsync(permission)) throw new ForbiddenException();
    }

    /// <summary>Businesses the current user may act on with a platform/zone permission.</summary>
    public IQueryable<Business> ScopedBusinesses(string permission)
    {
        var assignments = ActiveAssignments(me.RequireId(), permission);
        return db.Businesses.Where(b => assignments.Any(a =>
            (a.ZoneId == null || (b.ZoneId != null && b.Zone!.Path.IsDescendantOf(a.Zone!.Path))) &&
            (a.CategoryId == null || a.CategoryId == b.CategoryId)));
    }

    /// <summary>
    /// Platform/zone permission on one business, honouring zone + category scope and the
    /// conflict-of-interest rule (staff of a business cannot moderate or inspect it).
    /// </summary>
    public async Task RequireOnBusinessAsync(string permission, Guid businessId)
    {
        var userId = me.RequireId();
        if (!await ScopedBusinesses(permission).AnyAsync(b => b.Id == businessId))
            throw new ForbiddenException();
        var isMember = await db.BusinessMembers.AnyAsync(m =>
            m.BusinessId == businessId && m.UserId == userId && m.Status != MemberStatus.Removed);
        if (isMember)
            throw new ForbiddenException("নিজের সাথে যুক্ত ব্যবসায় এই কাজ করা যাবে না।", "conflict_of_interest");
    }

    /// <summary>Whether <paramref name="userId"/> holds an active inspector assignment covering <paramref name="zoneId"/>.</summary>
    public async Task<bool> IsInspectorForZoneAsync(Guid userId, int zoneId)
    {
        var assignments = ActiveAssignments(userId, P.InspectionsPerform);
        return await db.GeoZones.AnyAsync(z => z.Id == zoneId &&
            assignments.Any(a => a.ZoneId != null && z.Path.IsDescendantOf(a.Zone!.Path)));
    }
}
