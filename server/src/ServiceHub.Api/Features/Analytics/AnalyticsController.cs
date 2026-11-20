using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Features.Billing;

namespace ServiceHub.Api.Features.Analytics;

// ---------- Shared shapes: every workspace's analytics speaks the same small vocabulary ----------

/// <summary>A headline number for the selected period, with the same measure for the period before it.</summary>
public record Metric(double Value, double? Previous);

/// <summary>One day (Bangladesh time) of a multi-series trend; <c>Values</c> follow <see cref="TimeSeries.Keys"/>.</summary>
public record SeriesPoint(DateOnly Date, double[] Values);

public record TimeSeries(string[] Keys, List<SeriesPoint> Points);

/// <summary>A labelled amount (a bar, a segment, a row). <c>Secondary</c> carries a second measure where useful.</summary>
public record Slice(string Key, string Label, double Value, double? Secondary = null);

public record Period(int Days, DateOnly From, DateOnly To, DateTime StartUtc, DateTime EndUtc, DateTime PrevStartUtc)
{
    public static Period Last(int days)
    {
        days = days is 7 or 30 or 90 ? days : 30;
        var to = BdTime.Today();
        var from = to.AddDays(-(days - 1));
        var start = BdTime.ToUtc(from, TimeOnly.MinValue);
        return new Period(days, from, to, start, BdTime.ToUtc(to.AddDays(1), TimeOnly.MinValue), start.AddDays(-days));
    }

    public bool InCurrent(DateTime utc) => utc >= StartUtc && utc < EndUtc;
    public bool InPrevious(DateTime utc) => utc >= PrevStartUtc && utc < StartUtc;
    public DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(BdTime.ToLocal(utc));

    /// <summary>Buckets rows into one point per day of the period; empty days are zero, never missing.</summary>
    public TimeSeries Daily<T>(string[] keys, IEnumerable<T> rows, Func<T, DateTime?> when, Func<T, double[]> values)
    {
        var points = Enumerable.Range(0, Days).Select(i => From.AddDays(i)).ToDictionary(d => d, _ => new double[keys.Length]);
        foreach (var row in rows)
        {
            if (when(row) is not { } t || !InCurrent(t)) continue;
            if (!points.TryGetValue(LocalDate(t), out var acc)) continue;
            var v = values(row);
            for (var i = 0; i < keys.Length; i++) acc[i] += v[i];
        }
        return new TimeSeries(keys, points.OrderBy(p => p.Key).Select(p => new SeriesPoint(p.Key, p.Value)).ToList());
    }
}

internal static class Stats
{
    public static Metric Count<T>(IEnumerable<T> rows, Func<T, DateTime?> when, Period p) =>
        new(rows.Count(r => when(r) is { } t && p.InCurrent(t)), rows.Count(r => when(r) is { } t && p.InPrevious(t)));

    public static double Rate(double part, double whole) => whole == 0 ? 0 : Math.Round(part / whole * 100, 1);

    public static double? Avg(IEnumerable<double> xs)
    {
        var list = xs.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 1);
    }

    public static Metric Ratio(Func<bool, (double part, double whole)> f)
    {
        var (cp, cw) = f(true);
        var (pp, pw) = f(false);
        return new Metric(Rate(cp, cw), pw == 0 ? null : Rate(pp, pw));
    }

    public static readonly string[] StatusOrder =
        ["completed", "confirmed", "pending", "rescheduled", "cancelled", "rejected", "no_show"];

    public static readonly Dictionary<string, string> StatusLabel = new()
    {
        ["pending"] = "অপেক্ষমাণ", ["confirmed"] = "নিশ্চিত", ["rejected"] = "প্রত্যাখ্যাত", ["rescheduled"] = "সময় পরিবর্তিত",
        ["completed"] = "সম্পন্ন", ["cancelled"] = "বাতিল", ["no_show"] = "আসেননি",
    };

    public static List<Slice> StatusMix(IEnumerable<BookingStatus> statuses)
    {
        var counts = statuses.GroupBy(SnakeCase.FromEnum).ToDictionary(g => g.Key, g => g.Count());
        return StatusOrder.Where(counts.ContainsKey).Select(k => new Slice(k, StatusLabel[k], counts[k])).ToList();
    }

    public static bool IsLost(BookingStatus s) => s is BookingStatus.Cancelled or BookingStatus.Rejected or BookingStatus.NoShow;
}

// ---------- Business workspace ----------

public record BusinessAnalyticsDto(int Days, DateOnly From, DateOnly To,
    Dictionary<string, Metric> Kpis, TimeSeries Trend, List<Slice> StatusMix, List<Slice> TopServices, List<Slice> Staff,
    int[][] Heatmap, List<Slice> Ratings, List<Slice> Sources);

[ApiController]
[Authorize]
public class AnalyticsController(AppDbContext db, ICurrentUser me, AccessService access, EntitlementService plans) : ControllerBase
{
    /// <summary>
    /// Business performance for the last 7/30/90 days, compared with the period before it.
    /// Bookings are bucketed by the day they were received; revenue counts those that are now completed.
    /// The heatmap uses the appointment time, to show when demand actually lands.
    /// </summary>
    [HttpGet("api/businesses/{businessId:guid}/analytics")]
    public async Task<ActionResult<BusinessAnalyticsDto>> Business(Guid businessId, [FromQuery] int? days = null)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.AnalyticsView);
        var p = Period.Last(await plans.AnalyticsDaysAsync(businessId, days is 7 or 30 or 90 ? days : days is null ? null : 30));

        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.BusinessId == businessId && b.CreatedAt >= p.PrevStartUtc && b.CreatedAt < p.EndUtc)
            .Select(b => new
            {
                b.StartAt, b.CreatedAt, b.ConfirmedAt, b.Status, b.Source, b.ServiceId, b.ServiceName, b.AssignedMemberId,
                b.BusinessCustomerId, Price = b.FinalPrice ?? b.QuotedPrice ?? 0,
            })
            .ToListAsync();
        var cur = rows.Where(r => p.InCurrent(r.CreatedAt)).ToList();
        var prev = rows.Where(r => p.InPrevious(r.CreatedAt)).ToList();
        double RevenueOf<T>(List<T> xs, Func<T, BookingStatus> st, Func<T, decimal> price) =>
            (double)xs.Where(x => st(x) == BookingStatus.Completed).Sum(price);

        var customers = await db.BusinessCustomers.AsNoTracking().Where(c => c.BusinessId == businessId)
            .Select(c => new { c.Id, c.FirstBookingAt }).ToListAsync();
        var firstSeen = customers.ToDictionary(c => c.Id, c => c.FirstBookingAt);
        (double part, double whole) Returning(bool current)
        {
            var set = (current ? cur : prev).Select(r => r.BusinessCustomerId).Distinct().ToList();
            var since = current ? p.StartUtc : p.PrevStartUtc;
            return (set.Count(id => firstSeen.GetValueOrDefault(id) is { } f && f < since), set.Count);
        }

        var reviews = await db.Reviews.AsNoTracking()
            .Where(r => r.BusinessId == businessId && r.Status == ReviewStatus.Published)
            .Select(r => new { r.Rating, r.CreatedAt }).ToListAsync();
        double? AvgRating(Func<DateTime, bool> inPeriod) => Stats.Avg(reviews.Where(r => inPeriod(r.CreatedAt)).Select(r => (double)r.Rating));

        var confirmRows = rows.Where(r => r.ConfirmedAt != null).Select(r => (r.ConfirmedAt!.Value, Minutes: (r.ConfirmedAt.Value - r.CreatedAt).TotalMinutes)).ToList();

        var completedCur = cur.Count(r => r.Status == BookingStatus.Completed);
        var completedPrev = prev.Count(r => r.Status == BookingStatus.Completed);
        var revenueCur = RevenueOf(cur, r => r.Status, r => r.Price);
        var revenuePrev = RevenueOf(prev, r => r.Status, r => r.Price);

        var kpis = new Dictionary<string, Metric>
        {
            ["bookings"] = new(cur.Count, prev.Count),
            ["completed"] = new(completedCur, completedPrev),
            ["revenue"] = new(revenueCur, revenuePrev),
            ["avg_ticket"] = new(completedCur == 0 ? 0 : Math.Round(revenueCur / completedCur), completedPrev == 0 ? null : Math.Round(revenuePrev / completedPrev)),
            ["completion_rate"] = Stats.Ratio(c => { var s = c ? cur : prev; return (s.Count(r => r.Status == BookingStatus.Completed), s.Count(r => r.Status == BookingStatus.Completed || Stats.IsLost(r.Status))); }),
            ["cancellation_rate"] = Stats.Ratio(c => { var s = c ? cur : prev; return (s.Count(r => r.Status is BookingStatus.Cancelled or BookingStatus.Rejected), s.Count); }),
            ["no_show_rate"] = Stats.Ratio(c => { var s = c ? cur : prev; return (s.Count(r => r.Status == BookingStatus.NoShow), s.Count); }),
            ["new_customers"] = Stats.Count(customers, c => c.FirstBookingAt, p),
            ["returning_rate"] = Stats.Ratio(Returning),
            ["reviews"] = Stats.Count(reviews, r => r.CreatedAt, p),
            ["avg_rating"] = new(AvgRating(p.InCurrent) ?? 0, AvgRating(p.InPrevious)),
            ["response_minutes"] = new(
                Stats.Avg(confirmRows.Where(x => p.InCurrent(x.Value)).Select(x => x.Minutes)) is { } rc ? Math.Round(rc) : 0,
                Stats.Avg(confirmRows.Where(x => p.InPrevious(x.Value)).Select(x => x.Minutes)) is { } rp ? Math.Round(rp) : null),
        };

        var trend = p.Daily(["bookings", "completed", "revenue"], cur, r => r.CreatedAt, r =>
            [1, r.Status == BookingStatus.Completed ? 1 : 0, r.Status == BookingStatus.Completed ? (double)r.Price : 0]);

        var topServices = cur.GroupBy(r => (r.ServiceId, r.ServiceName))
            .Select(g => new Slice(g.Key.ServiceId.ToString(), g.Key.ServiceName, g.Count(),
                (double)g.Where(x => x.Status == BookingStatus.Completed).Sum(x => x.Price)))
            .OrderByDescending(s => s.Value).Take(8).ToList();

        var memberIds = cur.Where(r => r.AssignedMemberId != null).Select(r => r.AssignedMemberId!.Value).Distinct().ToList();
        var memberNames = await db.BusinessMembers.AsNoTracking().Where(m => memberIds.Contains(m.Id))
            .Select(m => new { m.Id, m.User.FullName }).ToDictionaryAsync(m => m.Id, m => m.FullName);
        var staff = cur.GroupBy(r => r.AssignedMemberId)
            .Select(g => new Slice(g.Key?.ToString() ?? "unassigned",
                g.Key is { } id ? memberNames.GetValueOrDefault(id, "—") : "নির্ধারিত হয়নি",
                g.Count(), g.Count(x => x.Status == BookingStatus.Completed)))
            .OrderByDescending(s => s.Value).Take(8).ToList();

        // Bangladesh week (Sat = 0 … Fri = 6) × hour of day, by scheduled start.
        var heat = Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();
        foreach (var r in cur)
        {
            var local = BdTime.ToLocal(r.StartAt);
            heat[BdTime.BdDayOfWeek(DateOnly.FromDateTime(local))][local.Hour]++;
        }

        var ratings = Enumerable.Range(1, 5).Reverse()
            .Select(star => new Slice(star.ToString(), $"{"০১২৩৪৫"[star]} তারা", reviews.Count(r => r.Rating == star))).ToList();

        var sourceLabel = new Dictionary<BookingSource, string>
        {
            [BookingSource.Web] = "ওয়েবসাইট", [BookingSource.MobileApp] = "মোবাইল অ্যাপ", [BookingSource.SharedLink] = "শেয়ার করা লিংক",
            [BookingSource.Promotion] = "প্রোমোশন", [BookingSource.Manual] = "ম্যানুয়াল",
        };
        var sources = cur.GroupBy(r => r.Source).Select(g => new Slice(SnakeCase.FromEnum(g.Key), sourceLabel[g.Key], g.Count()))
            .OrderByDescending(s => s.Value).ToList();

        return new BusinessAnalyticsDto(p.Days, p.From, p.To, kpis, trend, Stats.StatusMix(cur.Select(r => r.Status)),
            topServices, staff, heat, ratings, sources);
    }

    // ---------- Moderation workspace ----------

    public record ModerationAnalyticsDto(int Days, DateOnly From, DateOnly To, Dictionary<string, Metric> Kpis,
        TimeSeries Trend, List<Slice> Queue, List<Slice> Aging, List<Slice> Outcomes, List<Slice> ReportReasons,
        List<Slice> Moderators, List<Slice> PendingByCategory);

    /// <summary>Verification throughput, queue health and report handling, limited to the moderator's zone/category scope.</summary>
    [HttpGet("api/moderation/analytics")]
    public async Task<ActionResult<ModerationAnalyticsDto>> Moderation([FromQuery] int days = 30)
    {
        await access.RequirePlatformAsync(P.VerificationReview);
        var p = Period.Last(days);
        var scoped = access.ScopedBusinesses(P.VerificationReview).Select(b => b.Id);

        var requests = await db.VerificationRequests.AsNoTracking()
            .Where(v => scoped.Contains(v.BusinessId) && v.Status != VerificationStatus.Draft)
            .Select(v => new
            {
                v.Status, v.SubmittedAt, v.ReviewStartedAt, v.DecidedAt,
                CategoryId = v.Business.Category.ParentId ?? v.Business.CategoryId,
            })
            .ToListAsync();

        var actions = await db.ModerationActions.AsNoTracking()
            .Where(a => a.CreatedAt >= p.PrevStartUtc && a.CreatedAt < p.EndUtc &&
                        (a.BusinessId == null || scoped.Contains(a.BusinessId.Value)))
            .Select(a => new { a.ModeratorId, a.Action, a.CreatedAt })
            .ToListAsync();

        var canReports = await access.HasPlatformPermissionAsync(P.ReportsReview);
        var reportScope = access.ScopedBusinesses(P.ReportsReview).Select(b => b.Id);
        var globalReports = await access.HasGlobalPermissionAsync(P.ReportsReview);
        var reports = !canReports ? [] : await db.Reports.AsNoTracking()
            .Where(r => r.TargetBusinessId != null ? reportScope.Contains(r.TargetBusinessId.Value) : globalReports)
            .Select(r => new { r.Status, r.Reason, r.CreatedAt, r.ResolvedAt })
            .ToListAsync();

        bool Pending(VerificationStatus s) => s is VerificationStatus.Submitted or VerificationStatus.UnderReview or VerificationStatus.InfoRequested;
        bool Decided(VerificationStatus s) => s is VerificationStatus.Approved or VerificationStatus.Rejected;
        double Hours(DateTime a, DateTime b) => (b - a).TotalHours;

        var decided = requests.Where(r => Decided(r.Status) && r.DecidedAt != null).ToList();
        (double, double) Approval(bool c)
        {
            var set = decided.Where(r => c ? p.InCurrent(r.DecidedAt!.Value) : p.InPrevious(r.DecidedAt!.Value)).ToList();
            return (set.Count(r => r.Status == VerificationStatus.Approved), set.Count);
        }

        var resolvedReports = reports.Where(r => r.ResolvedAt != null).ToList();
        var kpis = new Dictionary<string, Metric>
        {
            ["submitted"] = Stats.Count(requests, r => r.SubmittedAt, p),
            ["decided"] = Stats.Count(decided, r => r.DecidedAt, p),
            ["approval_rate"] = Stats.Ratio(Approval),
            ["decision_hours"] = new(
                Stats.Avg(decided.Where(r => p.InCurrent(r.DecidedAt!.Value)).Select(r => Hours(r.SubmittedAt, r.DecidedAt!.Value))) ?? 0,
                Stats.Avg(decided.Where(r => p.InPrevious(r.DecidedAt!.Value)).Select(r => Hours(r.SubmittedAt, r.DecidedAt!.Value)))),
            ["first_review_hours"] = new(
                Stats.Avg(requests.Where(r => r.ReviewStartedAt is { } t && p.InCurrent(t)).Select(r => Hours(r.SubmittedAt, r.ReviewStartedAt!.Value))) ?? 0,
                Stats.Avg(requests.Where(r => r.ReviewStartedAt is { } t && p.InPrevious(t)).Select(r => Hours(r.SubmittedAt, r.ReviewStartedAt!.Value)))),
            ["pending_now"] = new(requests.Count(r => Pending(r.Status)), null),
            ["reports_opened"] = Stats.Count(reports, r => r.CreatedAt, p),
            ["reports_resolved"] = Stats.Count(resolvedReports, r => r.ResolvedAt, p),
            ["reports_open_now"] = new(reports.Count(r => r.Status is ReportStatus.Open or ReportStatus.UnderReview or ReportStatus.Escalated), null),
            ["report_resolution_hours"] = new(
                Stats.Avg(resolvedReports.Where(r => p.InCurrent(r.ResolvedAt!.Value)).Select(r => Hours(r.CreatedAt, r.ResolvedAt!.Value))) ?? 0,
                Stats.Avg(resolvedReports.Where(r => p.InPrevious(r.ResolvedAt!.Value)).Select(r => Hours(r.CreatedAt, r.ResolvedAt!.Value)))),
        };

        var trendRows = requests.Select(r => (When: (DateTime?)r.SubmittedAt, V: new double[] { 1, 0 }))
            .Concat(decided.Select(r => (When: r.DecidedAt, V: new double[] { 0, 1 })));
        var trend = p.Daily(["submitted", "decided"], trendRows, x => x.When, x => x.V);

        var queue = new[]
        {
            new Slice("submitted", "নতুন জমা", requests.Count(r => r.Status == VerificationStatus.Submitted)),
            new Slice("under_review", "পর্যালোচনাধীন", requests.Count(r => r.Status == VerificationStatus.UnderReview)),
            new Slice("info_requested", "তথ্য চাওয়া হয়েছে", requests.Count(r => r.Status == VerificationStatus.InfoRequested)),
        }.ToList();

        var now = DateTime.UtcNow;
        var pendingAges = requests.Where(r => Pending(r.Status)).Select(r => (now - r.SubmittedAt).TotalHours).ToList();
        var aging = new List<Slice>
        {
            new("lt_1d", "২৪ ঘণ্টার কম", pendingAges.Count(h => h < 24)),
            new("1_3d", "১–৩ দিন", pendingAges.Count(h => h >= 24 && h < 72)),
            new("3_7d", "৩–৭ দিন", pendingAges.Count(h => h >= 72 && h < 168)),
            new("gt_7d", "৭ দিনের বেশি", pendingAges.Count(h => h >= 168)),
        };

        var curActions = actions.Where(a => p.InCurrent(a.CreatedAt)).ToList();
        var outcomes = new List<Slice>
        {
            new("approved", "অনুমোদিত", curActions.Count(a => a.Action == ModerationActionType.ApproveBusiness)),
            new("rejected", "প্রত্যাখ্যাত", curActions.Count(a => a.Action == ModerationActionType.RejectBusiness)),
            new("info_requested", "তথ্য চাওয়া", curActions.Count(a => a.Action == ModerationActionType.RequestInformation)),
            new("inspection", "পরিদর্শনে পাঠানো", curActions.Count(a => a.Action == ModerationActionType.RequestInspection)),
        };

        var reasonLabel = new Dictionary<ReportReason, string>
        {
            [ReportReason.FakeBusiness] = "ভুয়া ব্যবসা", [ReportReason.Spam] = "স্প্যাম", [ReportReason.Fraud] = "প্রতারণা",
            [ReportReason.AbusiveBehaviour] = "অশোভন আচরণ", [ReportReason.MisleadingInformation] = "বিভ্রান্তিকর তথ্য", [ReportReason.InappropriateContent] = "অনুপযুক্ত ছবি বা লেখা", [ReportReason.Other] = "অন্যান্য",
        };
        var reportReasons = reports.Where(r => p.InCurrent(r.CreatedAt)).GroupBy(r => r.Reason)
            .Select(g => new Slice(SnakeCase.FromEnum(g.Key), reasonLabel.GetValueOrDefault(g.Key, "অন্যান্য"), g.Count()))
            .OrderByDescending(s => s.Value).ToList();

        var modIds = curActions.Select(a => a.ModeratorId).Distinct().ToList();
        var modNames = await db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => modIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);
        var moderators = curActions.GroupBy(a => a.ModeratorId)
            .Select(g => new Slice(g.Key.ToString(), modNames.GetValueOrDefault(g.Key, "—"), g.Count(),
                g.Count(a => a.Action is ModerationActionType.ApproveBusiness or ModerationActionType.RejectBusiness)))
            .OrderByDescending(s => s.Value).Take(10).ToList();

        var catNames = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.NameBn);
        var byCategory = requests.Where(r => Pending(r.Status)).GroupBy(r => r.CategoryId)
            .Select(g => new Slice(g.Key.ToString(), catNames.GetValueOrDefault(g.Key, "—"), g.Count()))
            .OrderByDescending(s => s.Value).ToList();

        return new ModerationAnalyticsDto(p.Days, p.From, p.To, kpis, trend, queue, aging, outcomes, reportReasons, moderators, byCategory);
    }

    // ---------- Inspection workspace ----------

    public record InspectionAnalyticsDto(int Days, DateOnly From, DateOnly To, Dictionary<string, Metric> Kpis,
        TimeSeries Trend, List<Slice> Outcomes, List<Slice> ByZone, List<Slice> ByType);

    /// <summary>Field work inside the inspector's zones: workload, turnaround and outcomes.</summary>
    [HttpGet("api/inspections/analytics")]
    public async Task<ActionResult<InspectionAnalyticsDto>> Inspections([FromQuery] int days = 30)
    {
        await access.RequirePlatformAsync(P.InspectionsPerform);
        var p = Period.Last(days);
        var userId = me.RequireId();
        var assignments = access.ActiveAssignments(userId, P.InspectionsPerform);

        var rows = await db.Inspections.AsNoTracking()
            .Where(i => i.InspectorId == userId ||
                        assignments.Any(a => a.ZoneId != null && i.Zone.Path.IsDescendantOf(a.Zone!.Path)))
            .Select(i => new { i.Status, i.Outcome, i.Type, i.CreatedAt, i.VisitedAt, i.ScheduledFor, i.InspectorId, Zone = i.Zone.NameBn })
            .ToListAsync();

        var done = rows.Where(r => r.Status == InspectionStatus.Completed && r.VisitedAt != null).ToList();
        var now = DateTime.UtcNow;
        bool Open(InspectionStatus s) => s is InspectionStatus.Scheduled or InspectionStatus.InProgress;
        var kpis = new Dictionary<string, Metric>
        {
            ["requested"] = Stats.Count(rows, r => r.CreatedAt, p),
            ["completed"] = Stats.Count(done, r => r.VisitedAt, p),
            ["mine_completed"] = Stats.Count(done.Where(r => r.InspectorId == userId).ToList(), r => r.VisitedAt, p),
            ["pass_rate"] = Stats.Ratio(c =>
            {
                var set = done.Where(r => c ? p.InCurrent(r.VisitedAt!.Value) : p.InPrevious(r.VisitedAt!.Value)).ToList();
                return (set.Count(r => r.Outcome == InspectionOutcome.Passed), set.Count);
            }),
            ["turnaround_hours"] = new(
                Stats.Avg(done.Where(r => p.InCurrent(r.VisitedAt!.Value)).Select(r => (r.VisitedAt!.Value - r.CreatedAt).TotalHours)) ?? 0,
                Stats.Avg(done.Where(r => p.InPrevious(r.VisitedAt!.Value)).Select(r => (r.VisitedAt!.Value - r.CreatedAt).TotalHours))),
            ["open_now"] = new(rows.Count(r => Open(r.Status)), null),
            ["unassigned_now"] = new(rows.Count(r => Open(r.Status) && r.InspectorId == null), null),
            ["overdue_now"] = new(rows.Count(r => Open(r.Status) && r.ScheduledFor is { } s && s < now), null),
        };

        var trendRows = rows.Select(r => (When: (DateTime?)r.CreatedAt, V: new double[] { 1, 0 }))
            .Concat(done.Select(r => (When: r.VisitedAt, V: new double[] { 0, 1 })));
        var trend = p.Daily(["requested", "completed"], trendRows, x => x.When, x => x.V);

        var outcomeLabel = new Dictionary<InspectionOutcome, string>
        {
            [InspectionOutcome.Passed] = "উত্তীর্ণ", [InspectionOutcome.Failed] = "অনুত্তীর্ণ",
            [InspectionOutcome.NeedsFollowup] = "পুনরায় দেখা দরকার", [InspectionOutcome.BusinessNotFound] = "ব্যবসা পাওয়া যায়নি",
        };
        var outcomes = done.Where(r => p.InCurrent(r.VisitedAt!.Value) && r.Outcome != null).GroupBy(r => r.Outcome!.Value)
            .Select(g => new Slice(SnakeCase.FromEnum(g.Key), outcomeLabel[g.Key], g.Count())).OrderByDescending(s => s.Value).ToList();

        var byZone = rows.Where(r => p.InCurrent(r.CreatedAt)).GroupBy(r => r.Zone)
            .Select(g => new Slice(g.Key, g.Key, g.Count(), g.Count(r => r.Status == InspectionStatus.Completed)))
            .OrderByDescending(s => s.Value).Take(10).ToList();

        var typeLabel = new Dictionary<InspectionType, string>
        {
            [InspectionType.VerificationVisit] = "যাচাইকরণ পরিদর্শন", [InspectionType.ComplaintFollowup] = "অভিযোগ তদন্ত", [InspectionType.RoutineAudit] = "নিয়মিত অডিট",
        };
        var byType = rows.Where(r => p.InCurrent(r.CreatedAt)).GroupBy(r => r.Type)
            .Select(g => new Slice(SnakeCase.FromEnum(g.Key), typeLabel[g.Key], g.Count())).OrderByDescending(s => s.Value).ToList();

        return new InspectionAnalyticsDto(p.Days, p.From, p.To, kpis, trend, outcomes, byZone, byType);
    }

    // ---------- Administration (platform-wide) ----------

    public record AdminAnalyticsDto(int Days, DateOnly From, DateOnly To, Dictionary<string, Metric> Kpis,
        TimeSeries Growth, TimeSeries Bookings, List<Slice> Funnel, List<Slice> ByCategory, List<Slice> ByDivision,
        List<Slice> StatusMix, List<Slice> TopBusinesses, List<Slice> Roles);

    /// <summary>Platform growth, marketplace activity and trust health. Global (unscoped) admins only.</summary>
    [HttpGet("api/admin/analytics")]
    public async Task<ActionResult<AdminAnalyticsDto>> Admin([FromQuery] int days = 30)
    {
        await access.RequireGlobalAsync(P.PlatformAnalyticsView);
        var p = Period.Last(days);

        var users = await db.Users.AsNoTracking().Select(u => new { u.CreatedAt, u.LastLoginAt }).ToListAsync();
        var businesses = await db.Businesses.AsNoTracking()
            .Select(b => new
            {
                b.Id, b.Name, b.CreatedAt, b.PublishedAt, b.Status,
                TopCategoryId = b.Category.ParentId ?? b.CategoryId,
                b.ZoneId,
            })
            .ToListAsync();
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.CreatedAt >= p.PrevStartUtc && b.CreatedAt < p.EndUtc)
            .Select(b => new { b.BusinessId, b.CreatedAt, b.Status, Price = b.FinalPrice ?? b.QuotedPrice ?? 0 })
            .ToListAsync();
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.Status == ReviewStatus.Published)
            .Select(r => new { r.Rating, r.CreatedAt }).ToListAsync();

        var cur = bookings.Where(b => p.InCurrent(b.CreatedAt)).ToList();
        var prev = bookings.Where(b => p.InPrevious(b.CreatedAt)).ToList();
        double Gmv<T>(IEnumerable<T> xs, Func<T, BookingStatus> s, Func<T, decimal> price) => (double)xs.Where(x => s(x) == BookingStatus.Completed).Sum(price);

        var kpis = new Dictionary<string, Metric>
        {
            ["new_users"] = Stats.Count(users, u => u.CreatedAt, p),
            ["active_users"] = Stats.Count(users, u => u.LastLoginAt, p),
            ["total_users"] = new(users.Count, null),
            ["new_businesses"] = Stats.Count(businesses, b => b.CreatedAt, p),
            ["published_businesses"] = Stats.Count(businesses, b => b.PublishedAt, p),
            ["live_businesses"] = new(businesses.Count(b => b.Status == BusinessStatus.Published), null),
            ["bookings"] = new(cur.Count, prev.Count),
            ["completed"] = new(cur.Count(b => b.Status == BookingStatus.Completed), prev.Count(b => b.Status == BookingStatus.Completed)),
            ["gmv"] = new(Gmv(cur, b => b.Status, b => b.Price), Gmv(prev, b => b.Status, b => b.Price)),
            ["completion_rate"] = Stats.Ratio(c => { var s = c ? cur : prev; return (s.Count(b => b.Status == BookingStatus.Completed), s.Count(b => b.Status == BookingStatus.Completed || Stats.IsLost(b.Status))); }),
            ["reviews"] = Stats.Count(reviews, r => r.CreatedAt, p),
            ["avg_rating"] = new(
                Stats.Avg(reviews.Where(r => p.InCurrent(r.CreatedAt)).Select(r => (double)r.Rating)) ?? 0,
                Stats.Avg(reviews.Where(r => p.InPrevious(r.CreatedAt)).Select(r => (double)r.Rating))),
            ["pending_verifications"] = new(await db.VerificationRequests.CountAsync(v =>
                v.Status == VerificationStatus.Submitted || v.Status == VerificationStatus.UnderReview || v.Status == VerificationStatus.InfoRequested), null),
            ["open_reports"] = new(await db.Reports.CountAsync(r =>
                r.Status == ReportStatus.Open || r.Status == ReportStatus.UnderReview || r.Status == ReportStatus.Escalated), null),
        };

        var growthRows = users.Select(u => (When: (DateTime?)u.CreatedAt, V: new double[] { 1, 0 }))
            .Concat(businesses.Select(b => (When: (DateTime?)b.CreatedAt, V: new double[] { 0, 1 })));
        var growth = p.Daily(["signups", "new_businesses"], growthRows, x => x.When, x => x.V);
        var bookingTrend = p.Daily(["bookings", "completed", "gmv"], cur, b => b.CreatedAt, b =>
            [1, b.Status == BookingStatus.Completed ? 1 : 0, b.Status == BookingStatus.Completed ? (double)b.Price : 0]);

        // All-time onboarding funnel: each step is a subset of the one before it.
        var submittedIds = (await db.VerificationRequests.AsNoTracking().Where(v => v.Status != VerificationStatus.Draft)
            .Select(v => v.BusinessId).Distinct().ToListAsync()).ToHashSet();
        var approvedIds = (await db.VerificationRequests.AsNoTracking().Where(v => v.Status == VerificationStatus.Approved)
            .Select(v => v.BusinessId).Distinct().ToListAsync()).ToHashSet();
        var bookedIds = (await db.Bookings.AsNoTracking().Select(b => b.BusinessId).Distinct().ToListAsync()).ToHashSet();
        var funnel = new List<Slice>
        {
            new("created", "পেজ তৈরি", businesses.Count),
            new("submitted", "যাচাইয়ে জমা", businesses.Count(b => submittedIds.Contains(b.Id))),
            new("approved", "অনুমোদিত", businesses.Count(b => approvedIds.Contains(b.Id))),
            new("published", "প্রকাশিত", businesses.Count(b => b.PublishedAt != null)),
            new("booked", "প্রথম বুকিং পেয়েছে", businesses.Count(b => b.PublishedAt != null && bookedIds.Contains(b.Id))),
        };

        var catNames = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.NameBn);
        var bookingsByBiz = cur.GroupBy(b => b.BusinessId).ToDictionary(g => g.Key, g => g.Count());
        var byCategory = businesses.Where(b => b.Status == BusinessStatus.Published).GroupBy(b => b.TopCategoryId)
            .Select(g => new Slice(g.Key.ToString(), catNames.GetValueOrDefault(g.Key, "—"), g.Count(),
                g.Sum(b => bookingsByBiz.GetValueOrDefault(b.Id))))
            .OrderByDescending(s => s.Value).ToList();

        // Division = first id of the zone's materialized path ("1.102.10202" -> 1).
        var zones = await db.GeoZones.AsNoTracking().Select(z => new { z.Id, z.ParentId, z.NameBn, z.Path }).ToListAsync();
        var divisionOf = zones.ToDictionary(z => z.Id, z => z.Path.ToString().Split('.')[0]);
        var divisionNames = zones.Where(z => z.ParentId == null).ToDictionary(z => z.Id.ToString(), z => z.NameBn);
        var byDivision = businesses.Where(b => b.ZoneId != null && divisionOf.ContainsKey(b.ZoneId.Value))
            .GroupBy(b => divisionOf[b.ZoneId!.Value])
            .Select(g => new Slice(g.Key, divisionNames.GetValueOrDefault(g.Key, "—"), g.Count(),
                g.Count(b => b.Status == BusinessStatus.Published)))
            .OrderByDescending(s => s.Value).ToList();

        var names = businesses.ToDictionary(b => b.Id, b => b.Name);
        var topBusinesses = cur.GroupBy(b => b.BusinessId)
            .Select(g => new Slice(g.Key.ToString(), names.GetValueOrDefault(g.Key, "—"), g.Count(), Gmv(g, x => x.Status, x => x.Price)))
            .OrderByDescending(s => s.Value).Take(8).ToList();

        var now = DateTime.UtcNow;
        var roles = await db.PlatformRoleAssignments.AsNoTracking()
            .Where(a => a.RevokedAt == null && a.ValidFrom <= now && (a.ValidUntil == null || a.ValidUntil > now))
            .GroupBy(a => new { a.Role.Key, a.Role.NameBn })
            .Select(g => new { g.Key.Key, g.Key.NameBn, Count = g.Count() })
            .ToListAsync();

        return new AdminAnalyticsDto(p.Days, p.From, p.To, kpis, growth, bookingTrend, funnel, byCategory, byDivision,
            Stats.StatusMix(cur.Select(b => b.Status)), topBusinesses,
            roles.OrderByDescending(r => r.Count).Select(r => new Slice(r.Key, r.NameBn, r.Count)).ToList());
    }

    // ---------- Customer ----------

    public record CustomerStatsDto(int TotalBookings, int Completed, int Upcoming, int Cancelled, double TotalSpent,
        int ReviewsGiven, int BusinessesUsed, string? TopCategory);

    /// <summary>A customer's own lifetime numbers for the "my bookings" header.</summary>
    [HttpGet("api/me/stats")]
    public async Task<ActionResult<CustomerStatsDto>> MyStats()
    {
        var userId = me.RequireId();
        var now = DateTime.UtcNow;
        var rows = await db.Bookings.AsNoTracking().Where(b => b.CustomerUserId == userId)
            .Select(b => new { b.Status, b.StartAt, b.BusinessId, Category = b.Service.Category.NameBn, Price = b.FinalPrice ?? b.QuotedPrice ?? 0 })
            .ToListAsync();
        return new CustomerStatsDto(
            rows.Count,
            rows.Count(r => r.Status == BookingStatus.Completed),
            rows.Count(r => r.StartAt >= now && r.Status is BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Rescheduled),
            rows.Count(r => r.Status is BookingStatus.Cancelled or BookingStatus.Rejected),
            (double)rows.Where(r => r.Status == BookingStatus.Completed).Sum(r => r.Price),
            await db.Reviews.CountAsync(r => r.CustomerId == userId),
            rows.Select(r => r.BusinessId).Distinct().Count(),
            rows.GroupBy(r => r.Category).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault());
    }
}
