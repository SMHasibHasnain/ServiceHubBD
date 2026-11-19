using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;

namespace ServiceHub.Api.Features.Billing;

/// <summary>The plan a business is effectively on right now, after trial/grace rules.</summary>
public record EffectivePlan(
    string Key, string NameBn, IReadOnlyDictionary<string, int> Limits,
    SubscriptionStatus? Status, BillingCycle? Cycle, DateTime? TrialEndsAt, DateTime? PeriodEnd, DateTime? GraceEndsAt,
    bool CancelAtPeriodEnd)
{
    public int Limit(string key) => Limits.TryGetValue(key, out var v) ? v : 0;
    public bool Has(string key) => Limit(key) != 0;
    public bool IsPaid => Key != PlanCatalog.Free && Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;
    public bool IsTrial => Status == SubscriptionStatus.Trialing && Key != PlanCatalog.Free;
}

public class PlanLimitException(string message) : AppException(StatusCodes.Status403Forbidden, "plan_limit_reached", message);

/// <summary>
/// Answers "what may this business do on its plan?". Effective status is computed from dates, so limits are
/// right even before the maintenance job has flipped an expired trial or period.
/// </summary>
public class EntitlementService(AppDbContext db, IOptions<BillingOptions> options)
{
    private readonly Dictionary<Guid, EffectivePlan> _cache = [];

    public async Task<EffectivePlan> GetAsync(Guid businessId)
    {
        if (_cache.TryGetValue(businessId, out var hit)) return hit;
        var sub = await db.BusinessSubscriptions.AsNoTracking()
            .Include(s => s.Plan).ThenInclude(p => p.Entitlements)
            .FirstOrDefaultAsync(s => s.BusinessId == businessId);
        var now = DateTime.UtcNow;
        var grace = TimeSpan.FromDays(options.Value.GraceDays);

        var live = sub is not null && sub.Status switch
        {
            SubscriptionStatus.Trialing => sub.TrialEndsAt > now,
            // A cancelled-at-period-end plan stops exactly at the end; others get the grace period.
            SubscriptionStatus.Active or SubscriptionStatus.PastDue => sub.CancelAtPeriodEnd ? sub.CurrentPeriodEnd > now : sub.CurrentPeriodEnd + grace > now,
            _ => false,
        };

        EffectivePlan plan;
        if (live)
        {
            var inGrace = sub!.CurrentPeriodEnd is { } end && end <= now;
            plan = new EffectivePlan(sub.Plan.Key, sub.Plan.NameBn, sub.Plan.Entitlements.ToDictionary(e => e.Key, e => e.Value),
                inGrace ? SubscriptionStatus.PastDue : sub.Status, sub.Cycle, sub.TrialEndsAt, sub.CurrentPeriodEnd,
                sub.CurrentPeriodEnd is { } e2 ? e2 + grace : null, sub.CancelAtPeriodEnd);
        }
        else
        {
            var free = PlanCatalog.Get(PlanCatalog.Free);
            plan = new EffectivePlan(free.Key, free.NameBn, free.Entitlements,
                sub?.Status is SubscriptionStatus.Cancelled ? SubscriptionStatus.Cancelled : sub is null ? null : SubscriptionStatus.Expired,
                null, sub?.TrialEndsAt, sub?.CurrentPeriodEnd, null, false);
        }
        return _cache[businessId] = plan;
    }

    public async Task<bool> HasAsync(Guid businessId, string key) => (await GetAsync(businessId)).Has(key);

    /// <summary>Throws when adding <paramref name="adding"/> more would exceed the plan's limit.</summary>
    public async Task RequireCapacityAsync(Guid businessId, string key, int current, int adding = 1)
    {
        var plan = await GetAsync(businessId);
        var limit = plan.Limit(key);
        if (limit == E.Unlimited || current + adding <= limit) return;
        var (noun, unit) = key switch
        {
            E.ServicesMax => ("সেবা", "টি"),
            E.MembersMax => ("টিম সদস্য", " জন"),
            E.BookingFieldsMax => ("প্রশ্ন (প্রতি সেবায়)", "টি"),
            _ => ("আইটেম", "টি"),
        };
        throw new PlanLimitException($"{plan.NameBn} প্ল্যানে সর্বোচ্চ {Bn(limit)}{unit} {noun} রাখা যায়। আরও যোগ করতে প্ল্যান আপগ্রেড করুন।");
    }

    public async Task RequireFeatureAsync(Guid businessId, string key)
    {
        var plan = await GetAsync(businessId);
        if (plan.Has(key)) return;
        var what = key switch
        {
            E.CrmNotes => "গ্রাহকের নোট, VIP ও ট্যাগ",
            E.AutoConfirm => "বুকিং স্বয়ংক্রিয় নিশ্চিতকরণ",
            _ => "এই সুবিধা",
        };
        throw new PlanLimitException($"{what} {plan.NameBn} প্ল্যানে নেই। প্রো বা বিজনেস প্ল্যানে আপগ্রেড করুন।");
    }

    /// <summary>Analytics range allowed on the plan; asking for more is an upgrade prompt, not a silent clamp.</summary>
    public async Task<int> AnalyticsDaysAsync(Guid businessId, int? requested)
    {
        var plan = await GetAsync(businessId);
        var max = plan.Limit(E.AnalyticsMaxDays);
        if (requested is null) return max == E.Unlimited || max >= 30 ? 30 : 7;
        if (max != E.Unlimited && requested > max)
            throw new PlanLimitException($"{plan.NameBn} প্ল্যানে শুধু গত {Bn(max)} দিনের বিশ্লেষণ দেখা যায়। দীর্ঘ সময়ের তথ্য দেখতে প্ল্যান আপগ্রেড করুন।");
        return requested.Value;
    }

    /// <summary>Team size as counted against members.max: active non-owner members plus open invitations.</summary>
    public async Task<int> TeamSizeAsync(Guid businessId, string? exceptInvitePhone = null)
    {
        var now = DateTime.UtcNow;
        var members = await db.BusinessMembers.CountAsync(m => m.BusinessId == businessId &&
            (m.Status == MemberStatus.Active || m.Status == MemberStatus.Suspended) && m.Role.Key != Access.R.BusinessOwner);
        var invites = await db.BusinessInvitations.CountAsync(i => i.BusinessId == businessId && i.Status == InvitationStatus.Pending &&
            i.ExpiresAt > now && (exceptInvitePhone == null || i.InviteePhone != exceptInvitePhone));
        return members + invites;
    }

    public void Forget(Guid businessId) => _cache.Remove(businessId);

    internal static string Bn(long n) => string.Concat(n.ToString().Select(c => char.IsDigit(c) ? "০১২৩৪৫৬৭৮৯"[c - '0'] : c));
}
