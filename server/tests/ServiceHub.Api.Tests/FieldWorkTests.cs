using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

/// <summary>Reschedule requests, staff day / progress / leave, saved businesses, notice alerts, review and inspection photos.</summary>
[Collection(ApiCollection.Name)]
public class FieldWorkTests(ApiFactory factory)
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01];

    private static Task<HttpResponseMessage> Upload(Infrastructure.Api api, string url, string? caption = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "p.png");
        if (caption is not null) content.Add(new StringContent(caption), "caption");
        return api.Client.PostAsync(url, content);
    }

    private async Task<(Infrastructure.Api Owner, Infrastructure.Api Customer, Guid BusinessId, Guid ServiceId, Guid BookingId)> BookedAsync(string name)
    {
        var owner = await factory.RegisterAsync($"{name} মালিক", TestData.NextPhone());
        var (businessId, serviceId, fieldId) = await TestData.CreatePublishedBusinessAsync(factory, owner, name);
        var customer = await factory.RegisterAsync($"{name} গ্রাহক", TestData.NextPhone());
        var time = await TestData.FirstFreeSlotAsync(customer, businessId, serviceId);
        var booking = await customer.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, time)).ExpectJson(201);
        return (owner, customer, businessId, serviceId, booking.Id());
    }

    [Fact]
    public async Task Customer_proposes_a_new_time_and_the_business_accepts_or_rejects_it()
    {
        var (owner, customer, businessId, serviceId, bookingId) = await BookedAsync("Reschedule Shop");
        var date = TestData.Tomorrow.ToString("yyyy-MM-dd");
        var original = (await customer.GetJson($"/api/me/bookings/{bookingId}")).Str("startAt");

        // Slots for moving don't count my own booking; pick a later one.
        var slots = await customer.GetJson($"/api/me/bookings/{bookingId}/reschedule-slots?date={date}");
        var later = slots["slots"]!.AsArray().Where(s => s!["available"]!.GetValue<bool>()).Skip(3).First()!.Str("time");
        var stranger = await factory.RegisterAsync("অন্য গ্রাহক", TestData.NextPhone());
        await stranger.PostAsync($"/api/me/bookings/{bookingId}/reschedule", new { date, time = later }).ExpectStatus(404, "booking_not_found");

        await customer.PostAsync($"/api/me/bookings/{bookingId}/reschedule", new { date, time = later, reason = "অফিসে মিটিং আছে" }).ExpectStatus(204);
        await customer.PostAsync($"/api/me/bookings/{bookingId}/reschedule", new { date, time = later }).ExpectStatus(409, "reschedule_pending");
        var seen = await owner.GetJson($"/api/businesses/{businessId}/bookings/{bookingId}");
        Assert.Equal("pending", seen["reschedule"].Str("status"));
        Assert.Equal("অফিসে মিটিং আছে", seen["reschedule"].Str("reason"));

        // Rejected: the booking stays, the customer sees why.
        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/reschedule/reject", new { reason = "ওই সময়ে টেকনিশিয়ান নেই" }).ExpectStatus(204);
        var mine = await customer.GetJson($"/api/me/bookings/{bookingId}");
        Assert.Equal("rejected", mine["reschedule"].Str("status"));
        Assert.Equal("ওই সময়ে টেকনিশিয়ান নেই", mine["reschedule"].Str("responseNote"));
        Assert.Equal(original, mine.Str("startAt"));

        // Withdrawn, then asked again and accepted: the booking moves and the old slot frees up.
        await customer.PostAsync($"/api/me/bookings/{bookingId}/reschedule", new { date, time = later }).ExpectStatus(204);
        await customer.DeleteAsync($"/api/me/bookings/{bookingId}/reschedule").ExpectStatus(204);
        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/reschedule/accept").ExpectStatus(404, "no_pending_reschedule");
        await customer.PostAsync($"/api/me/bookings/{bookingId}/reschedule", new { date, time = later }).ExpectStatus(204);
        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/reschedule/accept").ExpectStatus(204);

        mine = await customer.GetJson($"/api/me/bookings/{bookingId}");
        Assert.Equal("accepted", mine["reschedule"].Str("status"));
        Assert.NotEqual(original, mine.Str("startAt"));
        Assert.Contains(mine["history"]!.AsArray(), h => h!["reason"]?.GetValue<string>()?.StartsWith("সময় পরিবর্তন") == true);
        var day = await customer.GetJson($"/api/public/businesses/{businessId}/availability?serviceId={serviceId}&date={date}");
        Assert.False(day["slots"]!.AsArray().Single(s => s.Str("time") == later)!["available"]!.GetValue<bool>());
        await factory.WithDbAsync(async db =>
            Assert.True(await db.Notifications.AnyAsync(n => n.Type == "booking.rescheduled")));
    }

    [Fact]
    public async Task Staff_day_progress_and_leave_keep_assignments_honest()
    {
        var (owner, customer, businessId, _, bookingId) = await BookedAsync("Staff Day Shop");
        var staffPhone = TestData.NextPhone();
        var invite = await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = staffPhone, roleKey = "business_staff" }).ExpectJson();
        var staff = await factory.RegisterAsync("টেকনিশিয়ান সুমন", staffPhone);
        var memberId = (await staff.PostAsync("/api/invitations/accept", new { token = invite.Str("token") }).ExpectJson()).Id();

        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/confirm").ExpectStatus(200);
        await owner.PutAsync($"/api/businesses/{businessId}/bookings/{bookingId}/assignee", new { memberId }).ExpectStatus(200);
        var tomorrow = TestData.Tomorrow.ToString("yyyy-MM-dd");
        var myDay = await staff.GetJson($"/api/businesses/{businessId}/my-day?date={tomorrow}");
        Assert.NotNull(myDay["bookings"].FindById(bookingId));
        Assert.Empty((await staff.GetJson($"/api/businesses/{businessId}/my-day"))["bookings"]!.AsArray()); // nothing today

        // Progress is the assignee's to set, and the customer sees it.
        var outsider = await factory.RegisterAsync("বাইরের লোক", TestData.NextPhone());
        await outsider.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/progress", new { progress = "on_the_way" }).ExpectStatus(404, "business_not_found");
        await staff.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/progress", new { progress = "on_the_way" }).ExpectStatus(200);
        Assert.Equal("on_the_way", (await customer.GetJson($"/api/me/bookings/{bookingId}")).Str("progress"));
        await staff.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/progress", new { progress = "started" }).ExpectStatus(200);
        await staff.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/progress", new { progress = "on_the_way" }).ExpectStatus(409, "invalid_progress");

        // Leave: validated, approved by a manager, and it pulls the member off that day's bookings.
        await staff.PostAsync($"/api/businesses/{businessId}/leaves", new { startDate = TestData.Tomorrow.AddDays(-2).ToString("yyyy-MM-dd"), endDate = tomorrow })
            .ExpectStatus(400, "invalid_dates");
        var leave = await staff.PostAsync($"/api/businesses/{businessId}/leaves", new { startDate = tomorrow, endDate = tomorrow, reason = "পারিবারিক কাজ" }).ExpectJson();
        await staff.PostAsync($"/api/businesses/{businessId}/leaves", new { startDate = tomorrow, endDate = tomorrow }).ExpectStatus(409, "leave_overlaps");
        await staff.PostAsync($"/api/businesses/{businessId}/leaves/{leave.Id()}/approve", new { }).ExpectStatus(403);
        Assert.NotNull((await owner.GetJson($"/api/businesses/{businessId}/leaves")).FindById(leave.Id()));
        Assert.Single((await staff.GetJson($"/api/businesses/{businessId}/leaves")).AsArray()); // staff see only their own

        await owner.PostAsync($"/api/businesses/{businessId}/leaves/{leave.Id()}/approve", new { note = "ঠিক আছে" }).ExpectStatus(200);
        Assert.Null((await owner.GetJson($"/api/businesses/{businessId}/bookings/{bookingId}"))["assignedTo"]);
        await owner.PutAsync($"/api/businesses/{businessId}/bookings/{bookingId}/assignee", new { memberId }).ExpectStatus(409, "member_on_leave");
        myDay = await staff.GetJson($"/api/businesses/{businessId}/my-day?date={tomorrow}");
        Assert.Empty(myDay["bookings"]!.AsArray());
        Assert.Equal("approved", myDay["leave"].Str("status"));

        var later = TestData.Tomorrow.AddDays(7).ToString("yyyy-MM-dd");
        var second = await staff.PostAsync($"/api/businesses/{businessId}/leaves", new { startDate = later, endDate = later }).ExpectJson();
        await owner.PostAsync($"/api/businesses/{businessId}/leaves/{second.Id()}/reject", new { reason = "ওই সপ্তাহে চাপ বেশি" }).ExpectStatus(200);
        await owner.PostAsync($"/api/businesses/{businessId}/leaves/{second.Id()}/approve", new { }).ExpectStatus(409, "leave_decided");
        await staff.DeleteAsync($"/api/businesses/{businessId}/leaves/{leave.Id()}").ExpectStatus(204);
        await owner.PutAsync($"/api/businesses/{businessId}/bookings/{bookingId}/assignee", new { memberId }).ExpectStatus(200);
    }

    [Fact]
    public async Task Saved_businesses_notice_alerts_review_photos_and_inspection_photos()
    {
        var (owner, customer, businessId, _, bookingId) = await BookedAsync("Follow Shop");

        // Saved businesses page and notice alerts for followers (once per notice).
        await customer.PutAsync($"/api/me/favourites/{businessId}", new { }).ExpectStatus(204);
        var saved = await customer.GetJson("/api/me/favourites/businesses");
        Assert.NotNull(saved.FindById(businessId));
        var notice = await owner.PostAsync($"/api/businesses/{businessId}/page/notices", new { title = "নতুন অফার", body = "এই সপ্তাহে ১০% ছাড়।" }).ExpectJson();
        await owner.PutAsync($"/api/businesses/{businessId}/page/notices/{notice.Id()}", new { title = "নতুন অফার!", body = "এই সপ্তাহে ১০% ছাড়।" }).ExpectStatus(200);
        await factory.WithDbAsync(async db => Assert.Equal(1, await db.Notifications.CountAsync(n =>
            n.Type == "business.notice" && n.ActionUrl!.Contains("/b/") && n.Title.Contains("নতুন অফার"))));

        // Review photos: only ones the customer uploaded.
        var mineUrl = (await Upload(customer, "/api/me/uploads").ExpectJson()).Str("url");
        var otherUrl = (await Upload(owner, "/api/me/uploads").ExpectJson()).Str("url");
        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/confirm").ExpectStatus(200);
        await factory.WithDbAsync(async db =>
        {
            var b = await db.Bookings.FirstAsync(x => x.Id == bookingId);
            b.StartAt = DateTime.UtcNow.AddHours(-3);
            b.EndAt = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        });
        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/complete", new { }).ExpectStatus(200);
        await customer.PostAsync($"/api/me/bookings/{bookingId}/review", new { rating = 5, photoUrls = new[] { otherUrl } }).ExpectStatus(400, "invalid_image_url");
        await customer.PostAsync($"/api/me/bookings/{bookingId}/review", new { rating = 5, photoUrls = Enumerable.Repeat(mineUrl, 1).Concat(["a", "b", "c", "d"]) })
            .ExpectStatus(400, "too_many_photos");
        await customer.PostAsync($"/api/me/bookings/{bookingId}/review", new { rating = 5, comment = "চমৎকার", photoUrls = new[] { mineUrl } }).ExpectStatus(200);
        var slug = (await owner.GetJson($"/api/businesses/{businessId}")).Str("slug");
        var profile = await factory.Anonymous().GetJson($"/api/public/businesses/{slug}");
        Assert.Equal(mineUrl, profile["latestReviews"]![0]!["photos"]![0]!.GetValue<string>());
        Assert.Equal(mineUrl, (await owner.GetJson($"/api/businesses/{businessId}/reviews"))["items"]![0]!["photos"]![0]!.GetValue<string>());

        // Inspection photos: the assigned inspector adds/removes them until the report is submitted.
        var inspector = await factory.RegisterAsync("পরিদর্শক নাসির", TestData.NextPhone());
        await TestData.AssignRoleAsync(factory, inspector.UserId, "inspector", Zones.Gazipur);
        var inspectionId = Guid.Empty;
        await factory.WithDbAsync(async db =>
        {
            var request = await db.VerificationRequests.FirstAsync(v => v.BusinessId == businessId);
            var i = new Inspection
            {
                BusinessId = businessId, ZoneId = Zones.Mouchak, Type = InspectionType.VerificationVisit, VerificationRequestId = request.Id,
                InspectorId = inspector.UserId, RequestedBy = inspector.UserId, ScheduledFor = DateTime.UtcNow,
            };
            db.Inspections.Add(i);
            await db.SaveChangesAsync();
            inspectionId = i.Id;
        });
        var withPhoto = await Upload(inspector, $"/api/inspections/{inspectionId}/photos", "দোকানের সামনে").ExpectJson();
        Assert.Equal("দোকানের সামনে", withPhoto["photos"]![0].Str("caption"));
        var extra = await Upload(inspector, $"/api/inspections/{inspectionId}/photos").ExpectJson();
        var extraPhoto = extra["photos"]![1]!;
        await inspector.DeleteAsync($"/api/inspections/{inspectionId}/photos/{extraPhoto.Id()}").ExpectStatus(200);
        await factory.Anonymous().GetAsync(extraPhoto.Str("imageUrl")).ExpectStatus(404);
        await Upload(customer, $"/api/inspections/{inspectionId}/photos").ExpectStatus(404, "inspection_not_found");

        await inspector.PostAsync($"/api/inspections/{inspectionId}/submit", new { outcome = "passed", findings = "দোকান ঠিকানায় আছে, লাইসেন্স দেখা হয়েছে।" }).ExpectStatus(200);
        await Upload(inspector, $"/api/inspections/{inspectionId}/photos").ExpectStatus(409, "inspection_closed");

        var admin = await factory.AdminAsync();
        var requestId = Guid.Empty;
        await factory.WithDbAsync(async db => requestId = (await db.VerificationRequests.FirstAsync(v => v.BusinessId == businessId)).Id);
        var detail = await admin.GetJson($"/api/moderation/verifications/{requestId}");
        Assert.Single(detail["inspections"].FindById(inspectionId)!["photos"]!.AsArray());
    }
}
