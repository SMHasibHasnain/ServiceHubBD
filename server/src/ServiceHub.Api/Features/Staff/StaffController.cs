using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Features.Page;

namespace ServiceHub.Api.Features.Staff;

public record StaffBusinessDto(Guid Id, string Name, string Slug, string CategoryName, string? ZoneName, BusinessStatus Status,
    VerificationStatus VerificationStatus, string OwnerName, DateTime CreatedAt, DateTime? DeletedAt, DateTime? LastStaffEditAt);

public record StaffActionDto(long Id, Guid BusinessId, string BusinessName, Guid ActorId, string ActorName, string RoleKey,
    string RoleName, string Summary, DateTime CreatedAt, DateTime? UndoneAt, string? UndoneByName, string? UndoReason, long? UndoOf,
    int RowCount);

/// <summary>One changed field, values as display text (null = empty).</summary>
public record FieldChangeDto(string Field, string? Before, string? After);

/// <summary>One changed row: <c>added</c>, <c>removed</c> or <c>updated</c>, with a title like the service's name.</summary>
public record RowChangeDto(string Entity, string EntityLabel, string Kind, string? Title, List<FieldChangeDto> Fields);

public record StaffActionDetailDto(StaffActionDto Action, List<RowChangeDto> Rows);

public record UndoRequest([MaxLength(500)] string? Reason);

/// <summary>
/// Platform staff working across businesses: the all-business list (moderators, analysts, admins), the log of staff
/// edits (admins and super admins, who see every moderator's change), undo, and the platform page defaults.
/// Editing itself goes through the normal business endpoints; see <see cref="AccessService.RequireBusinessOrStaffAsync"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/staff")]
public class StaffController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit, StaffEdit staffEdit,
    PageDefaults defaults) : ControllerBase
{
    // ---------------- businesses ----------------

    [HttpGet("businesses")]
    public async Task<ActionResult<PagedResult<StaffBusinessDto>>> Businesses([FromQuery] string? q, [FromQuery] BusinessStatus? status,
        [FromQuery] VerificationStatus? verification, [FromQuery] bool deleted, [FromQuery] PageQuery paging)
    {
        await access.RequirePlatformAsync(P.BusinessesView);
        // Deleted pages are listed only for people who can bring them back.
        if (deleted) await access.RequireGlobalAsync(P.BusinessesUndo);
        var query = access.ScopedBusinesses(P.BusinessesView, includeDeleted: deleted);
        query = deleted ? query.Where(b => b.DeletedAt != null) : query.Where(b => b.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(b => EF.Functions.ILike(b.Name, like) || EF.Functions.ILike(b.Slug, like) ||
                                     EF.Functions.ILike(b.Owner.FullName, like));
        }
        if (status is not null) query = query.Where(b => b.Status == status);
        if (verification is not null) query = query.Where(b => b.VerificationStatus == verification);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(b => b.CreatedAt).Skip(paging.Skip).Take(paging.SafeSize)
            .Select(b => new StaffBusinessDto(b.Id, b.Name, b.Slug, b.Category.NameBn, b.Zone != null ? b.Zone.NameBn : null,
                b.Status, b.VerificationStatus, b.Owner.FullName, b.CreatedAt, b.DeletedAt,
                db.StaffActions.Where(a => a.BusinessId == b.Id).Max(a => (DateTime?)a.CreatedAt)))
            .ToListAsync();
        return new PagedResult<StaffBusinessDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    // ---------------- staff edit log ----------------

    [HttpGet("actions")]
    public async Task<ActionResult<PagedResult<StaffActionDto>>> Actions([FromQuery] Guid? businessId, [FromQuery] string? role,
        [FromQuery] PageQuery paging)
    {
        await access.RequireGlobalAsync(P.AuditView);
        var query = db.StaffActions.AsNoTracking().AsQueryable();
        if (businessId is not null) query = query.Where(a => a.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(a => a.RoleKey == role);
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(a => a.CreatedAt).Skip(paging.Skip).Take(paging.SafeSize).ToListAsync();
        return new PagedResult<StaffActionDto>(await ToDtosAsync(rows), paging.SafePage, paging.SafeSize, total);
    }

    [HttpGet("actions/{id:long}")]
    public async Task<ActionResult<StaffActionDetailDto>> Action(long id)
    {
        await access.RequireGlobalAsync(P.AuditView);
        var a = await db.StaffActions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new NotFoundException("পরিবর্তনটি পাওয়া যায়নি।", "staff_action_not_found");
        return new StaffActionDetailDto((await ToDtosAsync([a]))[0], a.Changes.Select(ToRow).ToList());
    }

    /// <summary>
    /// Puts back what a staff edit changed. Refused when any of those rows were changed again afterwards (409
    /// <c>changed_since</c>), so a later edit is never silently overwritten. The undo is itself logged and can be undone.
    /// </summary>
    [HttpPost("actions/{id:long}/undo")]
    public async Task<IActionResult> Undo(long id, UndoRequest req)
    {
        await access.RequireGlobalAsync(P.BusinessesUndo);
        var action = await db.StaffActions.FirstOrDefaultAsync(x => x.Id == id)
                     ?? throw new NotFoundException("পরিবর্তনটি পাওয়া যায়নি।", "staff_action_not_found");
        if (action.UndoneAt is not null)
            throw new ConflictException("এই পরিবর্তন আগেই ফিরিয়ে নেওয়া হয়েছে।", "already_undone");

        var userId = me.RequireId();
        if (await db.BusinessMembers.AnyAsync(m => m.BusinessId == action.BusinessId && m.UserId == userId && m.Status != MemberStatus.Removed))
            throw new ForbiddenException("নিজের সাথে যুক্ত ব্যবসায় এই কাজ করা যাবে না।", "conflict_of_interest");
        var role = await access.ActiveAssignments(userId, P.BusinessesUndo)
            .Select(a => new { a.Role.Id, a.Role.Key, a.Role.Rank }).OrderByDescending(r => r.Rank).FirstAsync();

        await staffEdit.BeginAsync(action.BusinessId, userId, role.Id, role.Key);
        staffEdit.UndoOf = action.Id;
        var conflicts = await BusinessSnapshot.UndoAsync(db, action.BusinessId, action.Changes);
        if (conflicts.Count > 0)
            throw new ConflictException($"এই পরিবর্তনের পরে আবার বদলানো হয়েছে: {string.Join(", ", conflicts)}। আগে সেগুলো দেখে নিন।", "changed_since");

        action.UndoneAt = DateTime.UtcNow;
        action.UndoneBy = userId;
        action.UndoReason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        audit.Log("staff_action.undo", "staff_action", action.Id, reason: action.UndoReason, businessId: action.BusinessId,
            scope: RoleScope.Platform, roleId: role.Id);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------------- page defaults ----------------

    [HttpGet("page-defaults")]
    public async Task<ActionResult<PageDefaultsDto>> GetPageDefaults()
    {
        await access.RequireGlobalAsync(P.PageDefaultsManage);
        return await defaults.GetAsync();
    }

    /// <summary>New and not-yet-customised business pages follow this; pages whose owner chose their own look keep it.</summary>
    [HttpPut("page-defaults")]
    public async Task<ActionResult<PageDefaultsDto>> SavePageDefaults(PageDefaultsDto req)
    {
        await access.RequireGlobalAsync(P.PageDefaultsManage);
        if (!PageCatalog.Themes.Contains(req.ThemeKey) || !PageCatalog.HeaderStyles.Contains(req.HeaderStyle))
            throw new BadRequestException("থিম বা হেডারের ধরনটি সঠিক নয়।", "invalid_theme");
        foreach (var s in req.Sections)
        {
            if (!PageCatalog.DefaultOrder.Contains(s.Key))
                throw new BadRequestException("অজানা সেকশন।", "invalid_section");
            if (!s.Visible && !PageCatalog.Hideable.Contains(s.Key))
                throw new BadRequestException("সেবা, রিভিউ ও পরিচিতি সেকশন লুকানো যায় না।", "section_required");
        }
        var value = new PageDefaultsDto(req.ThemeKey, req.HeaderStyle, PageCatalog.Normalize(req.Sections));
        await defaults.SaveAsync(value, me.RequireId());
        audit.Log("site.page_defaults.update", "site_content", PageDefaults.Key, scope: RoleScope.Platform,
            changes: new() { ["theme"] = value.ThemeKey, ["header"] = value.HeaderStyle });
        await db.SaveChangesAsync();
        return value;
    }

    // ---------------- helpers ----------------

    private async Task<List<StaffActionDto>> ToDtosAsync(List<Domain.Entities.StaffAction> rows)
    {
        var userIds = rows.Select(r => r.ActorId).Concat(rows.Where(r => r.UndoneBy != null).Select(r => r.UndoneBy!.Value)).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var bizIds = rows.Select(r => r.BusinessId).Distinct().ToList();
        var businesses = await db.Businesses.IgnoreQueryFilters().Where(b => bizIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name);
        var roleNames = R.All.ToDictionary(r => r.Key, r => r.NameBn);
        return rows.Select(r => new StaffActionDto(r.Id, r.BusinessId, businesses.GetValueOrDefault(r.BusinessId, "—"), r.ActorId,
            names.GetValueOrDefault(r.ActorId, "—"), r.RoleKey, roleNames.GetValueOrDefault(r.RoleKey, r.RoleKey), r.Summary, r.CreatedAt,
            r.UndoneAt, r.UndoneBy is { } u ? names.GetValueOrDefault(u) : null, r.UndoReason, r.UndoOf, r.Changes.Count)).ToList();
    }

    private static readonly string[] TitleFields = ["Name", "Title", "Question", "Label", "Caption", "InviteePhone"];

    private static readonly Dictionary<string, string> FieldLabels = new()
    {
        ["Name"] = "নাম", ["Tagline"] = "ট্যাগলাইন", ["ShortDescription"] = "ছোট বিবরণ", ["Description"] = "বিবরণ",
        ["Phone"] = "ফোন", ["Email"] = "ইমেইল", ["WebsiteUrl"] = "ওয়েবসাইট", ["FacebookUrl"] = "ফেসবুক",
        ["AddressLine"] = "ঠিকানা", ["ZoneId"] = "এলাকা", ["CategoryId"] = "ক্যাটাগরি", ["CoverageNote"] = "সেবার এলাকা",
        ["LogoUrl"] = "লোগো", ["CoverUrl"] = "কভার ছবি", ["ThemeKey"] = "থিম", ["HeaderStyle"] = "হেডারের ধরন",
        ["PageLayout"] = "সেকশন", ["DeletedAt"] = "মুছে ফেলার সময়", ["Status"] = "অবস্থা", ["Languages"] = "ভাষা",
        ["OffersHomeService"] = "হোম সার্ভিস", ["OffersEmergencyService"] = "জরুরি সেবা", ["EstablishedYear"] = "প্রতিষ্ঠার বছর",
        ["Price"] = "দাম", ["PriceMax"] = "সর্বোচ্চ দাম", ["PriceType"] = "দামের ধরন", ["DurationMinutes"] = "সময় (মিনিট)",
        ["ImageUrl"] = "ছবি", ["IsFeatured"] = "ফিচার্ড", ["Availability"] = "পাওয়া যায়", ["Title"] = "শিরোনাম",
        ["Body"] = "লেখা", ["IsPinned"] = "পিন", ["IsPublished"] = "প্রকাশিত", ["ExpiresAt"] = "মেয়াদ", ["Question"] = "প্রশ্ন",
        ["Answer"] = "উত্তর", ["Caption"] = "ক্যাপশন", ["SortOrder"] = "ক্রম", ["RoleId"] = "ভূমিকা", ["ShowOnProfile"] = "পেজে দেখানো",
        ["OpensAt"] = "খোলে", ["ClosesAt"] = "বন্ধ হয়", ["DayOfWeek"] = "দিন", ["AutoConfirm"] = "স্বয়ংক্রিয় নিশ্চিতকরণ",
        ["CancellationPolicy"] = "বাতিলের নিয়ম",
    };

    /// <summary>Internal columns that mean nothing to a reader of the log.</summary>
    private static readonly HashSet<string> HiddenFields = ["Id", "BusinessId", "ServiceId", "CreatedAt", "UpdatedAt", "TokenHash", "CreatedBy",
        "InvitedBy", "NotifiedAt", "Slug"];

    private static RowChangeDto ToRow(Domain.Entities.StaffChange c)
    {
        var set = BusinessSnapshot.Find(c.Entity);
        var kind = c.Before is null ? "added" : c.After is null ? "removed" : "updated";
        var source = c.After ?? c.Before!;
        var title = TitleFields.Select(f => source.TryGetValue(f, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        var fields = kind == "updated"
            ? set.ChangedFields(c.Before!, c.After!)
            : source.Keys.Where(k => source[k].ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)).ToList();
        return new RowChangeDto(c.Entity, set.Label, kind, title, fields.Where(f => !HiddenFields.Contains(f))
            .Select(f => new FieldChangeDto(FieldLabels.GetValueOrDefault(f, f), Text(c.Before, f), Text(c.After, f))).ToList());
    }

    private static string? Text(Dictionary<string, JsonElement>? values, string field)
    {
        if (values is null || !values.TryGetValue(field, out var v)) return null;
        var text = v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => v.GetString(),
            JsonValueKind.True => "হ্যাঁ",
            JsonValueKind.False => "না",
            _ => v.GetRawText(),
        };
        return text is { Length: > 300 } ? text[..300] + "…" : text;
    }
}
