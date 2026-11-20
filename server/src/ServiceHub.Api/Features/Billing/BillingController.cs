using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;

namespace ServiceHub.Api.Features.Billing;

public record PlanDto(string Key, string NameBn, string NameEn, string? TaglineBn, decimal PriceMonthly, decimal PriceYearly,
    Dictionary<string, int> Entitlements);

public record PaymentAccountDto(string Method, string Label, string Number, string? AccountName, string? Instructions);

public record PlansResponse(List<PlanDto> Plans, List<FeatureDef> Features, int TrialDays, int GraceDays, List<PaymentAccountDto> PaymentAccounts);

public record UsageDto(int Services, int Members);

public record BusinessPlanDto(string PlanKey, string PlanName, SubscriptionStatus? Status, BillingCycle? Cycle, DateTime? TrialEndsAt,
    DateTime? CurrentPeriodEnd, DateTime? GraceEndsAt, bool CancelAtPeriodEnd, bool IsPaid, bool IsTrial, bool TrialUsed,
    Dictionary<string, int> Limits, UsageDto Usage);

public record PaymentDto(Guid Id, SubscriptionPaymentMethod Method, decimal Amount, string? TransactionId, string? SenderNumber,
    SubscriptionPaymentStatus Status, DateTime CreatedAt, DateTime? ReviewedAt, string? ReviewNote);

public record InvoiceDto(Guid Id, string Number, string PlanKey, string PlanName, BillingCycle Cycle, decimal Subtotal, decimal Credit,
    decimal Total, InvoiceStatus Status, DateTime? PeriodStart, DateTime? PeriodEnd, DateTime CreatedAt, DateTime? PaidAt, List<PaymentDto> Payments);

public record CheckoutRequest([Required] string PlanKey, BillingCycle Cycle = BillingCycle.Monthly);

public record SubmitPaymentRequest(SubscriptionPaymentMethod Method, [Required, StringLength(40, MinimumLength = 6)] string TransactionId,
    [StringLength(40)] string? SenderNumber);

public record ReviewPaymentRequest([StringLength(500)] string? Note);

public record RejectPaymentRequest([Required, StringLength(500, MinimumLength = 5)] string Reason);

public record GrantRequest([Required] string PlanKey, BillingCycle Cycle, SubscriptionPaymentMethod Method, [Range(0, 1_000_000)] decimal? Amount,
    [Required, StringLength(500, MinimumLength = 5)] string Reason);

public record UpdatePlanPriceRequest([Range(0, 1_000_000)] decimal PriceMonthly, [Range(0, 10_000_000)] decimal PriceYearly);

public record AdminPaymentDto(Guid Id, Guid BusinessId, string BusinessName, string InvoiceNumber, string PlanName, BillingCycle Cycle,
    decimal InvoiceTotal, SubscriptionPaymentMethod Method, decimal Amount, string? TransactionId, string? SenderNumber,
    SubscriptionPaymentStatus Status, string SubmittedByName, DateTime CreatedAt, DateTime? ReviewedAt, string? ReviewNote);

public record AdminSubscriptionDto(Guid BusinessId, string BusinessName, string Slug, BusinessStatus BusinessStatus, string OwnerName,
    string PlanKey, string PlanName, SubscriptionStatus? Status, BillingCycle? Cycle, DateTime? TrialEndsAt, DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public record BillingSummaryDto(decimal Mrr, int ActivePaid, int Trialing, int PastDue, int PendingPayments, decimal Revenue30d,
    Dictionary<string, int> PaidByPlan);

[ApiController]
public class BillingController(
    AppDbContext db, ICurrentUser me, AccessService access, EntitlementService entitlements, SubscriptionService subscriptions,
    IOptions<BillingOptions> options, AuditLogger audit) : ControllerBase
{
    // ---------- Public ----------

    /// <summary>Plans, the comparison rows and where to pay — for the pricing page and the billing page.</summary>
    [HttpGet("api/plans")]
    public async Task<ActionResult<PlansResponse>> Plans()
    {
        var plans = await db.SubscriptionPlans.AsNoTracking().Include(p => p.Entitlements).Where(p => p.IsPublic)
            .OrderBy(p => p.SortOrder).ToListAsync();
        var o = options.Value;
        return new PlansResponse(
            plans.Select(p => new PlanDto(p.Key, p.NameBn, p.NameEn, p.TaglineBn, p.PriceMonthly, p.PriceYearly,
                p.Entitlements.ToDictionary(e => e.Key, e => e.Value))).ToList(),
            PlanCatalog.Features.ToList(), o.TrialDays, o.GraceDays,
            o.PaymentAccounts.Select(a => new PaymentAccountDto(a.Method, a.Label, a.Number, a.AccountName, a.Instructions)).ToList());
    }

    // ---------- Business workspace ----------

    /// <summary>Effective plan, limits and usage — any team member may see it (pages use it to show what's locked).</summary>
    [Authorize, HttpGet("api/businesses/{businessId:guid}/plan")]
    public async Task<ActionResult<BusinessPlanDto>> BusinessPlan(Guid businessId)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.BusinessView);
        var plan = await entitlements.GetAsync(businessId);
        var services = await db.Services.CountAsync(s => s.BusinessId == businessId);
        var members = await entitlements.TeamSizeAsync(businessId);
        var trialUsed = await db.SubscriptionEvents.AnyAsync(e => e.BusinessId == businessId && e.Type == "trial.started");
        return new BusinessPlanDto(plan.Key, plan.NameBn, plan.Status, plan.Cycle, plan.TrialEndsAt, plan.PeriodEnd, plan.GraceEndsAt,
            plan.CancelAtPeriodEnd, plan.IsPaid, plan.IsTrial, trialUsed, plan.Limits.ToDictionary(), new UsageDto(services, members));
    }

    [Authorize, HttpGet("api/businesses/{businessId:guid}/billing/invoices")]
    public async Task<ActionResult<PagedResult<InvoiceDto>>> Invoices(Guid businessId, [FromQuery] PageQuery paging)
    {
        await access.RequireBusinessAsync(businessId, P.BillingManage);
        var query = db.SubscriptionInvoices.Where(i => i.BusinessId == businessId && i.Status != InvoiceStatus.Void);
        var total = await query.CountAsync();
        var items = await InvoiceQuery(query).Skip(paging.Skip).Take(paging.SafeSize).ToListAsync();
        return new PagedResult<InvoiceDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    /// <summary>Starts buying/renewing/switching a plan: creates the invoice to pay.</summary>
    [Authorize, HttpPost("api/businesses/{businessId:guid}/billing/checkout")]
    public async Task<ActionResult<InvoiceDto>> Checkout(Guid businessId, CheckoutRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.BillingManage);
        var invoice = await subscriptions.CheckoutAsync(businessId, req.PlanKey, req.Cycle, me.RequireId());
        return await InvoiceQuery(db.SubscriptionInvoices.Where(i => i.Id == invoice.Id)).FirstAsync();
    }

    /// <summary>"I've paid" — the TrxID from bKash/Nagad or the bank reference, for staff to verify.</summary>
    [Authorize, HttpPost("api/businesses/{businessId:guid}/billing/invoices/{invoiceId:guid}/payments")]
    public async Task<ActionResult<InvoiceDto>> SubmitPayment(Guid businessId, Guid invoiceId, SubmitPaymentRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.BillingManage);
        await subscriptions.SubmitPaymentAsync(businessId, invoiceId, req.Method, req.TransactionId, req.SenderNumber, me.RequireId());
        return await InvoiceQuery(db.SubscriptionInvoices.Where(i => i.Id == invoiceId && i.BusinessId == businessId)).FirstAsync();
    }

    [Authorize, HttpPost("api/businesses/{businessId:guid}/billing/invoices/{invoiceId:guid}/void")]
    public async Task<IActionResult> VoidInvoice(Guid businessId, Guid invoiceId)
    {
        await access.RequireBusinessAsync(businessId, P.BillingManage);
        await subscriptions.VoidInvoiceAsync(businessId, invoiceId);
        return NoContent();
    }

    [Authorize, HttpPost("api/businesses/{businessId:guid}/billing/cancel")]
    public async Task<IActionResult> Cancel(Guid businessId)
    {
        await access.RequireBusinessAsync(businessId, P.BillingManage);
        await subscriptions.CancelAsync(businessId, me.RequireId());
        return NoContent();
    }

    [Authorize, HttpPost("api/businesses/{businessId:guid}/billing/resume")]
    public async Task<IActionResult> Resume(Guid businessId)
    {
        await access.RequireBusinessAsync(businessId, P.BillingManage);
        await subscriptions.ResumeAsync(businessId, me.RequireId());
        return NoContent();
    }

    private static IQueryable<InvoiceDto> InvoiceQuery(IQueryable<Domain.Entities.SubscriptionInvoice> source) =>
        source.AsNoTracking().OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvoiceDto(i.Id, i.Number, i.Plan.Key, i.Plan.NameBn, i.Cycle, i.Subtotal, i.Credit, i.Total, i.Status,
                i.PeriodStart, i.PeriodEnd, i.CreatedAt, i.PaidAt,
                i.Payments.OrderByDescending(p => p.CreatedAt).Select(p => new PaymentDto(p.Id, p.Method, p.Amount, p.TransactionId,
                    p.SenderNumber, p.Status, p.CreatedAt, p.ReviewedAt, p.ReviewNote)).ToList()));

    // ---------- Platform staff ----------

    [Authorize, HttpGet("api/admin/billing/summary")]
    public async Task<ActionResult<BillingSummaryDto>> Summary()
    {
        await access.RequireGlobalAsync(P.SubscriptionsManage);
        var now = DateTime.UtcNow;
        var paid = await db.BusinessSubscriptions.AsNoTracking()
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue) && s.Plan.Key != PlanCatalog.Free)
            .Select(s => new { s.Status, s.Cycle, s.Plan.Key, s.Plan.PriceMonthly, s.Plan.PriceYearly }).ToListAsync();
        var since = now.AddDays(-30);
        return new BillingSummaryDto(
            paid.Where(p => p.Status == SubscriptionStatus.Active).Sum(p => p.Cycle == BillingCycle.Yearly ? Math.Round(p.PriceYearly / 12) : p.PriceMonthly),
            paid.Count(p => p.Status == SubscriptionStatus.Active),
            await db.BusinessSubscriptions.CountAsync(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt > now),
            paid.Count(p => p.Status == SubscriptionStatus.PastDue),
            await db.SubscriptionPayments.CountAsync(p => p.Status == SubscriptionPaymentStatus.Pending),
            await db.SubscriptionPayments.Where(p => p.Status == SubscriptionPaymentStatus.Verified && p.ReviewedAt >= since).SumAsync(p => p.Amount),
            paid.Where(p => p.Status == SubscriptionStatus.Active).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Count()));
    }

    [Authorize, HttpGet("api/admin/billing/payments")]
    public async Task<ActionResult<PagedResult<AdminPaymentDto>>> Payments([FromQuery] SubscriptionPaymentStatus? status, [FromQuery] PageQuery paging)
    {
        await access.RequireGlobalAsync(P.SubscriptionsManage);
        var q = db.SubscriptionPayments.AsNoTracking().AsQueryable();
        if (status is not null) q = q.Where(p => p.Status == status);
        var total = await q.CountAsync();
        var items = await q.OrderBy(p => p.Status == SubscriptionPaymentStatus.Pending ? 0 : 1).ThenByDescending(p => p.CreatedAt)
            .Skip(paging.Skip).Take(paging.SafeSize)
            .Select(p => new AdminPaymentDto(p.Id, p.Invoice.BusinessId, p.Invoice.Business.Name, p.Invoice.Number, p.Invoice.Plan.NameBn,
                p.Invoice.Cycle, p.Invoice.Total, p.Method, p.Amount, p.TransactionId, p.SenderNumber, p.Status,
                db.Users.IgnoreQueryFilters().Where(u => u.Id == p.SubmittedBy).Select(u => u.FullName).FirstOrDefault() ?? "—",
                p.CreatedAt, p.ReviewedAt, p.ReviewNote))
            .ToListAsync();
        return new PagedResult<AdminPaymentDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    [Authorize, HttpPost("api/admin/billing/payments/{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, ReviewPaymentRequest req)
    {
        await access.RequireGlobalAsync(P.SubscriptionsManage);
        await subscriptions.VerifyAsync(id, me.RequireId(), req.Note);
        return NoContent();
    }

    [Authorize, HttpPost("api/admin/billing/payments/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectPaymentRequest req)
    {
        await access.RequireGlobalAsync(P.SubscriptionsManage);
        await subscriptions.RejectAsync(id, me.RequireId(), req.Reason);
        return NoContent();
    }

    /// <summary>Every business with its effective plan — including the many on Free with no subscription row.</summary>
    [Authorize, HttpGet("api/admin/billing/subscriptions")]
    public async Task<ActionResult<PagedResult<AdminSubscriptionDto>>> Subscriptions([FromQuery] string? q, [FromQuery] string? plan, [FromQuery] PageQuery paging)
    {
        await access.RequireGlobalAsync(P.SubscriptionsManage);
        var now = DateTime.UtcNow;
        var grace = TimeSpan.FromDays(options.Value.GraceDays);
        var rows = db.Businesses.AsNoTracking()
            .Select(b => new { B = b, S = db.BusinessSubscriptions.Where(s => s.BusinessId == b.Id).Select(s => new { s, s.Plan.Key, s.Plan.NameBn }).FirstOrDefault() });
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(x => EF.Functions.ILike(x.B.Name, $"%{q.Trim()}%"));
        var list = await rows.OrderByDescending(x => x.B.CreatedAt).ToListAsync();
        var dtos = list.Select(x =>
        {
            var s = x.S?.s;
            var live = s is not null && (s.Status == SubscriptionStatus.Trialing ? s.TrialEndsAt > now
                : s.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue && s.CurrentPeriodEnd + grace > now);
            return new AdminSubscriptionDto(x.B.Id, x.B.Name, x.B.Slug, x.B.Status, "",
                live ? x.S!.Key : PlanCatalog.Free, live ? x.S!.NameBn : "ফ্রি", s?.Status, s?.Cycle, s?.TrialEndsAt, s?.CurrentPeriodEnd,
                s?.CancelAtPeriodEnd ?? false);
        });
        if (!string.IsNullOrWhiteSpace(plan)) dtos = dtos.Where(d => d.PlanKey == plan);
        var filtered = dtos.ToList();
        var ownerIds = list.Select(x => x.B.OwnerId).Distinct().ToList();
        var owners = await db.Users.IgnoreQueryFilters().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var ownerOf = list.ToDictionary(x => x.B.Id, x => owners.GetValueOrDefault(x.B.OwnerId, "—"));
        var page = filtered.Skip(paging.Skip).Take(paging.SafeSize).Select(d => d with { OwnerName = ownerOf[d.BusinessId] }).ToList();
        return new PagedResult<AdminSubscriptionDto>(page, paging.SafePage, paging.SafeSize, filtered.Count);
    }

    /// <summary>Record a cash payment or give a plan for free. Always audited with a reason.</summary>
    [Authorize, HttpPost("api/admin/billing/businesses/{businessId:guid}/grant")]
    public async Task<IActionResult> Grant(Guid businessId, GrantRequest req)
    {
        await access.RequireGlobalAsync(P.SubscriptionsManage);
        await subscriptions.GrantAsync(businessId, req.PlanKey, req.Cycle, req.Method, req.Amount, req.Reason, me.RequireId());
        return NoContent();
    }

    [Authorize, HttpPut("api/admin/billing/plans/{key}")]
    public async Task<IActionResult> UpdatePrice(string key, UpdatePlanPriceRequest req)
    {
        await access.RequireGlobalAsync(P.SubscriptionsManage);
        var p = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.Key == key) ?? throw new NotFoundException("প্ল্যানটি পাওয়া যায়নি।");
        if (p.Key == PlanCatalog.Free) throw new BadRequestException("ফ্রি প্ল্যানের মূল্য বদলানো যায় না।", "invalid_plan");
        audit.Log("billing.plan_price", "subscription_plan", p.Key, scope: RoleScope.Platform, changes: new()
        {
            ["price_monthly"] = $"{p.PriceMonthly:0} → {req.PriceMonthly:0}", ["price_yearly"] = $"{p.PriceYearly:0} → {req.PriceYearly:0}",
        });
        p.PriceMonthly = req.PriceMonthly;
        p.PriceYearly = req.PriceYearly;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
