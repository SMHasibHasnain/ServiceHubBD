using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

/// <summary>Customer books → business confirms/assigns → staff completes → customer reviews → CRM updated.</summary>
[Collection(ApiCollection.Name)]
public class BookingFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Full_booking_lifecycle_with_team_crm_and_review()
    {
        var owner = await factory.RegisterAsync("মালিক রাশেদ", TestData.NextPhone());
        var (businessId, serviceId, fieldId) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Rashed AC Care");
        var customer = await factory.RegisterAsync("গ্রাহক ফারহানা", TestData.NextPhone());
        var other = await factory.RegisterAsync("অন্য গ্রাহক", TestData.NextPhone());

        // Public profile + availability.
        var profile = await factory.Anonymous().GetJson("/api/public/businesses/" + (await owner.GetJson($"/api/businesses/{businessId}")).Str("slug"));
        Assert.Single(profile["services"]!.AsArray());
        Assert.Null(profile["services"]![0]!["internalNotes"]);
        var time = await TestData.FirstFreeSlotAsync(customer, businessId, serviceId);

        // Required dynamic field is enforced.
        await customer.PostAsync("/api/bookings", new { serviceId, date = TestData.Tomorrow.ToString("yyyy-MM-dd"), time })
            .ExpectStatus(400, "field_required");

        var booking = await customer.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, time, "req-1")).ExpectJson(201);
        var bookingId = booking.Id();
        Assert.Equal("pending", booking.Str("status"));
        Assert.Matches(@"^SH-\d{2}-\d{6}$", booking.Str("bookingNumber"));
        Assert.Equal("গ্রী", booking["answers"]![0].Str("value"));

        // Same idempotency key → same booking, not a duplicate.
        var again = await customer.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, time, "req-1")).ExpectJson(201);
        Assert.Equal(bookingId, again.Id());

        // Capacity is 1: the slot is now taken for everyone else.
        await other.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, time)).ExpectStatus(409, "slot_taken");
        var day = await other.GetJson($"/api/public/businesses/{businessId}/availability?serviceId={serviceId}&date={TestData.Tomorrow:yyyy-MM-dd}");
        Assert.False(day["slots"]!.AsArray().Single(s => s.Str("time") == time)!["available"]!.GetValue<bool>());

        // Owners can't book their own business.
        await owner.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, await TestData.FirstFreeSlotAsync(owner, businessId, serviceId)))
            .ExpectStatus(400, "own_business");

        // Owner got a business-workspace notification.
        var ownerWs = await owner.GetJson("/api/me/workspaces");
        Assert.True(ownerWs.AsArray().Single(w => w!["businessId"]?.GetValue<string>() == businessId.ToString())!["unreadNotifications"]!.GetValue<int>() >= 1);

        // Invite a staff member by phone; they register and accept.
        var staffPhone = TestData.NextPhone();
        var invite = await owner.PostAsync($"/api/businesses/{businessId}/invitations", new { phone = staffPhone, roleKey = "business_staff" }).ExpectJson();
        var staff = await factory.RegisterAsync("টেকনিশিয়ান রাকিব", staffPhone);
        var wrongUser = await factory.RegisterAsync("ভুল ব্যক্তি", TestData.NextPhone());
        await wrongUser.PostAsync("/api/invitations/accept", new { token = invite.Str("token") }).ExpectStatus(403, "invitation_mismatch");
        var member = await staff.PostAsync("/api/invitations/accept", new { token = invite.Str("token") }).ExpectJson();
        var memberId = member.Id();

        // Staff has a business workspace but limited permissions.
        var staffWs = await staff.GetJson("/api/me/workspaces");
        Assert.Contains(staffWs.AsArray(), w => w.Str("type") == "business" && w.Str("roleKey") == "business_staff");
        await staff.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/confirm").ExpectStatus(403);
        await staff.PostAsync($"/api/businesses/{businessId}/services", new { name = "Staff service", categoryId = 5, priceType = "fixed", price = 100, durationMinutes = 30 })
            .ExpectStatus(403);
        var staffList = await staff.GetJson($"/api/businesses/{businessId}/bookings");
        Assert.Empty(staffList["items"]!.AsArray()); // not assigned yet

        // Owner confirms and assigns to the staff member.
        var confirmed = await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/confirm").ExpectJson();
        Assert.Equal("confirmed", confirmed.Str("status"));
        await owner.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/confirm").ExpectStatus(409, "invalid_transition");
        await owner.PutAsync($"/api/businesses/{businessId}/bookings/{bookingId}/assignee", new { memberId }).ExpectStatus(200);
        staffList = await staff.GetJson($"/api/businesses/{businessId}/bookings");
        Assert.NotNull(staffList["items"].FindById(bookingId));

        // Can't complete before the appointment time.
        await staff.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/complete", new { }).ExpectStatus(409, "too_early");
        await customer.PostAsync($"/api/me/bookings/{bookingId}/review", new { rating = 5 }).ExpectStatus(409, "booking_not_completed");

        // Move the appointment into the past, then the assigned staff completes it.
        await factory.WithDbAsync(async db =>
        {
            var b = await db.Bookings.FirstAsync(x => x.Id == bookingId);
            b.StartAt = DateTime.UtcNow.AddHours(-3);
            b.EndAt = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        });
        var done = await staff.PostAsync($"/api/businesses/{businessId}/bookings/{bookingId}/complete", new { finalPrice = 1800 }).ExpectJson();
        Assert.Equal("completed", done.Str("status"));
        Assert.Equal(1800m, done["finalPrice"]!.GetValue<decimal>());
        Assert.Equal(
            ["pending", "confirmed", "completed"],
            done["history"]!.AsArray().Select(h => h.Str("to")).ToArray());

        // Customer reviews exactly once; ratings roll up.
        await customer.PostAsync($"/api/me/bookings/{bookingId}/review", new { rating = 4, comment = "ভালো কাজ, সময়মতো এসেছেন।" }).ExpectStatus(200);
        await customer.PostAsync($"/api/me/bookings/{bookingId}/review", new { rating = 5 }).ExpectStatus(409, "already_reviewed");
        var reviews = await owner.GetJson($"/api/businesses/{businessId}/reviews");
        var reviewId = reviews["items"]![0].Id();
        await owner.PutAsync($"/api/businesses/{businessId}/reviews/{reviewId}/reply", new { body = "ধন্যবাদ আপনাকে!" }).ExpectStatus(204);
        await staff.PutAsync($"/api/businesses/{businessId}/reviews/{reviewId}/reply", new { body = "staff can't reply" }).ExpectStatus(403);

        var slug = (await owner.GetJson($"/api/businesses/{businessId}")).Str("slug");
        var publicProfile = await factory.Anonymous().GetJson("/api/public/businesses/" + slug);
        Assert.Equal(4.0m, publicProfile["avgRating"]!.GetValue<decimal>());
        Assert.Equal(1, publicProfile["reviewCount"]!.GetValue<int>());
        Assert.Equal(1, publicProfile["completedBookingCount"]!.GetValue<int>());
        Assert.Equal("ধন্যবাদ আপনাকে!", publicProfile["latestReviews"]![0].Str("replyBody"));

        // CRM: the customer appears once with up-to-date counters; staff can add private notes.
        var customers = await owner.GetJson($"/api/businesses/{businessId}/customers");
        var crm = customers["items"]!.AsArray().Single(c => c!["userId"]?.GetValue<string>() == customer.UserId.ToString())!;
        Assert.Equal(1, crm["completedBookings"]!.GetValue<int>());
        Assert.Equal(1800m, crm["lifetimeValue"]!.GetValue<decimal>());
        Assert.Equal("AC servicing", crm.Str("favouriteService"));
        await staff.PostAsync($"/api/businesses/{businessId}/customers/{crm.Id()}/notes", new { body = "সকালে আসতে পছন্দ করেন" }).ExpectStatus(200);
        await staff.PatchAsync($"/api/businesses/{businessId}/customers/{crm.Id()}", new { isVip = true }).ExpectStatus(403);
        await owner.PatchAsync($"/api/businesses/{businessId}/customers/{crm.Id()}", new { isVip = true }).ExpectStatus(200);
        var detail = await owner.GetJson($"/api/businesses/{businessId}/customers/{crm.Id()}");
        Assert.Contains("vip", detail["customer"]!["badges"]!.AsArray().Select(b => b!.GetValue<string>()));
        Assert.Single(detail["notes"]!.AsArray());

        // Customer side views.
        var past = await customer.GetJson("/api/me/bookings?scope=past");
        Assert.True(past["items"].FindById(bookingId)!["hasReview"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Customer_cancellation_needs_a_reason_and_frees_the_slot()
    {
        var owner = await factory.RegisterAsync("মালিক সাবরিনা", TestData.NextPhone());
        var (businessId, serviceId, fieldId) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Sabrina Cooling");
        var customer = await factory.RegisterAsync("গ্রাহক তানভীর", TestData.NextPhone());
        var time = await TestData.FirstFreeSlotAsync(customer, businessId, serviceId);
        var booking = await customer.PostAsync("/api/bookings", TestData.BookingBody(serviceId, fieldId, time)).ExpectJson(201);

        await customer.PostAsync($"/api/me/bookings/{booking.Id()}/cancel", new { reason = "" }).ExpectStatus(400);
        var cancelled = await customer.PostAsync($"/api/me/bookings/{booking.Id()}/cancel", new { reason = "পরিকল্পনা পরিবর্তন হয়েছে" }).ExpectJson();
        Assert.Equal("cancelled", cancelled.Str("status"));

        var day = await customer.GetJson($"/api/public/businesses/{businessId}/availability?serviceId={serviceId}&date={TestData.Tomorrow:yyyy-MM-dd}");
        Assert.True(day["slots"]!.AsArray().Single(s => s.Str("time") == time)!["available"]!.GetValue<bool>());

        var cancelledList = await customer.GetJson("/api/me/bookings?scope=cancelled");
        Assert.NotNull(cancelledList["items"].FindById(booking.Id()));
    }

    [Fact]
    public async Task Closures_and_weekly_hours_shape_availability()
    {
        var owner = await factory.RegisterAsync("মালিক জামাল", TestData.NextPhone());
        var (businessId, serviceId, _) = await TestData.CreatePublishedBusinessAsync(factory, owner, "Jamal Fix");
        await owner.PostAsync($"/api/businesses/{businessId}/closures", new { closedOn = TestData.Tomorrow.ToString("yyyy-MM-dd"), reason = "ঈদের ছুটি" })
            .ExpectStatus(200);
        var day = await factory.Anonymous().GetJson($"/api/public/businesses/{businessId}/availability?serviceId={serviceId}&date={TestData.Tomorrow:yyyy-MM-dd}");
        Assert.False(day["isOpen"]!.GetValue<bool>());
        Assert.Equal("ঈদের ছুটি", day.Str("closedReason"));

        await owner.PutAsync($"/api/businesses/{businessId}/hours", new[]
        {
            new { dayOfWeek = 0, opensAt = "09:00:00", closesAt = "13:00:00" },
            new { dayOfWeek = 0, opensAt = "12:00:00", closesAt = "18:00:00" },
        }).ExpectStatus(400, "overlapping_hours");
    }
}
