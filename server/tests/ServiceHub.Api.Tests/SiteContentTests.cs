using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

[Collection(ApiCollection.Name)]
public class SiteContentTests(ApiFactory factory)
{
    [Fact]
    public async Task Super_admin_saves_and_resets_homepage_content_everyone_can_read_it()
    {
        var admin = await factory.AdminAsync();
        var anon = factory.Anonymous();

        var content = new { version = 1, site = new { navSearch = "খুঁজুন" }, sections = new object[] { new { id = "hero", type = "hero", visible = true, data = new { titleLine1 = "নতুন শিরোনাম" } } } };
        await admin.PutAsync("/api/admin/site/home", new { content }).ExpectJson();

        var read = await anon.GetJson("/api/site/home");
        Assert.Equal("নতুন শিরোনাম", read["content"]!["sections"]![0]!["data"]!.Str("titleLine1"));
        Assert.NotNull(read["updatedAt"]);

        var logs = await admin.GetJson("/api/admin/audit-logs?entityId=home");
        Assert.Contains(logs["items"]!.AsArray(), l => l.Str("action") == "site_content.update");

        await admin.DeleteAsync("/api/admin/site/home").ExpectStatus(204);
        read = await anon.GetJson("/api/site/home");
        Assert.Null(read["content"]);
    }

    [Fact]
    public async Task Only_super_admin_may_edit_site_content()
    {
        var plainAdmin = await factory.RegisterAsync("এ্যাডমিন সাইট", TestData.NextPhone());
        await TestData.AssignRoleAsync(factory, plainAdmin.UserId, "admin");
        var user = await factory.RegisterAsync("সাধারণ সাইট", TestData.NextPhone());
        var body = new { content = new { version = 1 } };

        await plainAdmin.PutAsync("/api/admin/site/home", body).ExpectStatus(403);
        await user.PutAsync("/api/admin/site/home", body).ExpectStatus(403);
        await factory.Anonymous().PutAsync("/api/admin/site/home", body).ExpectStatus(401);
        await user.DeleteAsync("/api/admin/site/home").ExpectStatus(403);
    }

    [Fact]
    public async Task Content_must_be_an_object_for_a_known_page()
    {
        var admin = await factory.AdminAsync();
        await admin.PutAsync("/api/admin/site/home", new { content = new[] { 1, 2 } }).ExpectStatus(400, "invalid_content");
        await admin.PutAsync("/api/admin/site/home", new { content = new { big = new string('x', 300 * 1024) } }).ExpectStatus(400, "content_too_large");
        await admin.PutAsync("/api/admin/site/unknown", new { content = new { } }).ExpectStatus(404);
        await factory.Anonymous().GetAsync("/api/site/unknown").ExpectStatus(404);
    }

    [Fact]
    public async Task Admin_creates_lists_and_deletes_categories_but_not_ones_in_use()
    {
        var admin = await factory.AdminAsync();
        var slug = $"test-cat-{Guid.NewGuid():N}"[..20];
        var parent = await admin.PostAsync("/api/admin/categories", new { nameBn = "পরীক্ষা", nameEn = "Test", slug }).ExpectJson();
        var parentId = parent["id"]!.GetValue<int>();
        var child = await admin.PostAsync("/api/admin/categories", new { parentId, nameBn = "উপ", nameEn = "Sub", slug = slug + "-sub" }).ExpectJson();
        var childId = child["id"]!.GetValue<int>();

        // Two levels only, and slugs stay unique.
        await admin.PostAsync("/api/admin/categories", new { parentId = childId, nameBn = "নাতি", nameEn = "Grandchild", slug = slug + "-gc" }).ExpectStatus(400, "invalid_parent");
        await admin.PostAsync("/api/admin/categories", new { nameBn = "আবার", nameEn = "Again", slug }).ExpectStatus(409, "slug_taken");

        var all = await admin.GetJson("/api/admin/categories");
        Assert.Contains(all.AsArray(), c => c!["id"]!.GetValue<int>() == childId && c["parentId"]!.GetValue<int>() == parentId);

        await admin.DeleteAsync($"/api/admin/categories/{parentId}").ExpectStatus(409, "category_has_children");
        await admin.DeleteAsync($"/api/admin/categories/{childId}").ExpectStatus(204);
        await admin.DeleteAsync($"/api/admin/categories/{parentId}").ExpectStatus(204);

        // A category a business uses can only be hidden.
        var owner = await factory.RegisterAsync("ক্যাটাগরি মালিক", TestData.NextPhone());
        await TestData.CreateDraftBusinessAsync(owner, "Category User");
        var used = (await admin.GetJson("/api/admin/categories")).AsArray().First(c => c!["businessCount"]!.GetValue<int>() > 0)!;
        await admin.DeleteAsync($"/api/admin/categories/{used["id"]}").ExpectStatus(409, "category_in_use");

        var user = await factory.RegisterAsync("সাধারণ ক্যাটাগরি", TestData.NextPhone());
        await user.GetAsync("/api/admin/categories").ExpectStatus(403);
        await user.PostAsync("/api/admin/categories", new { nameBn = "x", nameEn = "x", slug = "xx-yy" }).ExpectStatus(403);
    }
}
