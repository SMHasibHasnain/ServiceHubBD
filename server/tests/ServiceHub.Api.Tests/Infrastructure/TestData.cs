using System.Text.Json.Nodes;
using ServiceHub.Api.Common;

namespace ServiceHub.Api.Tests.Infrastructure;

public static class Zones
{
    public const int DhakaDivision = 1;
    public const int ChattogramDivision = 2;
    public const int DhakaDistrict = 101;
    public const int Gazipur = 102;
    public const int ChattogramDistrict = 201;
    public const int Kaliakair = 10202;
    public const int Mouchak = 1020201;
    public const int Dhanmondi = 10101;
}

public static class Categories
{
    public const int Electrical = 1;
    public const int AcRepair = 5;
    public const int Repair = 4;
}

public static class TestData
{
    private static int _phone = Random.Shared.Next(1_000_000, 8_000_000);

    /// <summary>A fresh, valid Bangladeshi mobile number.</summary>
    public static string NextPhone() => "0181" + Interlocked.Increment(ref _phone).ToString("D7");

    public static DateOnly Tomorrow => BdTime.Today().AddDays(1);

    /// <summary>Creates a draft business with hours (every day 09:00–18:00) and one service with a required select field.</summary>
    public static async Task<(Guid BusinessId, Guid ServiceId, Guid FieldId)> CreateDraftBusinessAsync(
        Api owner, string name = "কুল এয়ার এসি সার্ভিস", int zoneId = Zones.Mouchak)
    {
        var biz = await owner.PostAsync("/api/businesses", new
        {
            name, categoryId = Categories.AcRepair, shortDescription = "সব ব্র্যান্ডের এসি সার্ভিসিং",
            zoneId, addressLine = "বাসা ১২, রোড ৫", offersHomeService = true,
        }).ExpectJson(201);
        var businessId = biz.Id();

        var hours = Enumerable.Range(0, 7).Select(d => new { dayOfWeek = d, opensAt = "09:00:00", closesAt = "18:00:00" }).ToArray();
        await owner.PutAsync($"/api/businesses/{businessId}/hours", hours).ExpectStatus(200);

        var service = await owner.PostAsync($"/api/businesses/{businessId}/services", new
        {
            name = "AC servicing", categoryId = Categories.AcRepair, priceType = "fixed", price = 1500, durationMinutes = 60,
            bookingFields = new[]
            {
                new { label = "এসির ব্র্যান্ড", fieldType = "select", options = new[] { "গ্রী", "জেনারেল", "স্যামসাং" }, isRequired = true, sortOrder = 0 },
            },
        }).ExpectJson(201);
        return (businessId, service.Id(), service["bookingFields"]![0].Id());
    }

    public static Task SubmitVerificationAsync(Api owner, Guid businessId) =>
        owner.PostAsync($"/api/businesses/{businessId}/verification", new
        {
            documents = new[] { new { documentType = "trade_license", fileUrl = "https://files.example.com/license.pdf" } },
        }).ExpectStatus(200);

    /// <summary>Full path to a published business, approved by the (global) super admin acting as moderator.</summary>
    public static async Task<(Guid BusinessId, Guid ServiceId, Guid FieldId)> CreatePublishedBusinessAsync(
        ApiFactory factory, Api owner, string name = "কুল এয়ার এসি সার্ভিস", int zoneId = Zones.Mouchak)
    {
        var ids = await CreateDraftBusinessAsync(owner, name, zoneId);
        await SubmitVerificationAsync(owner, ids.BusinessId);
        var admin = await factory.AdminAsync();
        var queue = await admin.GetJson("/api/moderation/verifications?pageSize=100");
        var requestId = queue["items"]!.AsArray().First(i => i!.Id("businessId") == ids.BusinessId)!.Id();
        await admin.PostAsync($"/api/moderation/verifications/{requestId}/start").ExpectStatus(204);
        await admin.PostAsync($"/api/moderation/verifications/{requestId}/approve", new { reason = "ডকুমেন্ট সঠিক পাওয়া গেছে" }).ExpectStatus(204);
        return ids;
    }

    /// <summary>First available slot tomorrow for the service.</summary>
    public static async Task<string> FirstFreeSlotAsync(Api api, Guid businessId, Guid serviceId, int skip = 0)
    {
        var day = await api.GetJson($"/api/public/businesses/{businessId}/availability?serviceId={serviceId}&date={Tomorrow:yyyy-MM-dd}");
        return day["slots"]!.AsArray().Where(s => s!["available"]!.GetValue<bool>()).Skip(skip).First()!.Str("time");
    }

    public static object BookingBody(Guid serviceId, Guid fieldId, string time, string? clientRequestId = null) => new
    {
        serviceId, date = Tomorrow.ToString("yyyy-MM-dd"), time, serviceAddress = "সেক্টর ৭, উত্তরা",
        answers = new[] { new { fieldId, value = "গ্রী" } }, clientRequestId,
    };

    public static async Task AssignRoleAsync(ApiFactory factory, Guid userId, string roleKey, int? zoneId = null, int? categoryId = null)
    {
        var admin = await factory.AdminAsync();
        await admin.PostAsync("/api/admin/role-assignments", new { userId, roleKey, zoneId, categoryId }).ExpectStatus(200);
    }

    public static JsonNode? FindById(this JsonNode? list, Guid id, string key = "id") =>
        list!.AsArray().FirstOrDefault(x => x!.Id(key) == id);
}
