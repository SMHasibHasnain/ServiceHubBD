using System.Text.Json;

namespace ServiceHub.Api.Domain.Entities;

/// <summary>
/// One edit a platform staff member (moderator, admin, super admin) made to a business they are not a member of.
/// <see cref="Changes"/> holds every row that changed, before and after, so an admin can undo it later.
/// </summary>
public class StaffAction
{
    public long Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ActorId { get; set; }
    /// <summary>The platform role that granted the edit (e.g. <c>system_moderator</c>).</summary>
    public short? RoleId { get; set; }
    public string RoleKey { get; set; } = "";
    /// <summary>HTTP method and route template of the request, e.g. <c>PUT api/businesses/{businessId}/page/appearance</c>.</summary>
    public string Action { get; set; } = "";
    /// <summary>Short Bangla description built from what changed, e.g. "সেবা, কাজের সময়".</summary>
    public string Summary { get; set; } = "";
    public List<StaffChange> Changes { get; set; } = [];
    /// <summary>Set when this action is itself the undo of another one.</summary>
    public long? UndoOf { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UndoneAt { get; set; }
    public Guid? UndoneBy { get; set; }
    public string? UndoReason { get; set; }
}

/// <summary>
/// One row of one table. <see cref="Before"/> is null for an inserted row, <see cref="After"/> null for a deleted one.
/// Values are the entity's properties serialised with System.Text.Json (web defaults).
/// </summary>
public record StaffChange(string Entity, string Key, Dictionary<string, JsonElement>? Before, Dictionary<string, JsonElement>? After);
