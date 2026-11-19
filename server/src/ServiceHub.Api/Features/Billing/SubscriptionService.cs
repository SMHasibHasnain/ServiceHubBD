using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Billing;

/// <summary>
/// Subscription lifecycle: trial → checkout (invoice) → manual payment claim → staff verification → active period,
/// plus cancellation, grace and expiry. Online gateways can later plug in as another payment method.
/// </summary>
public class SubscriptionService(AppDbContext db, IOptions<BillingOptions> options, Notifier notifier, AuditLogger audit,
    EntitlementService entitlements, ILogger<SubscriptionService> logger)
{
    private BillingOptions O => options.Value;

    public static DateTime AddCycle(DateTime start, BillingCycle cycle) =>
        cycle == BillingCycle.Yearly ? start.AddYears(1) : start.AddMonths(1);

    public static decimal Price(SubscriptionPlan plan, BillingCycle cycle) => cycle == BillingCycle.Yearly ? plan.PriceYearly : plan.PriceMonthly;

    private async Task<SubscriptionPlan> PlanAsync(string key) =>
        await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Key == key)
        ?? throw new BadRequestException("প্ল্যানটি পাওয়া যায়নি।", "invalid_plan");

    private void Event(Guid businessId, string type, string? from = null, string? to = null, Guid? actor = null, Dictionary<string, string?>? data = null) =>
        db.SubscriptionEvents.Add(new SubscriptionEvent { BusinessId = businessId, Type = type, FromPlan = from, ToPlan = to, ActorId = actor, Data = data });

    private Task NotifyOwnersAsync(Guid businessId, string type, string title, string? body = null) =>
        notifier.NotifyBusinessAsync(businessId, P.BillingManage, type, title, body, $"/w/{businessId}/billing");

    // ---------- Trial ----------

    /// <summary>Starts the free trial when a business is first published. Idempotent: a business gets one trial, ever.</summary>
    public async Task StartTrialAsync(Business business)
    {
        if (O.TrialDays <= 0 || await db.BusinessSubscriptions.AnyAsync(s => s.BusinessId == business.Id)) return;
        var plan = await PlanAsync(PlanCatalog.TrialPlan);
        db.BusinessSubscriptions.Add(new BusinessSubscription
        {
            BusinessId = business.Id, PlanId = plan.Id, Status = SubscriptionStatus.Trialing,
            TrialEndsAt = DateTime.UtcNow.AddDays(O.TrialDays),
        });
        Event(business.Id, "trial.started", PlanCatalog.Free, plan.Key, data: new() { ["days"] = O.TrialDays.ToString() });
        notifier.Notify(business.OwnerId, WorkspaceType.Business, business.Id, "billing.trial_started",
            $"{EntitlementService.Bn(O.TrialDays)} দিনের {plan.NameBn} ট্রায়াল চালু হয়েছে",
            "সব প্রো সুবিধা বিনামূল্যে ব্যবহার করুন। ট্রায়াল শেষে চাইলে প্ল্যান বেছে নিতে পারবেন।", $"/w/{business.Id}/billing");
        entitlements.Forget(business.Id);
    }

    // ---------- Checkout ----------

    public async Task<SubscriptionInvoice> CheckoutAsync(Guid businessId, string planKey, BillingCycle cycle, Guid actorId)
    {
        var plan = await PlanAsync(planKey);
        if (plan.Key == PlanCatalog.Free || !plan.IsPublic)
            throw new BadRequestException("এই প্ল্যান কেনা যায় না।", "invalid_plan");
        if (await db.SubscriptionInvoices.AnyAsync(i => i.BusinessId == businessId && i.Status == InvoiceStatus.PendingVerification))
            throw new ConflictException("আপনার একটি পেমেন্ট যাচাইয়ের অপেক্ষায় আছে। যাচাই শেষ হলে আবার চেষ্টা করুন।", "payment_pending");

        // Only one open invoice at a time — a new checkout replaces the old one.
        await db.SubscriptionInvoices.Where(i => i.BusinessId == businessId && i.Status == InvoiceStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Void));

        var subtotal = Price(plan, cycle);
        var credit = await UnusedCreditAsync(businessId, plan.Id);
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('invoice_number_seq') AS \"Value\"").SingleAsync();
        var invoice = new SubscriptionInvoice
        {
            Number = $"INV-{BdTime.Today():yy}-{seq:D6}", BusinessId = businessId, PlanId = plan.Id, Plan = plan, Cycle = cycle,
            Subtotal = subtotal, Credit = Math.Min(credit, subtotal), Total = Math.Max(0, subtotal - credit), CreatedBy = actorId,
        };
        db.SubscriptionInvoices.Add(invoice);
        Event(businessId, "checkout.created", data: new() { ["invoice"] = invoice.Number, ["plan"] = plan.Key, ["cycle"] = SnakeCase.FromEnum(cycle) }, actor: actorId);
        await db.SaveChangesAsync();

        if (invoice.Total == 0)
        {
            // Fully covered by credit — nothing to pay, switch right away.
            db.SubscriptionPayments.Add(new SubscriptionPayment
            {
                InvoiceId = invoice.Id, Method = SubscriptionPaymentMethod.Complimentary, Amount = 0, Status = SubscriptionPaymentStatus.Verified,
                SubmittedBy = actorId, ReviewedAt = DateTime.UtcNow, Note = "আগের প্ল্যানের অব্যবহৃত মূল্য থেকে সমন্বয়",
            });
            await ApplyAsync(invoice, actorId);
            await db.SaveChangesAsync();
        }
        return invoice;
    }

    /// <summary>Value of the unused part of a paid period on a different plan, credited towards the new one.</summary>
    private async Task<decimal> UnusedCreditAsync(Guid businessId, short newPlanId)
    {
        var sub = await db.BusinessSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.BusinessId == businessId);
        var now = DateTime.UtcNow;
        if (sub is null || sub.Status != SubscriptionStatus.Active || sub.PlanId == newPlanId ||
            sub.CurrentPeriodStart is not { } start || sub.CurrentPeriodEnd is not { } end || end <= now) return 0;
        var paid = await db.SubscriptionInvoices.Where(i => i.BusinessId == businessId && i.Status == InvoiceStatus.Paid && i.PlanId == sub.PlanId && i.PeriodEnd == end)
            .Select(i => (decimal?)i.Total).FirstOrDefaultAsync() ?? Price(sub.Plan, sub.Cycle);
        var fraction = (decimal)((end - now).TotalSeconds / (end - start).TotalSeconds);
        return Math.Floor(paid * fraction);
    }

    // ---------- Manual payment claims ----------

    public async Task<SubscriptionPayment> SubmitPaymentAsync(Guid businessId, Guid invoiceId, SubscriptionPaymentMethod method,
        string transactionId, string? senderNumber, Guid actorId)
    {
        if (method is not (SubscriptionPaymentMethod.Bkash or SubscriptionPaymentMethod.Nagad or SubscriptionPaymentMethod.BankTransfer))
            throw new BadRequestException("বিকাশ, নগদ অথবা ব্যাংক ট্রান্সফার বেছে নিন।", "invalid_method");
        var invoice = await db.SubscriptionInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.BusinessId == businessId)
            ?? throw new NotFoundException("ইনভয়েসটি পাওয়া যায়নি।", "invoice_not_found");
        if (invoice.Status != InvoiceStatus.Open)
            throw new ConflictException("এই ইনভয়েসে এখন পেমেন্ট জমা দেওয়া যাবে না।", "invoice_not_open");

        var trx = transactionId.Trim().ToUpperInvariant();
        if (trx.Length < 6) throw new BadRequestException("সঠিক লেনদেন নম্বর (TrxID) লিখুন।", "invalid_transaction");
        if (await db.SubscriptionPayments.AnyAsync(p => p.Method == method && p.TransactionId == trx && p.Status != SubscriptionPaymentStatus.Rejected))
            throw new ConflictException("এই লেনদেন নম্বর আগেই জমা দেওয়া হয়েছে।", "duplicate_transaction");
        string? sender = null;
        if (!string.IsNullOrWhiteSpace(senderNumber))
            sender = method == SubscriptionPaymentMethod.BankTransfer ? senderNumber.Trim()
                : Phone.NormalizeBd(senderNumber) ?? throw new BadRequestException("যে নম্বর থেকে টাকা পাঠিয়েছেন সেটি সঠিকভাবে লিখুন।", "invalid_phone");

        var payment = new SubscriptionPayment
        {
            InvoiceId = invoice.Id, Method = method, Amount = invoice.Total, TransactionId = trx, SenderNumber = sender, SubmittedBy = actorId,
        };
        db.SubscriptionPayments.Add(payment);
        invoice.Status = InvoiceStatus.PendingVerification;
        Event(businessId, "payment.submitted", actor: actorId, data: new() { ["invoice"] = invoice.Number, ["method"] = SnakeCase.FromEnum(method), ["trx"] = trx });

        var staff = await db.PlatformRoleAssignments
            .Where(a => a.RevokedAt == null && (a.ValidUntil == null || a.ValidUntil > DateTime.UtcNow) && a.ZoneId == null &&
                        a.Role.RolePermissions.Any(rp => rp.Permission.Key == P.SubscriptionsManage))
            .Select(a => a.UserId).Distinct().ToListAsync();
        var bizName = await db.Businesses.Where(b => b.Id == businessId).Select(b => b.Name).FirstAsync();
        foreach (var id in staff)
            notifier.Notify(id, WorkspaceType.Administration, null, "billing.payment_submitted",
                $"নতুন পেমেন্ট যাচাই করুন: {bizName}", $"{invoice.Number} · ৳{EntitlementService.Bn((long)invoice.Total)}", "/admin/billing");

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { throw new ConflictException("এই লেনদেন নম্বর আগেই জমা দেওয়া হয়েছে।", "duplicate_transaction"); }
        return payment;
    }

    public async Task VerifyAsync(Guid paymentId, Guid actorId, string? note)
    {
        var payment = await db.SubscriptionPayments.Include(p => p.Invoice).ThenInclude(i => i.Plan)
            .FirstOrDefaultAsync(p => p.Id == paymentId) ?? throw new NotFoundException("পেমেন্টটি পাওয়া যায়নি।");
        if (payment.Status != SubscriptionPaymentStatus.Pending) throw new ConflictException("এই পেমেন্ট আগেই যাচাই করা হয়েছে।", "already_reviewed");
        payment.Status = SubscriptionPaymentStatus.Verified;
        payment.ReviewedBy = actorId;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewNote = note;
        audit.Log("billing.payment_verify", "subscription_payment", payment.Id, note, businessId: payment.Invoice.BusinessId, scope: RoleScope.Platform,
            changes: new() { ["invoice"] = payment.Invoice.Number, ["trx"] = payment.TransactionId, ["amount"] = payment.Amount.ToString("0") });
        await ApplyAsync(payment.Invoice, actorId);
        await db.SaveChangesAsync();
    }

    public async Task RejectAsync(Guid paymentId, Guid actorId, string reason)
    {
        var payment = await db.SubscriptionPayments.Include(p => p.Invoice).FirstOrDefaultAsync(p => p.Id == paymentId)
            ?? throw new NotFoundException("পেমেন্টটি পাওয়া যায়নি।");
        if (payment.Status != SubscriptionPaymentStatus.Pending) throw new ConflictException("এই পেমেন্ট আগেই যাচাই করা হয়েছে।", "already_reviewed");
        payment.Status = SubscriptionPaymentStatus.Rejected;
        payment.ReviewedBy = actorId;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewNote = reason;
        payment.Invoice.Status = InvoiceStatus.Open;
        Event(payment.Invoice.BusinessId, "payment.rejected", actor: actorId, data: new() { ["invoice"] = payment.Invoice.Number, ["reason"] = reason });
        audit.Log("billing.payment_reject", "subscription_payment", payment.Id, reason, businessId: payment.Invoice.BusinessId, scope: RoleScope.Platform);
        await NotifyOwnersAsync(payment.Invoice.BusinessId, "billing.payment_rejected", "আপনার পেমেন্ট যাচাই করা যায়নি", reason);
        await db.SaveChangesAsync();
    }

    /// <summary>Staff records a cash payment or grants a plan for free (partnership, compensation…).</summary>
    public async Task GrantAsync(Guid businessId, string planKey, BillingCycle cycle, SubscriptionPaymentMethod method, decimal? amount,
        string reason, Guid actorId)
    {
        if (method is not (SubscriptionPaymentMethod.Cash or SubscriptionPaymentMethod.Complimentary or SubscriptionPaymentMethod.Bkash
            or SubscriptionPaymentMethod.Nagad or SubscriptionPaymentMethod.BankTransfer))
            throw new BadRequestException("পেমেন্টের ধরন সঠিক নয়।", "invalid_method");
        if (!await db.Businesses.AnyAsync(b => b.Id == businessId)) throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");
        var plan = await PlanAsync(planKey);
        if (plan.Key == PlanCatalog.Free) throw new BadRequestException("ফ্রি প্ল্যান দেওয়ার দরকার নেই।", "invalid_plan");

        await db.SubscriptionInvoices.Where(i => i.BusinessId == businessId && i.Status == InvoiceStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Void));
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('invoice_number_seq') AS \"Value\"").SingleAsync();
        var subtotal = Price(plan, cycle);
        var paid = method == SubscriptionPaymentMethod.Complimentary ? 0 : amount ?? subtotal;
        var invoice = new SubscriptionInvoice
        {
            Number = $"INV-{BdTime.Today():yy}-{seq:D6}", BusinessId = businessId, PlanId = plan.Id, Plan = plan, Cycle = cycle,
            Subtotal = subtotal, Credit = subtotal - paid, Total = paid, CreatedBy = actorId,
        };
        db.SubscriptionInvoices.Add(invoice);
        db.SubscriptionPayments.Add(new SubscriptionPayment
        {
            Invoice = invoice, Method = method, Amount = paid, Status = SubscriptionPaymentStatus.Verified, SubmittedBy = actorId,
            ReviewedBy = actorId, ReviewedAt = DateTime.UtcNow, Note = reason, ReviewNote = reason,
        });
        audit.Log("billing.grant", "business", businessId, reason, businessId: businessId, scope: RoleScope.Platform,
            changes: new() { ["plan"] = plan.Key, ["cycle"] = SnakeCase.FromEnum(cycle), ["method"] = SnakeCase.FromEnum(method), ["amount"] = paid.ToString("0") });
        await ApplyAsync(invoice, actorId);
        await db.SaveChangesAsync();
    }

    /// <summary>Marks the invoice paid and moves the subscription onto its plan for one billing period.</summary>
    private async Task ApplyAsync(SubscriptionInvoice invoice, Guid actorId)
    {
        var plan = invoice.Plan ?? await db.SubscriptionPlans.FirstAsync(p => p.Id == invoice.PlanId);
        var now = DateTime.UtcNow;
        var sub = await db.BusinessSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.BusinessId == invoice.BusinessId);
        var fromKey = sub is null ? PlanCatalog.Free : sub.Plan.Key;
        // Renewing the same plan early extends the current period; anything else starts today.
        var start = sub is { Status: SubscriptionStatus.Active or SubscriptionStatus.PastDue } && sub.PlanId == plan.Id &&
                    sub.CurrentPeriodEnd is { } end && end > now ? end : now;
        if (sub is null)
        {
            sub = new BusinessSubscription { BusinessId = invoice.BusinessId };
            db.BusinessSubscriptions.Add(sub);
        }
        sub.PlanId = plan.Id;
        sub.Plan = plan;
        sub.Status = SubscriptionStatus.Active;
        sub.Cycle = invoice.Cycle;
        sub.CurrentPeriodStart = start;
        sub.CurrentPeriodEnd = AddCycle(start, invoice.Cycle);
        sub.CancelAtPeriodEnd = false;
        sub.CancelledAt = null;

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = now;
        invoice.PeriodStart = sub.CurrentPeriodStart;
        invoice.PeriodEnd = sub.CurrentPeriodEnd;
        Event(invoice.BusinessId, fromKey == plan.Key ? "plan.renewed" : "plan.activated", fromKey, plan.Key, actorId,
            new() { ["invoice"] = invoice.Number, ["until"] = sub.CurrentPeriodEnd.Value.ToString("O") });
        await NotifyOwnersAsync(invoice.BusinessId, "billing.activated", $"{plan.NameBn} প্ল্যান চালু হয়েছে",
            $"মেয়াদ: {BdTime.ToLocal(sub.CurrentPeriodEnd.Value):dd/MM/yyyy} পর্যন্ত। ধন্যবাদ!");
        entitlements.Forget(invoice.BusinessId);
    }

    // ---------- Cancel / resume / void ----------

    public async Task CancelAsync(Guid businessId, Guid actorId)
    {
        var sub = await db.BusinessSubscriptions.Include(s => s.Plan).FirstOrDefaultAsync(s => s.BusinessId == businessId)
            ?? throw new ConflictException("আপনি এখন ফ্রি প্ল্যানে আছেন।", "no_subscription");
        if (sub.Status == SubscriptionStatus.Trialing)
        {
            // Ending a trial early just drops to Free now.
            sub.Status = SubscriptionStatus.Cancelled;
            sub.CancelledAt = DateTime.UtcNow;
        }
        else if (sub.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue) sub.CancelAtPeriodEnd = true;
        else throw new ConflictException("আপনি এখন ফ্রি প্ল্যানে আছেন।", "no_subscription");
        Event(businessId, "plan.cancel_requested", sub.Plan.Key, PlanCatalog.Free, actorId);
        entitlements.Forget(businessId);
        await db.SaveChangesAsync();
    }

    public async Task ResumeAsync(Guid businessId, Guid actorId)
    {
        var sub = await db.BusinessSubscriptions.FirstOrDefaultAsync(s => s.BusinessId == businessId);
        if (sub is not { CancelAtPeriodEnd: true, Status: SubscriptionStatus.Active or SubscriptionStatus.PastDue })
            throw new ConflictException("বাতিলের কোনো অনুরোধ নেই।", "nothing_to_resume");
        sub.CancelAtPeriodEnd = false;
        Event(businessId, "plan.resumed", actor: actorId);
        await db.SaveChangesAsync();
    }

    public async Task VoidInvoiceAsync(Guid businessId, Guid invoiceId)
    {
        var n = await db.SubscriptionInvoices.Where(i => i.Id == invoiceId && i.BusinessId == businessId && i.Status == InvoiceStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Void));
        if (n == 0) throw new ConflictException("এই ইনভয়েস বাতিল করা যাবে না।", "invoice_not_open");
    }

    // ---------- Maintenance (runs hourly; safe to run any time) ----------

    public record MaintenanceResult(int TrialsEnded, int PastDue, int Expired, int Cancelled, int Reminders);

    public async Task<MaintenanceResult> RunMaintenanceAsync(DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;
        var grace = TimeSpan.FromDays(O.GraceDays);
        int trials = 0, pastDue = 0, expired = 0, cancelled = 0, reminders = 0;

        var subs = await db.BusinessSubscriptions.Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Trialing || s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
            .ToListAsync();
        foreach (var s in subs)
        {
            if (s.Status == SubscriptionStatus.Trialing && s.TrialEndsAt <= now)
            {
                s.Status = SubscriptionStatus.Expired; trials++;
                Event(s.BusinessId, "trial.ended", s.Plan.Key, PlanCatalog.Free);
                await NotifyOwnersAsync(s.BusinessId, "billing.trial_ended", "আপনার ট্রায়াল শেষ হয়েছে",
                    "এখন ফ্রি প্ল্যান চলছে। আপনার সব তথ্য আগের মতোই আছে — চাইলে যেকোনো সময় প্ল্যান বেছে নিন।");
            }
            else if (s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd <= now)
            {
                if (s.CancelAtPeriodEnd)
                {
                    s.Status = SubscriptionStatus.Cancelled; s.CancelledAt = now; cancelled++;
                    Event(s.BusinessId, "plan.cancelled", s.Plan.Key, PlanCatalog.Free);
                    await NotifyOwnersAsync(s.BusinessId, "billing.cancelled", $"{s.Plan.NameBn} প্ল্যান শেষ হয়েছে", "এখন ফ্রি প্ল্যান চলছে।");
                }
                else
                {
                    s.Status = SubscriptionStatus.PastDue; pastDue++;
                    Event(s.BusinessId, "plan.past_due", s.Plan.Key);
                    await NotifyOwnersAsync(s.BusinessId, "billing.past_due", "আপনার প্ল্যানের নবায়ন বাকি",
                        $"{EntitlementService.Bn(O.GraceDays)} দিনের মধ্যে নবায়ন না করলে ফ্রি প্ল্যানে চলে যাবে।");
                }
            }
            else if (s.Status == SubscriptionStatus.PastDue && s.CurrentPeriodEnd + grace <= now)
            {
                s.Status = SubscriptionStatus.Expired; expired++;
                Event(s.BusinessId, "plan.expired", s.Plan.Key, PlanCatalog.Free);
                await NotifyOwnersAsync(s.BusinessId, "billing.expired", "প্ল্যানের মেয়াদ শেষ — ফ্রি প্ল্যান চালু হয়েছে",
                    "আপনার কোনো তথ্য মুছে যায়নি। নবায়ন করলে সব সুবিধা আবার ফিরে আসবে।");
            }
            else
            {
                // Heads-up a few days before a trial or paid period runs out (once per deadline).
                var deadline = s.Status == SubscriptionStatus.Trialing ? s.TrialEndsAt : s.CancelAtPeriodEnd ? null : s.CurrentPeriodEnd;
                if (deadline is { } d && d > now && d - now <= TimeSpan.FromDays(O.ReminderDays))
                {
                    var type = $"reminder.sent:{d:yyyyMMddHHmm}";
                    if (!await db.SubscriptionEvents.AnyAsync(e => e.BusinessId == s.BusinessId && e.Type == type))
                    {
                        reminders++;
                        Event(s.BusinessId, type, s.Plan.Key, data: new() { ["deadline"] = d.ToString("O") });
                        var days = Math.Max(1, (int)Math.Ceiling((d - now).TotalDays));
                        await NotifyOwnersAsync(s.BusinessId, "billing.reminder",
                            s.Status == SubscriptionStatus.Trialing ? $"ট্রায়াল শেষ হতে আর {EntitlementService.Bn(days)} দিন বাকি" : $"প্ল্যান নবায়নের আর {EntitlementService.Bn(days)} দিন বাকি",
                            "সুবিধাগুলো চালু রাখতে এখনই প্ল্যান বেছে নিন বা নবায়ন করুন।");
                    }
                }
            }
        }
        await db.SaveChangesAsync();
        if (trials + pastDue + expired + cancelled + reminders > 0)
            logger.LogInformation("Subscription maintenance: {Trials} trials ended, {PastDue} past due, {Expired} expired, {Cancelled} cancelled, {Reminders} reminders",
                trials, pastDue, expired, cancelled, reminders);
        return new MaintenanceResult(trials, pastDue, expired, cancelled, reminders);
    }
}

/// <summary>Runs subscription maintenance on a timer (disable with Billing:RunMaintenance=false).</summary>
public class SubscriptionWorker(IServiceScopeFactory scopes, IOptions<BillingOptions> options, ILogger<SubscriptionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.RunMaintenance) return;
        var every = TimeSpan.FromMinutes(Math.Max(1, options.Value.MaintenanceIntervalMinutes));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SubscriptionService>().RunMaintenanceAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Subscription maintenance failed");
            }
            try { await Task.Delay(every, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
