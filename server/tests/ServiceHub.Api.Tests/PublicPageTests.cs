using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

/// <summary>Public page customisation: notice board, sections, theme, FAQs, highlights, gallery, team and uploads.</summary>
[Collection(ApiCollection.Name)]
public class PublicPageTests(ApiFactory factory)
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01];

    private static object Notice(string title, bool published = true, bool pinned = false, string body = "শীতকালীন অফার: সার্ভিসিংয়ে ২০% ছাড়।") =>
        new { title, body, isPublished = published, isPinned = pinned };

    private static Task<HttpResponseMessage> UploadAsync(Infrastructure.Api api, Guid businessId, byte[] bytes, string name = "photo.png")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", name);
        return api.Client.PostAsync($"/api/businesses/{businessId}/uploads", content);
    }

    private static string[] Keys(JsonNode? array, string key = "key") => array!.AsArray().Select(n => n.Str(key)).ToArray();

    [Fact]
    public async Task Notice_board_respects_plan_limit_phone_guard_and_hiding()
    {
        var owner = await factory.RegisterAsync("নোটিশ মালিক", TestData.NextPhone());
        var (draftId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Notice Draft Shop");

        // Free plan: one live notice. Unpublished drafts don't count.
        var first = await owner.PostAsync($"/api/businesses/{draftId}/page/notices", Notice("ঈদের ছুটি")).ExpectJson();
        Assert.True(first["isLive"]!.GetValue<bool>());
        await owner.PostAsync($"/api/businesses/{draftId}/page/notices", Notice("নতুন অফার")).ExpectStatus(403, "plan_limit_reached");
        var draftNotice = await owner.PostAsync($"/api/businesses/{draftId}/page/notices", Notice("খসড়া", published: false)).ExpectJson();
        Assert.False(draftNotice["isLive"]!.GetValue<bool>());
        await owner.PutAsync($"/api/businesses/{draftId}/page/notices/{first.Id()}", Notice("ঈদের ছুটি (হালনাগাদ)")).ExpectStatus(200);

        // Phone numbers are rejected in Latin or Bangla digits, with separators.
        await owner.PostAsync($"/api/businesses/{draftId}/page/notices", Notice("কল করুন", published: false, body: "যোগাযোগ: 01712-345678"))
            .ExpectStatus(400, "contact_info_not_allowed");
        await owner.PostAsync($"/api/businesses/{draftId}/page/notices", Notice("কল করুন", published: false, body: "যোগাযোগ: ০১৭১২ ৩৪৫ ৬৭৮"))
            .ExpectStatus(400, "contact_info_not_allowed");

        // Non-members can't see the page settings; the workspace stays private.
        var stranger = await factory.RegisterAsync("অপরিচিত", TestData.NextPhone());
        await stranger.GetAsync($"/api/businesses/{draftId}/page").ExpectStatus(404, "business_not_found");
        await stranger.PostAsync($"/api/businesses/{draftId}/page/notices", Notice("অপরিচিতের নোটিশ")).ExpectStatus(404, "business_not_found");

        // Published page (Pro trial): live notices show, pinned first; hiding the section removes them.
        var (bizId, _, _) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Notice Board Shop");
        await owner.PostAsync($"/api/businesses/{bizId}/page/notices", Notice("সাধারণ নোটিশ")).ExpectStatus(200);
        await owner.PostAsync($"/api/businesses/{bizId}/page/notices", Notice("গুরুত্বপূর্ণ", pinned: true)).ExpectStatus(200);
        await owner.PostAsync($"/api/businesses/{bizId}/page/notices", Notice("লুকানো", published: false)).ExpectStatus(200);

        var slug = (await owner.GetJson($"/api/businesses/{bizId}")).Str("slug");
        var profile = await factory.Anonymous().GetJson($"/api/public/businesses/{slug}");
        Assert.Equal(["গুরুত্বপূর্ণ", "সাধারণ নোটিশ"], Keys(profile["notices"], "title"));
        Assert.Contains("notices", profile["sections"]!.AsArray().Select(n => n!.GetValue<string>()));

        var layout = (await owner.GetJson($"/api/businesses/{bizId}/page"))["sections"]!.AsArray()
            .Select(s => new { key = s.Str("key"), visible = s.Str("key") != "notices" }).ToArray();
        await owner.PutAsync($"/api/businesses/{bizId}/page/layout", new { sections = layout }).ExpectStatus(200);
        profile = await factory.Anonymous().GetJson($"/api/public/businesses/{slug}");
        Assert.Empty(profile["notices"]!.AsArray());
        Assert.DoesNotContain("notices", profile["sections"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task Layout_theme_faqs_highlights_and_team()
    {
        var owner = await factory.RegisterAsync("লেআউট মালিক", TestData.NextPhone());
        var (draftId, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Layout Draft Shop");

        var sections = Keys((await owner.GetJson($"/api/businesses/{draftId}/page"))["sections"]);
        Assert.Equal(["notices", "highlights", "services", "gallery", "reviews", "faqs", "team", "about"], sections);

        // Hiding optional sections is free; reviews/services/about can never be hidden; reordering needs a paid plan.
        await owner.PutAsync($"/api/businesses/{draftId}/page/layout",
            new { sections = sections.Select(k => new { key = k, visible = k != "gallery" }) }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{draftId}/page/layout",
            new { sections = sections.Select(k => new { key = k, visible = k != "reviews" }) }).ExpectStatus(400, "section_required");
        await owner.PutAsync($"/api/businesses/{draftId}/page/layout",
            new { sections = sections.Reverse().Select(k => new { key = k, visible = true }) }).ExpectStatus(403, "plan_limit_reached");

        await owner.PutAsync($"/api/businesses/{draftId}/page/appearance", new { themeKey = "teal", headerStyle = "color" }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{draftId}/page/appearance", new { themeKey = "#ff0000", headerStyle = "cover" }).ExpectStatus(400, "invalid_theme");

        await owner.PutAsync($"/api/businesses/{draftId}/page/faqs", new[] { new { question = "কল দিন?", answer = "01812345678 নম্বরে" } })
            .ExpectStatus(400, "contact_info_not_allowed");
        await owner.PutAsync($"/api/businesses/{draftId}/page/highlights", new[] { new { iconKey = "rocket", title = "দ্রুত সেবা" } })
            .ExpectStatus(400, "invalid_icon");

        // Preview works for the draft (not public yet), for members only, and matches the public shape.
        var preview = await owner.GetJson($"/api/businesses/{draftId}/page/preview");
        Assert.Equal("teal", preview.Str("themeKey"));
        Assert.DoesNotContain("gallery", preview["sections"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("Layout Draft Shop", preview.Str("name"));
        Assert.False(preview["isVerified"]!.GetValue<bool>()); // no badge before approval
        var draftSlug = preview.Str("slug");
        await factory.Anonymous().GetAsync($"/api/public/businesses/{draftSlug}").ExpectStatus(404, "business_not_found");
        var outsider = await factory.RegisterAsync("বাইরের লোক", TestData.NextPhone());
        await outsider.GetAsync($"/api/businesses/{draftId}/page/preview").ExpectStatus(404, "business_not_found");

        // Published (Pro trial): reorder works and the public page follows the owner's order and content.
        var (bizId, _, _) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Layout Shop");
        var reordered = new[] { "services", "reviews", "faqs", "highlights", "notices", "gallery", "team", "about" };
        await owner.PutAsync($"/api/businesses/{bizId}/page/layout",
            new { sections = reordered.Select(k => new { key = k, visible = true }) }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{bizId}/page/appearance", new { themeKey = "violet", headerStyle = "color" }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{bizId}/page/faqs", new[]
        {
            new { question = "কত দিনের ওয়ারেন্টি?", answer = "সার্ভিসিংয়ে ৩০ দিন।" },
            new { question = "বাসায় আসেন?", answer = "হ্যাঁ, ঢাকার ভেতরে।" },
        }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{bizId}/page/highlights", new[] { new { iconKey = "shield", title = "অভিজ্ঞ টেকনিশিয়ান", description = "১০ বছরের অভিজ্ঞতা" } })
            .ExpectStatus(200);
        var page = await owner.GetJson($"/api/businesses/{bizId}/page");
        var ownerMember = page["team"]![0]!.Id("memberId");
        await owner.PutAsync($"/api/businesses/{bizId}/page/team", new { memberIds = new[] { ownerMember } }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{bizId}/page/team", new { memberIds = new[] { Guid.NewGuid() } }).ExpectStatus(400, "member_not_found");

        var slug = (await owner.GetJson($"/api/businesses/{bizId}")).Str("slug");
        var profile = await factory.Anonymous().GetJson($"/api/public/businesses/{slug}");
        Assert.Equal(reordered, profile["sections"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
        Assert.Equal("violet", profile.Str("themeKey"));
        Assert.True(profile["isVerified"]!.GetValue<bool>());
        Assert.Equal("color", profile.Str("headerStyle"));
        Assert.Equal(["কত দিনের ওয়ারেন্টি?", "বাসায় আসেন?"], Keys(profile["faqs"], "question"));
        Assert.Equal("অভিজ্ঞ টেকনিশিয়ান", profile["highlights"]![0].Str("title"));
        Assert.Equal("লেআউট মালিক", profile["team"]![0].Str("name"));
        Assert.Equal("মালিক", profile["team"]![0].Str("roleName"));
        Assert.Null(profile["team"]![0]!["phone"]); // only name, title, photo and role are public

        // The owner's preview of a published page is the public page.
        var published = await owner.GetJson($"/api/businesses/{bizId}/page/preview");
        Assert.Equal(profile.ToJsonString(), published.ToJsonString());
    }

    [Fact]
    public async Task Uploads_are_checked_by_content_and_gallery_follows_plan_limits()
    {
        var owner = await factory.RegisterAsync("গ্যালারি মালিক", TestData.NextPhone());
        var (draftId, serviceId, _) = await TestData.CreateDraftBusinessAsync(owner, "Gallery Draft Shop");

        var upload = await UploadAsync(owner, draftId, Png).ExpectJson();
        var url = upload.Str("url");
        Assert.StartsWith($"/uploads/{draftId}/", url);
        var served = await factory.Anonymous().GetAsync(url);
        await served.Expect(200);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());

        // A script renamed to .png is refused; so is a request with no file.
        await UploadAsync(owner, draftId, "<svg onload=alert(1)>"u8.ToArray(), "evil.png").ExpectStatus(400, "unsupported_file_type");
        await owner.Client.PostAsync($"/api/businesses/{draftId}/uploads", new MultipartFormDataContent { { new StringContent("x"), "note" } })
            .ExpectStatus(400, "file_required");
        var stranger = await factory.RegisterAsync("অপরিচিত আপলোডার", TestData.NextPhone());
        await UploadAsync(stranger, draftId, Png).ExpectStatus(404, "business_not_found");

        // Free plan: 6 gallery photos. Only our uploads or https links are accepted.
        var six = Enumerable.Range(0, 6).Select(i => new { imageUrl = i == 0 ? url : $"https://img.example.com/{i}.jpg", caption = $"কাজ {i}" }).ToList();
        await owner.PutAsync($"/api/businesses/{draftId}/page/gallery", six).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{draftId}/page/gallery", six.Append(new { imageUrl = "https://img.example.com/7.jpg", caption = "সাত" }))
            .ExpectStatus(403, "plan_limit_reached");
        await owner.PutAsync($"/api/businesses/{draftId}/page/gallery", new[] { new { imageUrl = "javascript:alert(1)", caption = "x" } })
            .ExpectStatus(400, "invalid_image_url");

        // Service photo.
        var service = await owner.GetJson($"/api/businesses/{draftId}/services/{serviceId}");
        service["imageUrl"] = url;
        await owner.PutAsync($"/api/businesses/{draftId}/services/{serviceId}", service).ExpectStatus(200);
        Assert.Equal(url, (await owner.GetJson($"/api/businesses/{draftId}/services/{serviceId}")).Str("imageUrl"));

        // Removing an uploaded photo from the gallery deletes the file.
        var second = (await UploadAsync(owner, draftId, Png).ExpectJson()).Str("url");
        await owner.PutAsync($"/api/businesses/{draftId}/page/gallery", new[] { new { imageUrl = second, caption = "নতুন" } }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{draftId}/page/gallery", Array.Empty<object>()).ExpectStatus(200);
        await factory.Anonymous().GetAsync(second).ExpectStatus(404);
    }

    [Fact]
    public async Task Replaced_images_are_cleaned_up_and_reported_page_content_can_be_removed_by_moderators()
    {
        var owner = await factory.RegisterAsync("রিপোর্ট মালিক", TestData.NextPhone());
        var (bizId, serviceId, _) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Report Shop");
        var web = factory.Anonymous();
        async Task<string> Up() => (await UploadAsync(owner, bizId, Png).ExpectJson()).Str("url");

        // Replacing the logo deletes the old file; a file still used elsewhere (cover) survives gallery removal.
        var logo1 = await Up();
        var logo2 = await Up();
        await owner.PatchAsync($"/api/businesses/{bizId}", new { logoUrl = logo1 }).ExpectStatus(200);
        await owner.PatchAsync($"/api/businesses/{bizId}", new { logoUrl = logo2 }).ExpectStatus(200);
        await web.GetAsync(logo1).ExpectStatus(404);
        await web.GetAsync(logo2).ExpectStatus(200);
        await owner.PatchAsync($"/api/businesses/{bizId}", new { logoUrl = "javascript:alert(1)" }).ExpectStatus(400, "invalid_image_url");

        var shared = await Up();
        await owner.PatchAsync($"/api/businesses/{bizId}", new { coverUrl = shared }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{bizId}/page/gallery", new[] { new { imageUrl = shared, caption = "কভারও" } }).ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{bizId}/page/gallery", Array.Empty<object>()).ExpectStatus(200);
        await web.GetAsync(shared).ExpectStatus(200);

        var svc1 = await Up();
        var svc2 = await Up();
        var service = await owner.GetJson($"/api/businesses/{bizId}/services/{serviceId}");
        service["imageUrl"] = svc1;
        await owner.PutAsync($"/api/businesses/{bizId}/services/{serviceId}", service).ExpectStatus(200);
        service["imageUrl"] = svc2;
        await owner.PutAsync($"/api/businesses/{bizId}/services/{serviceId}", service).ExpectStatus(200);
        await web.GetAsync(svc1).ExpectStatus(404);

        // Website and Facebook links stay private, like the phone.
        await owner.PatchAsync($"/api/businesses/{bizId}", new { websiteUrl = "https://shop.example.com", facebookUrl = "https://facebook.com/shop" }).ExpectStatus(200);
        var slug = (await owner.GetJson($"/api/businesses/{bizId}")).Str("slug");
        var profile = await web.GetJson($"/api/public/businesses/{slug}");
        Assert.Null(profile["websiteUrl"]);
        Assert.Null(profile["facebookUrl"]);

        // A customer reports one notice and one photo.
        var notice = await owner.PostAsync($"/api/businesses/{bizId}/page/notices", Notice("আপত্তিকর নোটিশ")).ExpectJson();
        var photoUrl = await Up();
        var page = await owner.PutAsync($"/api/businesses/{bizId}/page/gallery", new[] { new { imageUrl = photoUrl, caption = "আপত্তিকর ছবি" } }).ExpectJson();
        var photoId = page["gallery"]![0].Id();

        var customer = await factory.RegisterAsync("অভিযোগকারী", TestData.NextPhone());
        object ContentReport(string type, Guid id, Guid business) => new
        {
            targetType = "business", targetBusinessId = business, reason = "inappropriate_content", description = "অশোভন", contentType = type, contentId = id,
        };
        var noticeReport = await customer.PostAsync("/api/reports", ContentReport("notice", notice.Id(), bizId)).ExpectJson();
        var photoReport = await customer.PostAsync("/api/reports", ContentReport("gallery_item", photoId, bizId)).ExpectJson();
        var (otherBiz, _, _) = await TestData.CreateDraftBusinessAsync(owner, "Other Shop");
        await customer.PostAsync("/api/reports", ContentReport("notice", notice.Id(), otherBiz)).ExpectStatus(400, "invalid_target");
        await customer.PostAsync("/api/reports", ContentReport("faq", notice.Id(), bizId)).ExpectStatus(400, "invalid_target");
        var plainReport = await customer.PostAsync("/api/reports", new { targetType = "business", targetBusinessId = bizId, reason = "spam" }).ExpectJson();

        // The moderator sees what was reported and removes it with a reason.
        var admin = await factory.AdminAsync();
        var reports = (await admin.GetJson("/api/moderation/reports?pageSize=100"))["items"]!;
        var listed = reports.FindById(noticeReport.Id())!;
        Assert.Equal("notice", listed["content"].Str("type"));
        Assert.Equal("আপত্তিকর নোটিশ", listed["content"].Str("title"));
        Assert.Equal(photoUrl, reports.FindById(photoReport.Id())!["content"].Str("imageUrl"));
        Assert.Null(reports.FindById(plainReport.Id())!["content"]);

        await admin.PostAsync($"/api/moderation/reports/{plainReport.Id()}/resolve", new { resolution = "remove_content", reason = "কোনো কনটেন্ট নেই" })
            .ExpectStatus(400, "not_a_content_report");
        await admin.PostAsync($"/api/moderation/reports/{noticeReport.Id()}/resolve", new { resolution = "remove_content", reason = "অশোভন ভাষা ব্যবহার" }).ExpectStatus(204);
        await admin.PostAsync($"/api/moderation/reports/{photoReport.Id()}/resolve", new { resolution = "remove_content", reason = "অনুপযুক্ত ছবি" }).ExpectStatus(204);

        profile = await web.GetJson($"/api/public/businesses/{slug}");
        Assert.DoesNotContain("আপত্তিকর নোটিশ", Keys(profile["notices"], "title"));
        Assert.Empty(profile["gallery"]!.AsArray());
        await web.GetAsync(photoUrl).ExpectStatus(404);
        await factory.WithDbAsync(async db =>
            Assert.Equal(2, await db.Notifications.CountAsync(n => n.BusinessId == bizId && n.Type == "business.content_removed")));
    }
}
