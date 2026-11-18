using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

[Collection(ApiCollection.Name)]
public class AccessControlTests(ApiFactory factory)
{
    [Fact]
    public async Task Non_members_cannot_see_a_private_workspace()
    {
        var owner = await factory.RegisterAsync("গোপন মালিক", TestData.NextPhone());
        var stranger = await factory.RegisterAsync("অপরিচিত", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Private Draft");

        await stranger.GetAsync($"/api/businesses/{businessId}").ExpectStatus(404, "business_not_found");
        await stranger.GetAsync($"/api/businesses/{businessId}/bookings").ExpectStatus(404);
        await stranger.PatchAsync($"/api/businesses/{businessId}", new { name = "Hijacked" }).ExpectStatus(404);
    }

    [Fact]
    public async Task Platform_endpoints_require_platform_roles()
    {
        var user = await factory.RegisterAsync("সাধারণ ব্যবহারকারী", TestData.NextPhone());
        await user.GetAsync("/api/admin/users").ExpectStatus(403);
        await user.GetAsync("/api/moderation/verifications").ExpectStatus(403);
        await user.GetAsync("/api/inspections").ExpectStatus(403);
        await user.PutAsync("/api/admin/categories", new { id = 99, nameBn = "x", nameEn = "x", slug = "xx" }).ExpectStatus(403);
    }

    [Fact]
    public async Task Only_super_admin_can_grant_admin_and_zone_roles_need_a_zone()
    {
        var plainAdmin = await factory.RegisterAsync("এ্যাডমিন আনিস", TestData.NextPhone());
        var target = await factory.RegisterAsync("লক্ষ্য ব্যবহারকারী", TestData.NextPhone());
        await TestData.AssignRoleAsync(factory, plainAdmin.UserId, "admin");

        await plainAdmin.PostAsync("/api/admin/role-assignments", new { userId = target.UserId, roleKey = "admin" }).ExpectStatus(403);
        await plainAdmin.PostAsync("/api/admin/role-assignments", new { userId = target.UserId, roleKey = "inspector" })
            .ExpectStatus(400, "zone_required");
        var assignment = await plainAdmin.PostAsync("/api/admin/role-assignments",
            new { userId = target.UserId, roleKey = "inspector", zoneId = Zones.Kaliakair }).ExpectJson();
        await plainAdmin.PostAsync("/api/admin/role-assignments",
            new { userId = target.UserId, roleKey = "inspector", zoneId = Zones.Kaliakair }).ExpectStatus(409, "already_assigned");

        var ws = await target.GetJson("/api/me/workspaces");
        Assert.Contains(ws.AsArray(), w => w.Str("type") == "inspection" && w!["zoneId"]!.GetValue<int>() == Zones.Kaliakair);

        await plainAdmin.PostAsync($"/api/admin/role-assignments/{assignment.Id()}/revoke", new { reason = "দায়িত্ব পরিবর্তন" }).ExpectStatus(204);
        ws = await target.GetJson("/api/me/workspaces");
        Assert.DoesNotContain(ws.AsArray(), w => w.Str("type") == "inspection");

        var logs = await (await factory.AdminAsync()).GetJson($"/api/admin/audit-logs?entityId={assignment.Id()}");
        Assert.Equal(["role.revoke", "role.assign"], logs["items"]!.AsArray().Select(l => l.Str("action")).ToArray());
    }

    [Fact]
    public async Task Workspaces_follow_permissions_not_just_the_primary_role()
    {
        var ws = await (await factory.AdminAsync()).GetJson("/api/me/workspaces");
        var types = ws.AsArray().Select(w => w.Str("type")).ToList();
        Assert.Contains("administration", types);
        Assert.Contains("moderation", types); // super admin can moderate, so the switcher offers it
        Assert.Single(types, t => t == "administration");
    }

    [Fact]
    public async Task Team_roles_follow_rank_rules()
    {
        var owner = await factory.RegisterAsync("টিম মালিক", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Rank Shop");

        var managerPhone = TestData.NextPhone();
        var inv = await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = managerPhone, roleKey = "business_manager" }).ExpectJson();
        var manager = await factory.RegisterAsync("ম্যানেজার মুনা", managerPhone);
        await manager.PostAsync("/api/invitations/accept", new { token = inv.Str("token") }).ExpectStatus(200);

        // Owner role can never be granted; a manager can't grant manager (equal rank).
        await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_owner" })
            .ExpectStatus(403, "insufficient_rank");
        await manager.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_manager" })
            .ExpectStatus(403, "insufficient_rank");
        await manager.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "page_moderator" })
            .ExpectStatus(200);

        var members = await owner.GetJson($"/api/businesses/{businessId}/members");
        var ownerMember = members.AsArray().Single(m => m.Str("roleKey") == "business_owner")!;
        await manager.DeleteAsync($"/api/businesses/{businessId}/members/{ownerMember.Id()}").ExpectStatus(409, "cannot_remove_owner");

        // A manager can't delete the business.
        await manager.DeleteAsync($"/api/businesses/{businessId}").ExpectStatus(403);
    }
}
