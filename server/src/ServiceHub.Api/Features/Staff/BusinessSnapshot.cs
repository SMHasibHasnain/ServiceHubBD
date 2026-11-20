using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Staff;

/// <summary>
/// The tables a staff member can change on a business, read as plain rows so an edit can be diffed and undone without
/// each endpoint knowing about it. Works on rows, not on EF change tracking, so bulk <c>ExecuteDelete/Update</c>
/// calls (opening hours, invitations) are captured too.
/// </summary>
public static class BusinessSnapshot
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Changed on every write or by the system in the background; never diffed, compared or restored.</summary>
    private static readonly HashSet<string> AlwaysVolatile = ["UpdatedAt"];

    /// <summary>Registry order = parents before children (EF orders the actual inserts and deletes itself).</summary>
    internal static readonly TrackedSet[] Sets =
    [
        new TrackedSet<Business>("business", "ব্যবসার তথ্য", (db, id) => db.Businesses.IgnoreQueryFilters().Where(x => x.Id == id),
            "AvgRating", "ReviewCount", "CompletedBookingCount", "AvgResponseMinutes", "HealthScore", "HealthScoreUpdatedAt", "PublishedAt"),
        new TrackedSet<BusinessBookingSettings>("booking_settings", "বুকিং সেটিংস", (db, id) => db.BusinessBookingSettings.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<BusinessHour>("hours", "কাজের সময়", (db, id) => db.BusinessHours.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<BusinessClosure>("closures", "বন্ধের দিন", (db, id) => db.BusinessClosures.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<Service>("services", "সেবা", (db, id) => db.Services.IgnoreQueryFilters().Where(x => x.BusinessId == id),
            "BookingCount", "AvgRating", "ViewCount", "LastBookedAt"),
        new TrackedSet<ServiceBookingField>("service_fields", "বুকিং ফর্মের প্রশ্ন", (db, id) => db.Set<ServiceBookingField>().IgnoreQueryFilters()
            .Where(f => db.Services.IgnoreQueryFilters().Any(s => s.Id == f.ServiceId && s.BusinessId == id))),
        new TrackedSet<BusinessNotice>("notices", "নোটিশ বোর্ড", (db, id) => db.BusinessNotices.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<BusinessFaq>("faqs", "প্রশ্নোত্তর", (db, id) => db.BusinessFaqs.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<BusinessHighlight>("highlights", "কেন আমাদের বেছে নেবেন", (db, id) => db.BusinessHighlights.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<GalleryItem>("gallery", "গ্যালারি", (db, id) => db.GalleryItems.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<BusinessMember>("members", "টিম সদস্য", (db, id) => db.BusinessMembers.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
        new TrackedSet<BusinessInvitation>("invitations", "টিম আমন্ত্রণ", (db, id) => db.BusinessInvitations.IgnoreQueryFilters().Where(x => x.BusinessId == id)),
    ];

    public static TrackedSet Find(string name) => Sets.First(s => s.Name == name);

    public static async Task<Dictionary<string, Dictionary<string, Row>>> TakeAsync(AppDbContext db, Guid businessId)
    {
        var result = new Dictionary<string, Dictionary<string, Row>>();
        foreach (var set in Sets) result[set.Name] = await set.SnapshotAsync(db, businessId);
        return result;
    }

    /// <summary>Rows that differ between two snapshots (ignoring volatile columns).</summary>
    public static List<StaffChange> Diff(Dictionary<string, Dictionary<string, Row>> before, Dictionary<string, Dictionary<string, Row>> after)
    {
        var changes = new List<StaffChange>();
        foreach (var set in Sets)
        {
            var b = before[set.Name];
            var a = after[set.Name];
            foreach (var key in b.Keys.Union(a.Keys))
            {
                b.TryGetValue(key, out var was);
                a.TryGetValue(key, out var now);
                if (was is not null && now is not null && set.ChangedFields(was, now).Count == 0) continue;
                changes.Add(new StaffChange(set.Name, key, was?.Values, now?.Values));
            }
        }
        return changes;
    }

    /// <summary>
    /// Puts every changed row back as it was before. Only the fields the edit itself changed are compared and restored, so
    /// unrelated later activity (a review updating the rating, a moderator publishing the page) doesn't block an undo.
    /// Returns the labels of the parts that were changed again since; nothing is applied when there are any.
    /// </summary>
    public static async Task<List<string>> UndoAsync(AppDbContext db, Guid businessId, List<StaffChange> changes)
    {
        var conflicts = new List<string>();
        var plans = new List<Action>();
        foreach (var group in changes.GroupBy(c => c.Entity))
        {
            var set = Find(group.Key);
            var (ok, apply) = await set.PrepareUndoAsync(db, businessId, group.ToList());
            if (!ok) conflicts.Add(set.Label);
            else plans.Add(apply);
        }
        if (conflicts.Count == 0) plans.ForEach(p => p());
        return conflicts;
    }

    /// <summary>"সেবা, কাজের সময়" style summary of what a list of changes touched.</summary>
    public static string Summarize(List<StaffChange> changes)
    {
        var parts = new List<string>();
        foreach (var c in changes)
        {
            var set = Find(c.Entity);
            var label = set.Label;
            if (c.Entity == "business" && c.Before is not null && c.After is not null)
            {
                var fields = set.ChangedFields(c.Before, c.After);
                if (fields.Contains("DeletedAt"))
                    label = c.After.TryGetValue("DeletedAt", out var d) && d.ValueKind != JsonValueKind.Null ? "ব্যবসা মুছে ফেলা" : "ব্যবসা ফিরিয়ে আনা";
                else if (fields.All(f => f is "ThemeKey" or "HeaderStyle" or "PageLayout")) label = "পেজের থিম ও সেকশন";
                else if (fields.All(f => f is "LogoUrl" or "CoverUrl")) label = "লোগো ও কভার ছবি";
            }
            if (!parts.Contains(label)) parts.Add(label);
        }
        return string.Join(", ", parts);
    }

    public sealed record Row(Dictionary<string, JsonElement> Values);

    // ---------------- JSON comparison ----------------

    /// <summary>Semantic equality: object key order doesn't matter (jsonb reorders keys), numbers compare by value.</summary>
    public static bool Same(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
            return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var ap = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bp = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                return ap.Count == bp.Count && ap.All(kv => bp.TryGetValue(kv.Key, out var v) && Same(kv.Value, v));
            case JsonValueKind.Array:
                var aa = a.EnumerateArray().ToList();
                var ba = b.EnumerateArray().ToList();
                return aa.Count == ba.Count && aa.Zip(ba).All(t => Same(t.First, t.Second));
            case JsonValueKind.Number:
                return a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) ? x == y : a.GetRawText() == b.GetRawText();
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            default:
                return true; // true, false, null
        }
    }

    // ---------------- one table ----------------

    public abstract class TrackedSet(string name, string label, string[] volatileProps)
    {
        public string Name { get; } = name;
        public string Label { get; } = label;
        protected readonly HashSet<string> Volatile = [.. AlwaysVolatile, .. volatileProps];

        public abstract Task<Dictionary<string, Row>> SnapshotAsync(AppDbContext db, Guid businessId);
        public abstract Task<(bool Ok, Action Apply)> PrepareUndoAsync(AppDbContext db, Guid businessId, List<StaffChange> changes);

        public List<string> ChangedFields(Row before, Row after) => ChangedFields(before.Values, after.Values);

        public List<string> ChangedFields(Dictionary<string, JsonElement> before, Dictionary<string, JsonElement> after) =>
            before.Keys.Union(after.Keys).Where(k => !Volatile.Contains(k) &&
                !(before.TryGetValue(k, out var x) && after.TryGetValue(k, out var y) && Same(x, y))).ToList();
    }

    public sealed class TrackedSet<T>(string name, string label, Func<AppDbContext, Guid, IQueryable<T>> query, params string[] volatileProps)
        : TrackedSet(name, label, volatileProps) where T : class, new()
    {
        public override async Task<Dictionary<string, Row>> SnapshotAsync(AppDbContext db, Guid businessId)
        {
            var type = EntityType(db);
            var rows = await query(db, businessId).AsNoTracking().ToListAsync();
            return rows.ToDictionary(r => KeyOf(type, r), r => new Row(ValuesOf(type, r)));
        }

        public override async Task<(bool Ok, Action Apply)> PrepareUndoAsync(AppDbContext db, Guid businessId, List<StaffChange> changes)
        {
            var type = EntityType(db);
            var current = (await query(db, businessId).ToListAsync()).ToDictionary(r => KeyOf(type, r));
            foreach (var c in changes)
            {
                current.TryGetValue(c.Key, out var row);
                var ok = c.After is null
                    ? row is null
                    : row is not null && (c.Before is null
                        ? ChangedFields(ValuesOf(type, row), c.After).Count == 0
                        : ChangedFields(c.Before, c.After).All(f => Same(Value(ValuesOf(type, row), f), Value(c.After, f))));
                if (!ok) return (false, () => { });
            }

            return (true, () =>
            {
                foreach (var c in changes)
                {
                    current.TryGetValue(c.Key, out var row);
                    if (c.Before is null) db.Remove(row!);
                    else if (c.After is null)
                    {
                        var entity = new T();
                        foreach (var p in Columns(type))
                            if (c.Before.TryGetValue(p.Name, out var v)) p.PropertyInfo!.SetValue(entity, v.Deserialize(p.ClrType, Json));
                        db.Add(entity);
                    }
                    else
                        foreach (var f in ChangedFields(c.Before, c.After))
                        {
                            var p = type.FindProperty(f);
                            if (p?.PropertyInfo is null || p.IsPrimaryKey()) continue;
                            p.PropertyInfo.SetValue(row, Value(c.Before, f).Deserialize(p.ClrType, Json));
                        }
                }
            });
        }

        private static JsonElement Value(Dictionary<string, JsonElement> values, string field) =>
            values.TryGetValue(field, out var v) ? v : default;

        private static IEntityType EntityType(AppDbContext db) => db.Model.FindEntityType(typeof(T))!;

        private static IEnumerable<IProperty> Columns(IEntityType type) =>
            type.GetProperties().Where(p => p.PropertyInfo is not null && !p.IsShadowProperty());

        private static string KeyOf(IEntityType type, T row) =>
            string.Join("|", type.FindPrimaryKey()!.Properties.Select(p => p.PropertyInfo!.GetValue(row)?.ToString()));

        private static Dictionary<string, JsonElement> ValuesOf(IEntityType type, T row) =>
            Columns(type).ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.PropertyInfo!.GetValue(row), p.ClrType, Json));
    }
}
