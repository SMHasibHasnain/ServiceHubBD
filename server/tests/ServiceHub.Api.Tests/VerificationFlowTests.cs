using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

/// <summary>
/// Business lifecycle: draft → submitted → moderator review → field inspection (zone-scoped) → approved → published.
/// </summary>
[Collection(ApiCollection.Name)]
public class VerificationFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Business_goes_from_draft_to_published_through_moderation_and_zone_inspection()
    {
        var owner = await factory.RegisterAsync("হাসিব হাসনাইন", TestData.NextPhone());
        var moderator = await factory.RegisterAsync("মডারেটর মিতা", TestData.NextPhone());
        var inspector = await factory.RegisterAsync("ইন্সপেক্টর ইমন", TestData.NextPhone());
        var ctgInspector = await factory.RegisterAsync("চট্টগ্রাম ইন্সপেক্টর", TestData.NextPhone());

        // Moderator limited to Dhaka division; inspectors limited to their districts.
        await TestData.AssignRoleAsync(factory, moderator.UserId, "system_moderator", Zones.DhakaDivision);
        await TestData.AssignRoleAsync(factory, inspector.UserId, "inspector", Zones.Gazipur);
        await TestData.AssignRoleAsync(factory, ctgInspector.UserId, "inspector", Zones.ChattogramDistrict);

        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "হাসিব ইলেকট্রনিক্স", Zones.Mouchak);

        // Drafts are invisible to the public.
        var search = await factory.Anonymous().GetJson("/api/public/businesses?zoneId=" + Zones.Gazipur);
        Assert.Null(search["items"].FindById(businessId));

        // Owner's switcher shows the new workspace with owner permissions.
        var workspaces = await owner.GetJson("/api/me/workspaces");
        var ws = workspaces.AsArray().Single(w => w!["businessId"]?.GetValue<string>() == businessId.ToString());
        Assert.Equal("business_owner", ws.Str("roleKey"));

        await TestData.SubmitVerificationAsync(owner, businessId);
        await owner.PostAsync($"/api/businesses/{businessId}/verification", new
        {
            documents = new[] { new { documentType = "national_id", fileUrl = "https://files.example.com/nid.pdf" } },
        }).ExpectStatus(409, "verification_in_progress");

        // Moderator sees it in the Dhaka-division queue.
        var queue = await moderator.GetJson("/api/moderation/verifications");
        var requestId = queue["items"].FindById(businessId, "businessId")!.Id();

        // A moderator scoped to another division can't see or touch it.
        var ctgModerator = await factory.RegisterAsync("চট্টগ্রাম মডারেটর", TestData.NextPhone());
        await TestData.AssignRoleAsync(factory, ctgModerator.UserId, "system_moderator", Zones.ChattogramDivision);
        var ctgQueue = await ctgModerator.GetJson("/api/moderation/verifications");
        Assert.Null(ctgQueue["items"].FindById(businessId, "businessId"));
        await ctgModerator.PostAsync($"/api/moderation/verifications/{requestId}/start").ExpectStatus(403);

        // Decisions need the request claimed first, and a reason.
        await moderator.PostAsync($"/api/moderation/verifications/{requestId}/approve", new { reason = "সব ঠিক আছে" })
            .ExpectStatus(409, "not_under_review");
        await moderator.PostAsync($"/api/moderation/verifications/{requestId}/start").ExpectStatus(204);
        await moderator.PostAsync($"/api/moderation/verifications/{requestId}/approve", new { reason = "" }).ExpectStatus(400);

        // Ask for a field visit: routed to the Gazipur inspector (Mouchak is inside Gazipur).
        var inspectionId = Guid.Parse((await moderator.PostAsync($"/api/moderation/verifications/{requestId}/request-inspection",
            new { reason = "দোকানের ঠিকানা সরেজমিনে যাচাই প্রয়োজন" }).ExpectJson())!.GetValue<string>());
        await moderator.PostAsync($"/api/moderation/verifications/{requestId}/approve", new { reason = "সব ঠিক আছে" })
            .ExpectStatus(409, "inspection_pending");

        var tasks = await inspector.GetJson("/api/inspections");
        Assert.NotNull(tasks["items"].FindById(inspectionId));
        var ctgTasks = await ctgInspector.GetJson("/api/inspections");
        Assert.Null(ctgTasks["items"].FindById(inspectionId));
        await ctgInspector.PostAsync($"/api/inspections/{inspectionId}/start", new { latitude = 22.3, longitude = 91.8 }).ExpectStatus(404);

        await inspector.PostAsync($"/api/inspections/{inspectionId}/start", new { latitude = 24.07, longitude = 90.22 }).ExpectStatus(200);
        var report = await inspector.PostAsync($"/api/inspections/{inspectionId}/submit", new
        {
            outcome = "passed", findings = "সাইনবোর্ড আছে, ঠিকানা মিলেছে, মালিকের সাথে কথা হয়েছে।",
            checklist = new Dictionary<string, bool> { ["signboard"] = true, ["address_matches"] = true },
        }).ExpectJson();
        Assert.Equal("completed", report.Str("status"));

        // Moderator was notified in the moderation workspace, then approves.
        var modWs = await moderator.GetJson("/api/me/workspaces");
        Assert.Contains(modWs.AsArray(), w => w.Str("type") == "moderation" && w!["unreadNotifications"]!.GetValue<int>() > 0);
        await moderator.PostAsync($"/api/moderation/verifications/{requestId}/approve", new { reason = "মাঠ পরিদর্শনে উত্তীর্ণ" })
            .ExpectStatus(204);

        // Published: findable by any zone that contains Mouchak, not by others.
        var inGazipur = await factory.Anonymous().GetJson("/api/public/businesses?zoneId=" + Zones.Gazipur);
        Assert.NotNull(inGazipur["items"].FindById(businessId));
        var inDhakaDivision = await factory.Anonymous().GetJson("/api/public/businesses?zoneId=" + Zones.DhakaDivision);
        Assert.NotNull(inDhakaDivision["items"].FindById(businessId));
        var inDhakaDistrict = await factory.Anonymous().GetJson("/api/public/businesses?zoneId=" + Zones.DhakaDistrict);
        Assert.Null(inDhakaDistrict["items"].FindById(businessId));

        var owned = await owner.GetJson($"/api/businesses/{businessId}");
        Assert.Equal("published", owned.Str("status"));
        Assert.Equal("approved", owned.Str("verificationStatus"));

        // Every decision is in the moderation history with its reason.
        var detail = await moderator.GetJson($"/api/moderation/verifications/{requestId}");
        var actions = detail["history"]!.AsArray().Select(a => a.Str("action")).ToList();
        Assert.Contains("start_review", actions);
        Assert.Contains("request_inspection", actions);
        Assert.Contains("approve_business", actions);
        Assert.Single(detail["inspections"]!.AsArray());
    }

    [Fact]
    public async Task Incomplete_profile_cannot_be_submitted_and_rejected_page_can_resubmit()
    {
        var owner = await factory.RegisterAsync("অসম্পূর্ণ মালিক", TestData.NextPhone());
        var draft = await owner.PostAsync("/api/businesses", new { name = "Empty Shop", categoryId = Categories.Electrical }).ExpectJson(201);
        await owner.PostAsync($"/api/businesses/{draft.Id()}/verification", new
        {
            documents = new[] { new { documentType = "trade_license", fileUrl = "https://files.example.com/l.pdf" } },
        }).ExpectStatus(400, "profile_incomplete");

        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Reject Me");
        await TestData.SubmitVerificationAsync(owner, businessId);
        var admin = await factory.AdminAsync();
        var queue = await admin.GetJson("/api/moderation/verifications?pageSize=100");
        var requestId = queue["items"].FindById(businessId, "businessId")!.Id();
        await admin.PostAsync($"/api/moderation/verifications/{requestId}/start").ExpectStatus(204);
        await admin.PostAsync($"/api/moderation/verifications/{requestId}/reject", new { reason = "ট্রেড লাইসেন্স অস্পষ্ট" }).ExpectStatus(204);

        var history = await owner.GetJson($"/api/businesses/{businessId}/verification");
        Assert.Equal("rejected", history[0].Str("status"));
        Assert.Equal("ট্রেড লাইসেন্স অস্পষ্ট", history[0].Str("decisionReason"));

        await TestData.SubmitVerificationAsync(owner, businessId);
        history = await owner.GetJson($"/api/businesses/{businessId}/verification");
        Assert.Equal(2, history[0]!["attemptNo"]!.GetValue<int>());
    }

    [Fact]
    public async Task Moderator_cannot_review_a_business_they_belong_to()
    {
        var owner = await factory.RegisterAsync("মালিক-মডারেটর", TestData.NextPhone());
        await TestData.AssignRoleAsync(factory, owner.UserId, "system_moderator");
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Conflict Shop");
        await TestData.SubmitVerificationAsync(owner, businessId);

        var queue = await owner.GetJson("/api/moderation/verifications?pageSize=100");
        Assert.Null(queue["items"].FindById(businessId, "businessId"));

        var admin = await factory.AdminAsync();
        var requestId = (await admin.GetJson("/api/moderation/verifications?pageSize=100"))["items"].FindById(businessId, "businessId")!.Id();
        await owner.PostAsync($"/api/moderation/verifications/{requestId}/start").ExpectStatus(403, "conflict_of_interest");
    }
}
