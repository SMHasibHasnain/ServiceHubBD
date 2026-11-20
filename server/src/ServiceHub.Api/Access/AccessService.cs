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
/// <summary>
/// Who is acting on a business: a member (<see cref="Member"/> set) or platform staff without membership.
/// <see cref="RoleId"/> and <see cref="Scope"/> are what to write to the audit log.
/// </summary>
public record BusinessActor(Guid BusinessId, Guid UserId, BusinessMember? Member, short? RoleId, RoleScope Scope, short Rank,
    string? StaffRoleKey, string? StaffRoleName)
{
    public bool IsStaff => Member is null;
}

public class AccessService(AppDbContext db, ICurrentUser me, Features.Staff.StaffEdit staffEdit)
{
    /// <summary>
    /// Staff act just below the owner: they can manage every team role but never the owner, who can't be removed
    /// or demoted and whose ownership is only transferred.
    /// </summary>
    public const short StaffRank = 99;

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

    /// <summary>
    /// Like <see cref="RequireBusinessAsync"/>, but platform staff who aren't members can also get in through
    /// <see cref="P.StaffGrants"/>. Members always use their own role (no staff fallback: conflict of interest).
    /// A staff write starts a <see cref="Features.Staff.StaffEdit"/>, which logs the change for undo.
    /// </summary>
    public async Task<BusinessActor> RequireBusinessOrStaffAsync(Guid businessId, string permission)
    {
        var userId = me.RequireId();
        var member = await db.BusinessMembers.Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.BusinessId == businessId && m.UserId == userId && m.Status != MemberStatus.Removed);
        if (member is not null)
        {
            if (member.Status != MemberStatus.Active) throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");
            if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == member.RoleId && rp.Permission.Key == permission))
                throw new ForbiddenException();
            return new BusinessActor(businessId, userId, member, member.RoleId, RoleScope.Business, member.Role.Rank, null, null);
        }

        var role = await StaffRoleAsync(businessId, permission)
                   ?? throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");
        await staffEdit.BeginAsync(businessId, userId, role.Id, role.Key);
        return new BusinessActor(businessId, userId, null, role.Id, RoleScope.Platform, StaffRank, role.Key, role.NameBn);
    }

    public record StaffRole(short Id, string Key, string NameBn, short Rank);

    /// <summary>The highest platform role that gives the current (non-member) user <paramref name="permission"/> on this business.</summary>
    public async Task<StaffRole?> StaffRoleAsync(Guid businessId, string permission, bool includeDeleted = false)
    {
        if (!P.StaffGrants.TryGetValue(permission, out var grants)) return null;
        StaffRole? best = null;
        foreach (var grant in grants)
        {
            var role = await CoveringAssignments(grant, businessId, includeDeleted)
                .OrderByDescending(a => a.Role.Rank)
                .Select(a => new StaffRole(a.Role.Id, a.Role.Key, a.Role.NameBn, a.Role.Rank))
                .FirstOrDefaultAsync();
            if (role is not null && (best is null || role.Rank > best.Rank)) best = role;
        }
        return best;
    }

    /// <summary>Business permissions the current user gets as staff on this business (empty for members or outsiders).</summary>
    public async Task<string[]> StaffPermissionsAsync(Guid businessId)
    {
        var held = new HashSet<string>();
        foreach (var grant in P.StaffGrants.Values.SelectMany(g => g).Distinct())
            if (await CoveringAssignments(grant, businessId, false).AnyAsync()) held.Add(grant);
        return P.StaffGrants.Where(kv => kv.Value.Any(held.Contains)).Select(kv => kv.Key).OrderBy(k => k).ToArray();
    }

    /// <summary>Active assignments of the current user with <paramref name="permission"/> whose zone/category covers the business.</summary>
    private IQueryable<PlatformRoleAssignment> CoveringAssignments(string permission, Guid businessId, bool includeDeleted)
    {
        var businesses = includeDeleted ? db.Businesses.IgnoreQueryFilters() : db.Businesses;
        var target = businesses.Where(b => b.Id == businessId);
        return ActiveAssignments(me.RequireId(), permission).Where(a => target.Any(b =>
            (a.ZoneId == null || (b.ZoneId != null && b.Zone!.Path.IsDescendantOf(a.Zone!.Path))) &&
            (a.CategoryId == null || a.CategoryId == b.CategoryId)));
    }

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
    public IQueryable<Business> ScopedBusinesses(string permission, bool includeDeleted = false)
    {
        var assignments = ActiveAssignments(me.RequireId(), permission);
        return (includeDeleted ? db.Businesses.IgnoreQueryFilters() : db.Businesses).Where(b => assignments.Any(a =>
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
