using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

/// <summary>Platform staff editing businesses they don't belong to: access, the staff edit log, undo and page defaults.</summary>
[Collection(ApiCollection.Name)]
public class StaffAccessTests(ApiFactory factory)
{
    private async Task<Infrastructure.Api> StaffAsync(string role)
    {
        var api = await factory.RegisterAsync($"কর্মী {role}", TestData.NextPhone());
        await TestData.AssignRoleAsync(factory, api.UserId, role);
        return api;
    }

    [Fact]
    public async Task Moderator_edit_is_logged_and_admin_undoes_it()
    {
        var owner = await factory.RegisterAsync("মালিক স্টাফ১", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Staff Edit One");
        var moderator = await StaffAsync("system_moderator");
        var admin = await StaffAsync("admin");

        // The moderator sees it in the all-business list and opens the workspace with staff permissions.
        var list = await moderator.GetJson("/api/staff/businesses?q=Staff Edit One");
        Assert.NotNull(list["items"].FindById(businessId));
        var biz = await moderator.GetJson($"/api/businesses/{businessId}");
        Assert.Equal("সিস্টেম মডারেটর", biz.Str("staffRole"));
        Assert.Contains(biz["myPermissions"]!.AsArray(), p => p!.GetValue<string>() == "business.update");
        Assert.DoesNotContain(biz["myPermissions"]!.AsArray(), p => p!.GetValue<string>() == "members.manage");

        await moderator.PatchAsync($"/api/businesses/{businessId}", new { name = "Renamed by moderator", tagline = "নতুন ট্যাগলাইন" }).ExpectJson();
        await moderator.PutAsync($"/api/businesses/{businessId}/page/appearance", new { themeKey = "teal", headerStyle = "color" }).ExpectJson();

        var log = await admin.GetJson($"/api/staff/actions?businessId={businessId}&role=system_moderator");
        var items = log["items"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Str("summary") == "পেজের থিম ও সেকশন");
        var rename = items.First(i => i.Str("summary") == "ব্যবসার তথ্য")!;
        var detail = await admin.GetJson($"/api/staff/actions/{rename["id"]}");
        var fields = detail["rows"]![0]!["fields"]!.AsArray();
        Assert.Contains(fields, f => f.Str("field") == "নাম" && f.Str("before") == "Staff Edit One" && f.Str("after") == "Renamed by moderator");

        // Moderators can't undo; admins can, and the undo is itself logged.
        await moderator.PostAsync($"/api/staff/actions/{rename["id"]}/undo", new { reason = "x" }).ExpectStatus(403);
        await admin.PostAsync($"/api/staff/actions/{rename["id"]}/undo", new { reason = "ভুল নাম" }).ExpectStatus(204);
        var after = await owner.GetJson($"/api/businesses/{businessId}");
        Assert.Equal("Staff Edit One", after.Str("name"));
        await admin.PostAsync($"/api/staff/actions/{rename["id"]}/undo", new { }).ExpectStatus(409, "already_undone");

        log = await admin.GetJson($"/api/staff/actions?businessId={businessId}");
        var undo = log["items"]!.AsArray().First(i => i!["undoOf"] is not null)!;
        Assert.Equal("admin", undo.Str("roleKey"));
        Assert.StartsWith("ফিরিয়ে নেওয়া", undo.Str("summary"));
    }

    [Fact]
    public async Task Undo_is_refused_when_the_rows_changed_again()
    {
        var owner = await factory.RegisterAsync("মালিক স্টাফ২", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Staff Edit Two");
        var moderator = await StaffAsync("system_moderator");
        var admin = await StaffAsync("admin");

        await moderator.PatchAsync($"/api/businesses/{businessId}", new { name = "Moderator name" }).ExpectJson();
        await owner.PatchAsync($"/api/businesses/{businessId}", new { name = "Owner name" }).ExpectJson();
        var id = (await admin.GetJson($"/api/staff/actions?businessId={businessId}"))["items"]![0]!["id"];
        await admin.PostAsync($"/api/staff/actions/{id}/undo", new { }).ExpectStatus(409, "changed_since");
        Assert.Equal("Owner name", (await owner.GetJson($"/api/businesses/{businessId}")).Str("name"));
    }

    [Fact]
    public async Task Hours_replaced_in_bulk_are_restored_by_undo()
    {
        var owner = await factory.RegisterAsync("মালিক স্টাফ৩", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Staff Hours");
        var moderator = await StaffAsync("system_moderator");
        var admin = await StaffAsync("admin");

        await moderator.PutAsync($"/api/businesses/{businessId}/hours", new[] { new { dayOfWeek = 1, opensAt = "10:00:00", closesAt = "12:00:00" } })
            .ExpectStatus(200);
        Assert.Single((await owner.GetJson($"/api/businesses/{businessId}"))["hours"]!.AsArray());

        var action = (await admin.GetJson($"/api/staff/actions?businessId={businessId}"))["items"]![0]!;
        Assert.Equal("কাজের সময়", action.Str("summary"));
        await admin.PostAsync($"/api/staff/actions/{action["id"]}/undo", new { }).ExpectStatus(204);
        Assert.Equal(7, (await owner.GetJson($"/api/businesses/{businessId}"))["hours"]!.AsArray().Count);
    }

    [Fact]
    public async Task Team_delete_and_undo_are_admin_only_and_delete_is_soft()
    {
        var owner = await factory.RegisterAsync("মালিক স্টাফ৪", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Staff Delete");
        var moderator = await StaffAsync("system_moderator");
        var admin = await StaffAsync("admin");

        await moderator.GetAsync($"/api/businesses/{businessId}/members").ExpectStatus(404);
        await moderator.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_staff" }).ExpectStatus(404);
        await moderator.DeleteAsync($"/api/businesses/{businessId}").ExpectStatus(404);

        // Admins manage the team (never the owner) and can delete; delete is soft and undoable.
        var members = await admin.GetJson($"/api/businesses/{businessId}/members");
        var ownerMember = members.AsArray().Single()!;
        await admin.DeleteAsync($"/api/businesses/{businessId}/members/{ownerMember["id"]}").ExpectStatus(409, "cannot_remove_owner");
        await admin.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_manager" }).ExpectStatus(200);

        await admin.DeleteAsync($"/api/businesses/{businessId}").ExpectStatus(204);
        await factory.WithDbAsync(async db =>
        {
            var row = await db.Businesses.IgnoreQueryFilters().FirstAsync(b => b.Id == businessId);
            Assert.NotNull(row.DeletedAt);
        });
        var deleted = await admin.GetJson("/api/staff/businesses?deleted=true&q=Staff Delete");
        Assert.NotNull(deleted["items"].FindById(businessId));
        await moderator.GetAsync("/api/staff/businesses?deleted=true").ExpectStatus(403);

        var deletion = (await admin.GetJson($"/api/staff/actions?businessId={businessId}"))["items"]!.AsArray()
            .First(i => i.Str("summary") == "ব্যবসা মুছে ফেলা")!;
        await admin.PostAsync($"/api/staff/actions/{deletion["id"]}/undo", new { reason = "ভুলে মুছেছি" }).ExpectStatus(204);
        Assert.Equal("Staff Delete", (await owner.GetJson($"/api/businesses/{businessId}")).Str("name"));
    }

    [Fact]
    public async Task Analyst_sees_businesses_and_analytics_but_edits_nothing()
    {
        var owner = await factory.RegisterAsync("মালিক স্টাফ৫", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Staff Analyst");
        var analyst = await StaffAsync("platform_analyst");

        Assert.NotNull((await analyst.GetJson("/api/staff/businesses?q=Staff Analyst"))["items"].FindById(businessId));
        await analyst.GetAsync($"/api/businesses/{businessId}/analytics").ExpectStatus(200);
        await analyst.GetAsync("/api/admin/analytics").ExpectStatus(200);
        await analyst.PatchAsync($"/api/businesses/{businessId}", new { name = "নতুন নাম" }).ExpectStatus(404);
        await analyst.GetAsync("/api/staff/actions").ExpectStatus(403);
        await analyst.GetAsync("/api/admin/users").ExpectStatus(403);
    }

    [Fact]
    public async Task Members_never_get_staff_access_to_their_own_business()
    {
        var owner = await factory.RegisterAsync("মালিক স্টাফ৬", TestData.NextPhone());
        var (businessId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Staff Conflict");
        var invite = await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = TestData.NextPhone(), roleKey = "business_analyst" }).ExpectJson();
        var member = await factory.RegisterAsync("সদস্য মডারেটর", invite["invitation"]!.Str("inviteePhone"));
        await member.PostAsync("/api/invitations/accept", new { token = invite.Str("token") }).ExpectStatus(200);
        await TestData.AssignRoleAsync(factory, member.UserId, "system_moderator");

        // An analyst member stays an analyst here even though they moderate the platform.
        await member.PatchAsync($"/api/businesses/{businessId}", new { name = "নতুন নাম" }).ExpectStatus(403);
    }

    [Fact]
    public async Task Page_defaults_apply_to_pages_that_never_chose_their_own()
    {
        var admin = await StaffAsync("admin");
        var moderator = await StaffAsync("system_moderator");
        var sections = new[] { "about", "services", "reviews", "notices", "highlights", "gallery", "faqs", "team" }
            .Select(k => new { key = k, visible = k != "gallery" }).ToArray();

        await moderator.PutAsync("/api/staff/page-defaults", new { themeKey = "green", headerStyle = "color", sections }).ExpectStatus(403);
        await admin.PutAsync("/api/staff/page-defaults", new { themeKey = "green", headerStyle = "color", sections = new[] { new { key = "services", visible = false } } })
            .ExpectStatus(400, "section_required");

        var customised = await factory.RegisterAsync("মালিক নিজস্ব থিম", TestData.NextPhone());
        var (ownId, _, _) = await TestData.CreateDraftBusinessAsync(customised, "Own Theme");
        await customised.PutAsync($"/api/businesses/{ownId}/page/appearance", new { themeKey = "rose", headerStyle = "cover" }).ExpectJson();

        try
        {
            await admin.PutAsync("/api/staff/page-defaults", new { themeKey = "green", headerStyle = "color", sections }).ExpectJson();

            var fresh = await factory.RegisterAsync("মালিক ডিফল্ট থিম", TestData.NextPhone());
            var (freshId, _, _) = await TestData.CreateDraftBusinessAsync(fresh, "Default Theme");
            var page = await fresh.GetJson($"/api/businesses/{freshId}/page");
            Assert.Equal("green", page.Str("themeKey"));
            Assert.Equal("color", page.Str("headerStyle"));
            Assert.Equal("about", page["sections"]![0].Str("key"));
            Assert.False(page["sections"]!.AsArray().First(s => s.Str("key") == "gallery")!["visible"]!.GetValue<bool>());

            Assert.Equal("rose", (await customised.GetJson($"/api/businesses/{ownId}/page")).Str("themeKey"));
        }
        finally
        {
            // Other tests expect the built-in look.
            await factory.WithDbAsync(async db =>
            {
                await db.SiteContents.Where(x => x.Key == "page_defaults").ExecuteDeleteAsync();
            });
        }
    }
}
