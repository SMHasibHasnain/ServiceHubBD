using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

/// <summary>Analytics endpoints for every workspace: correct numbers, period handling and access rules.</summary>
[Collection(ApiCollection.Name)]
public class AnalyticsTests(ApiFactory factory)
{
    private static double Kpi(JsonNode dto, string key) => dto["kpis"]![key]!["value"]!.GetValue<double>();

    [Fact]
    public async Task Business_analytics_counts_bookings_revenue_and_ratings()
    {
        var owner = await factory.RegisterAsync("বিশ্লেষণ মালিক", TestData.NextPhone());
        var (businessId, serviceId, fieldId) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Analytics AC");
        var alice = await factory.RegisterAsync("গ্রাহক আলিফ", TestData.NextPhone());
        var bina = await factory.RegisterAsync("গ্রাহক বীণা", TestData.NextPhone());

        // Two bookings: one completed at ৳2,000 and reviewed 5★, one cancelled by the customer.
        var t1 = await TestData.FirstFreeSlotAsync(alice, businessId, serviceId);
        var done = await alice.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, t1)).ExpectJson(201);
        var t2 = await TestData.FirstFreeSlotAsync(bina, businessId, serviceId);
        var lost = await bina.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, t2)).ExpectJson(201);
        await bina.PostAsync($"/api/me/bookings/{lost.Id()}/cancel", new { reason = "অন্য দিনে নেব" }).ExpectStatus(200);

        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{done.Id()}/confirm").ExpectStatus(200);
        await factory.WithDbAsync(async db =>
        {
            var b = await db.Bookings.FirstAsync(x => x.Id == done.Id());
            b.StartAt = DateTime.UtcNow.AddHours(-3);
            b.EndAt = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        });
        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{done.Id()}/complete", new { finalPrice = 2000 }).ExpectStatus(200);
        await alice.PostAsync($"/api/me/bookings/{done.Id()}/review", new { rating = 5 }).ExpectStatus(200);

        var a = await owner.GetJson($"/api/businesses/{businessId}/analytics?days=7");
        Assert.Equal(7, a["days"]!.GetValue<int>());
        Assert.Equal(2, Kpi(a, "bookings"));
        Assert.Equal(1, Kpi(a, "completed"));
        Assert.Equal(2000, Kpi(a, "revenue"));
        Assert.Equal(2000, Kpi(a, "avg_ticket"));
        Assert.Equal(50, Kpi(a, "completion_rate"));      // 1 completed of 2 resolved
        Assert.Equal(50, Kpi(a, "cancellation_rate"));    // 1 cancelled of 2 received
        Assert.Equal(2, Kpi(a, "new_customers"));
        Assert.Equal(5, Kpi(a, "avg_rating"));
        Assert.Equal(0, a["kpis"]!["bookings"]!["previous"]!.GetValue<double>());

        // One point per day, empty days included, totals agree with the KPI.
        var points = a["trend"]!["points"]!.AsArray();
        Assert.Equal(7, points.Count);
        Assert.Equal(2, points.Sum(pt => pt!["values"]![0]!.GetValue<double>()));
        Assert.Equal(2000, points.Sum(pt => pt!["values"]![2]!.GetValue<double>()));

        var mix = a["statusMix"]!.AsArray().ToDictionary(s => s.Str("key"), s => s!["value"]!.GetValue<double>());
        Assert.Equal(1, mix["completed"]);
        Assert.Equal(1, mix["cancelled"]);
        Assert.Equal(2, a["topServices"]![0]!["value"]!.GetValue<double>());
        Assert.Equal(1, a["ratings"]!.AsArray().Single(r => r.Str("key") == "5")!["value"]!.GetValue<double>());
        Assert.Equal(2, a["heatmap"]!.AsArray().Sum(day => day!.AsArray().Sum(h => h!.GetValue<int>())));

        // Unknown range falls back to 30 days.
        Assert.Equal(30, (await owner.GetJson($"/api/businesses/{businessId}/analytics?days=12"))["days"]!.GetValue<int>());
    }

    [Fact]
    public async Task Analytics_follow_workspace_permissions()
    {
        var owner = await factory.RegisterAsync("অনুমতি মালিক", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Perm Shop");
        var stranger = await factory.RegisterAsync("অপরিচিত ব্যক্তি", TestData.NextPhone());

        // Staff can't see business analytics; an analyst can.
        var staffPhone = TestData.NextPhone();
        var inv = await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = staffPhone, roleKey = "business_staff" }).ExpectJson();
        var staff = await factory.RegisterAsync("স্টাফ সুমন", staffPhone);
        await staff.PostAsync("/api/invitations/accept", new { token = inv.Str("token") }).ExpectStatus(200);
        var analystPhone = TestData.NextPhone();
        inv = await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = analystPhone, roleKey = "business_analyst" }).ExpectJson();
        var analyst = await factory.RegisterAsync("বিশ্লেষক নীলা", analystPhone);
        await analyst.PostAsync("/api/invitations/accept", new { token = inv.Str("token") }).ExpectStatus(200);

        await stranger.GetAsync($"/api/businesses/{businessId}/analytics").ExpectStatus(404);
        await staff.GetAsync($"/api/businesses/{businessId}/analytics").ExpectStatus(403);
        await analyst.GetAsync($"/api/businesses/{businessId}/analytics").ExpectStatus(200);

        // Platform analytics need platform roles.
        await stranger.GetAsync("/api/admin/analytics").ExpectStatus(403);
        await stranger.GetAsync("/api/moderation/analytics").ExpectStatus(403);
        await stranger.GetAsync("/api/inspections/analytics").ExpectStatus(403);
        await stranger.GetAsync("/api/me/stats").ExpectStatus(200);

        var inspector = await factory.RegisterAsync("পরিদর্শক জামিল", TestData.NextPhone());
        await TestData.AssignRoleAsync(factory, inspector.UserId, "inspector", Zones.Kaliakair);
        var ins = await inspector.GetJson("/api/inspections/analytics?days=90");
        Assert.Equal(90, ins["days"]!.GetValue<int>());
        await inspector.GetAsync("/api/admin/analytics").ExpectStatus(403);
    }

    [Fact]
    public async Task Moderation_and_admin_analytics_reflect_the_verification_pipeline()
    {
        var admin = await factory.AdminAsync();
        var before = await admin.GetJson("/api/admin/analytics?days=7");
        var modBefore = await admin.GetJson("/api/moderation/analytics?days=7");

        var owner = await factory.RegisterAsync("পাইপলাইন মালিক", TestData.NextPhone());
        await TestData.CreatePublishedBusinessAsync(factory, owner, "Pipeline Electric");

        var after = await admin.GetJson("/api/admin/analytics?days=7");
        Assert.Equal(Kpi(before, "new_users") + 1, Kpi(after, "new_users"));
        Assert.Equal(Kpi(before, "new_businesses") + 1, Kpi(after, "new_businesses"));
        Assert.Equal(Kpi(before, "published_businesses") + 1, Kpi(after, "published_businesses"));

        // Funnel steps never grow: each is a subset of the one before.
        var funnel = after["funnel"]!.AsArray().Select(s => s!["value"]!.GetValue<double>()).ToArray();
        Assert.Equal(5, funnel.Length);
        Assert.True(funnel.Zip(funnel.Skip(1)).All(x => x.First >= x.Second));
        Assert.Equal(7, after["growth"]!["points"]!.AsArray().Count);
        Assert.Contains(after["roles"]!.AsArray(), r => r.Str("key") == "super_admin");

        var mod = await admin.GetJson("/api/moderation/analytics?days=7");
        Assert.Equal(Kpi(modBefore, "submitted") + 1, Kpi(mod, "submitted"));
        Assert.Equal(Kpi(modBefore, "decided") + 1, Kpi(mod, "decided"));
        var approved = mod["outcomes"]!.AsArray().Single(o => o.Str("key") == "approved")!["value"]!.GetValue<double>();
        var approvedBefore = modBefore["outcomes"]!.AsArray().Single(o => o.Str("key") == "approved")!["value"]!.GetValue<double>();
        Assert.Equal(approvedBefore + 1, approved);
        Assert.Equal(4, mod["aging"]!.AsArray().Count);
        Assert.NotEmpty(mod["moderators"]!.AsArray());
    }

    [Fact]
    public async Task Customer_stats_summarise_own_bookings()
    {
        var owner = await factory.RegisterAsync("স্ট্যাট মালিক", TestData.NextPhone());
        var (businessId, serviceId, fieldId) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Stats Cooling");
        var customer = await factory.RegisterAsync("স্ট্যাট গ্রাহক", TestData.NextPhone());
        var time = await TestData.FirstFreeSlotAsync(customer, businessId, serviceId);
        await customer.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, time)).ExpectStatus(201);

        var s = await customer.GetJson("/api/me/stats");
        Assert.Equal(1, s["totalBookings"]!.GetValue<int>());
        Assert.Equal(1, s["upcoming"]!.GetValue<int>());
        Assert.Equal(1, s["businessesUsed"]!.GetValue<int>());
        Assert.Equal(0, s["totalSpent"]!.GetValue<double>());
    }
}
