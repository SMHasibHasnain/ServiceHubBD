using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Features.Billing;
using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

/// <summary>Plans, limits, trial, manual (TrxID) payments with staff verification, and lifecycle maintenance.</summary>
[Collection(ApiCollection.Name)]
public class BillingTests(ApiFactory factory)
{
    private static string NextTrx() => "TRX" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    private async Task<SubscriptionService.MaintenanceResult> MaintainAsync(DateTime at)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SubscriptionService>().RunMaintenanceAsync(at);
    }

    [Fact]
    public async Task Plans_are_public_and_free_limits_are_enforced_on_the_server()
    {
        var plans = await factory.Anonymous().GetJson("/api/plans");
        Assert.Equal(["free", "pro", "business"], plans["plans"]!.AsArray().Select(p => p.Str("key")).ToArray());
        Assert.Equal(499m, plans["plans"]![1]!["priceMonthly"]!.GetValue<decimal>());
        Assert.Equal(14, plans["trialDays"]!.GetValue<int>());

        // A draft business is on Free: 5 services, 2 questions per form, 2 team members, 7-day analytics, no CRM notes/auto-confirm.
        var owner = await factory.RegisterAsync("ফ্রি মালিক", TestData.NextPhone());
        var (businessId, serviceId, _) = await TestData.CreateDraftBusinessAsync(owner, "Free Plan Shop");
        var plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("free", plan.Str("planKey"));
        Assert.Equal(1, plan["usage"]!["services"]!.GetValue<int>());

        object Service(string name, int fields = 0) => new
        {
            name, categoryId = Categories.AcRepair, priceType = "fixed", price = 500, durationMinutes = 30,
            bookingFields = Enumerable.Range(0, fields).Select(i => new { label = $"প্রশ্ন {i}", fieldType = "short_text", isRequired = false, sortOrder = i }).ToArray(),
        };
        for (var i = 2; i <= 5; i++) await owner.PostAsync($"/api/businesses/{businessId}/services", Service($"Service {i}")).ExpectStatus(201);
        await owner.PostAsync($"/api/businesses/{businessId}/services", Service("Service 6")).ExpectStatus(403, "plan_limit_reached");
        await owner.PostAsync($"/api/businesses/{businessId}/services/{serviceId}/duplicate").ExpectStatus(403, "plan_limit_reached");

        await owner.DeleteAsync($"/api/businesses/{businessId}/services/{serviceId}").ExpectStatus(204);
        await owner.PostAsync($"/api/businesses/{businessId}/services", Service("Too many questions", fields: 3)).ExpectStatus(403, "plan_limit_reached");
        await owner.PostAsync($"/api/businesses/{businessId}/services", Service("Two questions", fields: 2)).ExpectStatus(201);

        await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_staff" }).ExpectStatus(200);
        await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_staff" }).ExpectStatus(200);
        await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_staff" }).ExpectStatus(403, "plan_limit_reached");

        await owner.GetAsync($"/api/businesses/{businessId}/analytics?days=7").ExpectStatus(200);
        Assert.Equal(7, (await owner.GetJson($"/api/businesses/{businessId}/analytics"))["days"]!.GetValue<int>());
        await owner.GetAsync($"/api/businesses/{businessId}/analytics?days=30").ExpectStatus(403, "plan_limit_reached");

        var settings = (await owner.GetJson($"/api/businesses/{businessId}"))["bookingSettings"]!;
        settings["autoConfirm"] = true;
        await owner.PutAsync($"/api/businesses/{businessId}/booking-settings", settings).ExpectStatus(403, "plan_limit_reached");
    }

    [Fact]
    public async Task Publishing_starts_a_trial_and_a_verified_TrxID_payment_activates_the_plan()
    {
        var owner = await factory.RegisterAsync("পেমেন্ট মালিক", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Paying Shop");

        var plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("pro", plan.Str("planKey"));
        Assert.True(plan["isTrial"]!.GetValue<bool>());
        Assert.Equal(-1, plan["limits"]!["services.max"]!.GetValue<int>());
        await owner.GetAsync($"/api/businesses/{businessId}/analytics?days=90").ExpectStatus(200);

        // A manager runs the business but can't buy plans.
        var managerPhone = TestData.NextPhone();
        var inv = await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = managerPhone, roleKey = "business_manager" }).ExpectJson();
        var manager = await factory.RegisterAsync("ম্যানেজার", managerPhone);
        await manager.PostAsync("/api/invitations/accept", new { token = inv.Str("token") }).ExpectStatus(200);
        await manager.PostAsync($"/api/businesses/{businessId}/billing/checkout", new { planKey = "pro" }).ExpectStatus(403);
        await manager.GetAsync($"/api/businesses/{businessId}/plan").ExpectStatus(200);

        // Checkout → invoice; free can't be bought.
        await owner.PostAsync($"/api/businesses/{businessId}/billing/checkout", new { planKey = "free" }).ExpectStatus(400, "invalid_plan");
        var invoice = await owner.PostAsync($"/api/businesses/{businessId}/billing/checkout", new { planKey = "pro", cycle = "yearly" }).ExpectJson();
        Assert.Equal(4990m, invoice["total"]!.GetValue<decimal>());
        Assert.Matches(@"^INV-\d{2}-\d{6}$", invoice.Str("number"));

        // Submit the bKash TrxID; the same TrxID can't be reused, and no second checkout while it's pending.
        var trx = NextTrx();
        var pending = await owner.PostAsync($"/api/businesses/{businessId}/billing/invoices/{invoice.Id()}/payments",
            new { method = "bkash", transactionId = trx.ToLowerInvariant(), senderNumber = "01712345678" }).ExpectJson();
        Assert.Equal("pending_verification", pending.Str("status"));
        Assert.Equal(trx, pending["payments"]![0].Str("transactionId"));
        await owner.PostAsync($"/api/businesses/{businessId}/billing/checkout", new { planKey = "business" }).ExpectStatus(409, "payment_pending");

        var other = await factory.RegisterAsync("অন্য মালিক", TestData.NextPhone());
        var (otherBiz, _, _) = await TestData.CreatePublishedBusinessAsync(factory, other, "Other Shop");
        var otherInvoice = await other.PostAsync($"/api/businesses/{otherBiz}/billing/checkout", new { planKey = "pro" }).ExpectJson();
        await other.PostAsync($"/api/businesses/{otherBiz}/billing/invoices/{otherInvoice.Id()}/payments", new { method = "bkash", transactionId = trx })
            .ExpectStatus(409, "duplicate_transaction");

        // Staff verify it from the admin queue.
        await owner.GetAsync("/api/admin/billing/payments").ExpectStatus(403);
        var admin = await factory.AdminAsync();
        var queue = await admin.GetJson("/api/admin/billing/payments?status=pending&pageSize=100");
        var row = queue["items"]!.AsArray().Single(p => p.Str("transactionId") == trx)!;
        Assert.Equal("Paying Shop", row.Str("businessName"));
        await admin.PostAsync($"/api/admin/billing/payments/{row.Id()}/verify", new { note = "স্টেটমেন্টের সাথে মিলেছে" }).ExpectStatus(204);
        await admin.PostAsync($"/api/admin/billing/payments/{row.Id()}/verify", new { }).ExpectStatus(409, "already_reviewed");

        plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("pro", plan.Str("planKey"));
        Assert.Equal("active", plan.Str("status"));
        Assert.Equal("yearly", plan.Str("cycle"));
        Assert.InRange(plan["currentPeriodEnd"]!.GetValue<DateTime>(), DateTime.UtcNow.AddDays(364), DateTime.UtcNow.AddDays(367));
        var invoices = await owner.GetJson($"/api/businesses/{businessId}/billing/invoices");
        Assert.Equal("paid", invoices[0].Str("status"));

        var ws = await owner.GetJson("/api/me/workspaces");
        Assert.True(ws.AsArray().Single(w => w!["businessId"]?.GetValue<string>() == businessId.ToString())!["unreadNotifications"]!.GetValue<int>() >= 1);

        var summary = await admin.GetJson("/api/admin/billing/summary");
        Assert.True(summary["activePaid"]!.GetValue<int>() >= 1);
        Assert.True(summary["mrr"]!.GetValue<decimal>() >= 416);

        var logs = await admin.GetJson($"/api/admin/audit-logs?entityId={row.Id()}");
        Assert.Equal("billing.payment_verify", logs["items"]![0].Str("action"));
    }

    [Fact]
    public async Task Rejected_payments_reopen_the_invoice_and_staff_can_grant_plans()
    {
        var owner = await factory.RegisterAsync("রিজেক্ট মালিক", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Reject Shop");
        var invoice = await owner.PostAsync($"/api/businesses/{businessId}/billing/checkout", new { planKey = "business" }).ExpectJson();
        Assert.Equal(1499m, invoice["total"]!.GetValue<decimal>());
        var trx = NextTrx();
        await owner.PostAsync($"/api/businesses/{businessId}/billing/invoices/{invoice.Id()}/payments", new { method = "nagad", transactionId = trx }).ExpectStatus(200);

        var admin = await factory.AdminAsync();
        var row = (await admin.GetJson("/api/admin/billing/payments?status=pending&pageSize=100"))["items"]!.AsArray().Single(p => p.Str("transactionId") == trx)!;
        await admin.PostAsync($"/api/admin/billing/payments/{row.Id()}/reject", new { reason = "এই নম্বরে কোনো লেনদেন পাওয়া যায়নি" }).ExpectStatus(204);
        var invoices = await owner.GetJson($"/api/businesses/{businessId}/billing/invoices");
        Assert.Equal("open", invoices[0].Str("status"));
        Assert.Equal("rejected", invoices[0]!["payments"]![0].Str("status"));
        // After a rejection the same TrxID may be submitted again (e.g. a typo fixed on the other side).
        await owner.PostAsync($"/api/businesses/{businessId}/billing/invoices/{invoice.Id()}/payments", new { method = "nagad", transactionId = trx }).ExpectStatus(200);
        var again = (await admin.GetJson("/api/admin/billing/payments?status=pending&pageSize=100"))["items"]!.AsArray().Single(p => p.Str("transactionId") == trx)!;
        await admin.PostAsync($"/api/admin/billing/payments/{again.Id()}/reject", new { reason = "দ্বিতীয়বারও লেনদেন পাওয়া যায়নি" }).ExpectStatus(204);

        // Cash payment recorded by staff activates immediately, even on a draft business.
        await admin.PostAsync($"/api/admin/billing/businesses/{businessId}/grant",
            new { planKey = "pro", cycle = "monthly", method = "cash", amount = 499, reason = "মাঠ পর্যায়ে নগদ গ্রহণ" }).ExpectStatus(204);
        var plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("pro", plan.Str("planKey"));
        Assert.True(plan["isPaid"]!.GetValue<bool>());

        // Switching to a bigger plan mid-period credits the unused part of the current one.
        var upgrade = await owner.PostAsync($"/api/businesses/{businessId}/billing/checkout", new { planKey = "business" }).ExpectJson();
        Assert.InRange(upgrade["credit"]!.GetValue<decimal>(), 480m, 499m);
        Assert.Equal(1499m - upgrade["credit"]!.GetValue<decimal>(), upgrade["total"]!.GetValue<decimal>());
        await owner.PostAsync($"/api/businesses/{businessId}/billing/invoices/{upgrade.Id()}/void").ExpectStatus(204);

        var subs = await admin.GetJson($"/api/admin/billing/subscriptions?q=Reject Shop");
        Assert.Equal("pro", subs["items"]![0].Str("planKey"));
        Assert.Equal("রিজেক্ট মালিক", subs["items"]![0].Str("ownerName"));
    }

    [Fact]
    public async Task Trials_expire_paid_periods_get_a_grace_period_then_fall_back_to_free()
    {
        var owner = await factory.RegisterAsync("মেয়াদ মালিক", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Lifecycle Shop");
        DateTime trialEnd = default;
        await factory.WithDbAsync(async db => trialEnd = (await db.BusinessSubscriptions.FirstAsync(s => s.BusinessId == businessId)).TrialEndsAt!.Value);

        // Reminder a couple of days before the trial ends — once.
        Assert.True((await MaintainAsync(trialEnd.AddDays(-2))).Reminders >= 1);
        await MaintainAsync(trialEnd.AddDays(-1));
        await factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.SubscriptionEvents.CountAsync(e => e.BusinessId == businessId && e.Type.StartsWith("reminder.sent"))));

        // Trial over → Free (effective immediately, even before maintenance flips the row).
        await factory.WithDbAsync(async db =>
        {
            var s = await db.BusinessSubscriptions.FirstAsync(x => x.BusinessId == businessId);
            s.TrialEndsAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        Assert.Equal("free", (await owner.GetJson($"/api/businesses/{businessId}/plan")).Str("planKey"));
        Assert.True((await MaintainAsync(DateTime.UtcNow)).TrialsEnded >= 1);

        // Pay for a month, then let it lapse: past due within grace keeps the plan, after grace it's Free.
        var admin = await factory.AdminAsync();
        await admin.PostAsync($"/api/admin/billing/businesses/{businessId}/grant",
            new { planKey = "pro", cycle = "monthly", method = "complimentary", reason = "পরীক্ষামূলক প্রদান" }).ExpectStatus(204);
        await factory.WithDbAsync(async db =>
        {
            var s = await db.BusinessSubscriptions.FirstAsync(x => x.BusinessId == businessId);
            s.CurrentPeriodEnd = DateTime.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();
        });
        await MaintainAsync(DateTime.UtcNow);
        var plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("pro", plan.Str("planKey"));
        Assert.Equal("past_due", plan.Str("status"));

        await factory.WithDbAsync(async db =>
        {
            var s = await db.BusinessSubscriptions.FirstAsync(x => x.BusinessId == businessId);
            s.CurrentPeriodEnd = DateTime.UtcNow.AddDays(-8);
            await db.SaveChangesAsync();
        });
        Assert.True((await MaintainAsync(DateTime.UtcNow)).Expired >= 1);
        plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("free", plan.Str("planKey"));
        Assert.Equal("expired", plan.Str("status"));

        // Nothing was deleted: services above the free limit stay, only adding more is blocked.
        Assert.True((await owner.GetJson($"/api/businesses/{businessId}/services")).AsArray().Count >= 1);
    }

    [Fact]
    public async Task Cancelling_keeps_the_plan_until_the_period_ends()
    {
        var owner = await factory.RegisterAsync("বাতিল মালিক", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Cancel Shop");
        await owner.PostAsync($"/api/businesses/{businessId}/billing/cancel").ExpectStatus(409, "no_subscription");
        var admin = await factory.AdminAsync();
        await admin.PostAsync($"/api/admin/billing/businesses/{businessId}/grant",
            new { planKey = "business", cycle = "monthly", method = "cash", reason = "নগদ গ্রহণ করা হয়েছে" }).ExpectStatus(204);

        await owner.PostAsync($"/api/businesses/{businessId}/billing/cancel").ExpectStatus(204);
        var plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("business", plan.Str("planKey"));
        Assert.True(plan["cancelAtPeriodEnd"]!.GetValue<bool>());
        await owner.PostAsync($"/api/businesses/{businessId}/billing/resume").ExpectStatus(204);
        await owner.PostAsync($"/api/businesses/{businessId}/billing/cancel").ExpectStatus(204);

        await factory.WithDbAsync(async db =>
        {
            var s = await db.BusinessSubscriptions.FirstAsync(x => x.BusinessId == businessId);
            s.CurrentPeriodEnd = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });
        Assert.True((await MaintainAsync(DateTime.UtcNow)).Cancelled >= 1);
        plan = await owner.GetJson($"/api/businesses/{businessId}/plan");
        Assert.Equal("free", plan.Str("planKey"));
        Assert.Equal("cancelled", plan.Str("status"));
    }
}
