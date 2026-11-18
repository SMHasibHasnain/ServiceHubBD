using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Features.Auth;

namespace ServiceHub.Api.Features.Me;

public record UpdateProfileRequest([StringLength(120, MinimumLength = 2)] string? FullName, string? AvatarUrl,
    string? AddressLine, int? ZoneId);

public record WorkspaceDto(
    WorkspaceType Type,
    string Name,
    string Subtitle,
    Guid? BusinessId,
    string? RoleKey,
    string? RoleName,
    VerificationStatus? VerificationStatus,
    int? ZoneId,
    int UnreadNotifications,
    string[] Permissions);

public record SetWorkspaceRequest(WorkspaceType Type, Guid? BusinessId);

public record NotificationDto(Guid Id, WorkspaceType Workspace, Guid? BusinessId, string Type, string Title,
    string? Body, string? ActionUrl, Dictionary<string, string>? Data, DateTime? ReadAt, DateTime CreatedAt);

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(AppDbContext db, ICurrentUser me, AccessService access) : ControllerBase
{
    /// <summary>Permissions that open a platform workspace regardless of the role's primary one.</summary>
    private static readonly Dictionary<string, WorkspaceType> WorkspaceByPermission = new()
    {
        [P.VerificationReview] = WorkspaceType.Moderation,
        [P.InspectionsPerform] = WorkspaceType.Inspection,
        [P.CampaignsManage] = WorkspaceType.Marketing,
        [P.UsersView] = WorkspaceType.Administration,
    };

    [HttpGet]
    public async Task<ActionResult<UserDto>> Get() => AuthController.ToDto(await CurrentAsync());

    [HttpPatch]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileRequest req)
    {
        var user = await CurrentAsync();
        if (req.FullName is not null) user.FullName = req.FullName.Trim();
        if (req.AvatarUrl is not null) user.AvatarUrl = req.AvatarUrl;
        if (req.AddressLine is not null) user.AddressLine = req.AddressLine;
        if (req.ZoneId is not null)
        {
            if (!await db.GeoZones.AnyAsync(z => z.Id == req.ZoneId)) throw new BadRequestException("এলাকাটি পাওয়া যায়নি।", "invalid_zone");
            user.ZoneId = req.ZoneId;
        }
        await db.SaveChangesAsync();
        return AuthController.ToDto(user);
    }

    /// <summary>
    /// Everything the workspace switcher shows: the customer workspace, one entry per business
    /// membership and one per active platform/zone role — all under one login.
    /// </summary>
    [HttpGet("workspaces")]
    public async Task<ActionResult<List<WorkspaceDto>>> Workspaces()
    {
        var userId = me.RequireId();
        var unread = await db.Notifications
            .Where(n => n.RecipientId == userId && n.ReadAt == null)
            .GroupBy(n => new { n.Workspace, n.BusinessId })
            .Select(g => new { g.Key.Workspace, g.Key.BusinessId, Count = g.Count() })
            .ToListAsync();
        int Unread(WorkspaceType t, Guid? bid = null) =>
            unread.Where(u => u.Workspace == t && u.BusinessId == bid).Sum(u => u.Count);

        var user = await CurrentAsync();
        var result = new List<WorkspaceDto>
        {
            new(WorkspaceType.Customer, user.FullName, "গ্রাহক", null, null, null, null, null, Unread(WorkspaceType.Customer), []),
        };

        var memberships = await db.BusinessMembers
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active)
            .Select(m => new
            {
                m.BusinessId, m.Business.Name, m.Business.VerificationStatus,
                RoleKey = m.Role.Key, RoleName = m.Role.NameBn,
                Permissions = m.Role.RolePermissions.Select(rp => rp.Permission.Key).ToArray(),
            })
            .OrderBy(m => m.Name)
            .ToListAsync();
        result.AddRange(memberships.Select(m => new WorkspaceDto(WorkspaceType.Business, m.Name, m.RoleName, m.BusinessId,
            m.RoleKey, m.RoleName, m.VerificationStatus, null, Unread(WorkspaceType.Business, m.BusinessId), m.Permissions)));

        var now = DateTime.UtcNow;
        var assignments = await db.PlatformRoleAssignments
            .Where(a => a.UserId == userId && a.RevokedAt == null && a.ValidFrom <= now && (a.ValidUntil == null || a.ValidUntil > now))
            .Select(a => new
            {
                a.Role.Key, a.Role.NameBn, a.ZoneId, ZoneName = a.Zone != null ? a.Zone.NameBn : null,
                Permissions = a.Role.RolePermissions.Select(rp => rp.Permission.Key).ToArray(),
            })
            .ToListAsync();
        // A role opens every workspace its permissions reach — e.g. an admin, who can also moderate,
        // sees both Administration and Moderation. One entry per (workspace, zone).
        var seen = new HashSet<(WorkspaceType, int?)>();
        foreach (var a in assignments.OrderByDescending(a => R.Get(a.Key).Rank))
        {
            var types = new List<WorkspaceType> { R.Get(a.Key).Workspace };
            types.AddRange(WorkspaceByPermission.Where(kv => a.Permissions.Contains(kv.Key)).Select(kv => kv.Value));
            foreach (var type in types.Distinct())
            {
                if (!seen.Add((type, a.ZoneId))) continue;
                var name = type switch
                {
                    WorkspaceType.Moderation => "মডারেশন",
                    WorkspaceType.Inspection => "পরিদর্শন",
                    WorkspaceType.Marketing => "মার্কেটিং",
                    _ => "এ্যাডমিনিস্ট্রেশন",
                };
                if (a.ZoneName != null) name += " — " + a.ZoneName;
                result.Add(new WorkspaceDto(type, name, a.NameBn, null, a.Key, a.NameBn, null, a.ZoneId, Unread(type), a.Permissions));
            }
        }
        return result;
    }

    /// <summary>Remembers the selected workspace so the next login opens it. No re-authentication involved.</summary>
    [HttpPut("workspace")]
    public async Task<IActionResult> SetWorkspace(SetWorkspaceRequest req)
    {
        var user = await CurrentAsync();
        if (req.Type == WorkspaceType.Business)
        {
            if (req.BusinessId is null) throw new BadRequestException("ব্যবসা নির্বাচন করুন।", "business_required");
            await access.RequireBusinessAsync(req.BusinessId.Value, P.BusinessView);
        }
        user.LastWorkspaceType = req.Type;
        user.LastActiveBusinessId = req.Type == WorkspaceType.Business ? req.BusinessId : null;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<PagedResult<NotificationDto>>> Notifications(
        [FromQuery] WorkspaceType? workspace, [FromQuery] Guid? businessId, [FromQuery] PageQuery paging,
        [FromQuery] bool unreadOnly = false)
    {
        var page = paging;
        var userId = me.RequireId();
        var q = db.Notifications.Where(n => n.RecipientId == userId);
        if (workspace is not null) q = q.Where(n => n.Workspace == workspace);
        if (businessId is not null) q = q.Where(n => n.BusinessId == businessId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip(page.Skip).Take(page.SafeSize)
            .Select(n => new NotificationDto(n.Id, n.Workspace, n.BusinessId, n.Type, n.Title, n.Body, n.ActionUrl, n.Data, n.ReadAt, n.CreatedAt))
            .ToListAsync();
        return new PagedResult<NotificationDto>(items, page.SafePage, page.SafeSize, total);
    }

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = me.RequireId();
        await db.Notifications.Where(x => x.Id == id && x.RecipientId == userId && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, DateTime.UtcNow));
        return NoContent();
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllRead([FromQuery] WorkspaceType? workspace, [FromQuery] Guid? businessId)
    {
        var userId = me.RequireId();
        var q = db.Notifications.Where(x => x.RecipientId == userId && x.ReadAt == null);
        if (workspace is not null) q = q.Where(x => x.Workspace == workspace);
        if (businessId is not null) q = q.Where(x => x.BusinessId == businessId);
        await q.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, DateTime.UtcNow));
        return NoContent();
    }

    [HttpGet("favourites")]
    public async Task<ActionResult<List<Guid>>> Favourites() =>
        await db.Favourites.Where(f => f.UserId == me.RequireId()).OrderByDescending(f => f.CreatedAt)
            .Select(f => f.BusinessId).ToListAsync();

    [HttpPut("favourites/{businessId:guid}")]
    public async Task<IActionResult> AddFavourite(Guid businessId)
    {
        var userId = me.RequireId();
        if (!await db.Businesses.AnyAsync(b => b.Id == businessId && b.Status == BusinessStatus.Published))
            throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");
        if (!await db.Favourites.AnyAsync(f => f.UserId == userId && f.BusinessId == businessId))
        {
            db.Favourites.Add(new Favourite { UserId = userId, BusinessId = businessId });
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    [HttpDelete("favourites/{businessId:guid}")]
    public async Task<IActionResult> RemoveFavourite(Guid businessId)
    {
        var userId = me.RequireId();
        await db.Favourites.Where(f => f.UserId == userId && f.BusinessId == businessId).ExecuteDeleteAsync();
        return NoContent();
    }

    private async Task<User> CurrentAsync() =>
        await db.Users.FindAsync(me.RequireId()) ?? throw new UnauthorizedException();
}
