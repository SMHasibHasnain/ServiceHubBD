namespace ServiceHub.Api.Features.Billing;

/// <summary>Entitlement keys. Limits: -1 = unlimited. Switches: 1 = on, 0 = off.</summary>
public static class E
{
    public const string ServicesMax = "services.max";
    public const string MembersMax = "members.max";
    public const string BookingFieldsMax = "booking_fields.max";
    public const string AnalyticsMaxDays = "analytics.max_days";
    public const string CrmNotes = "crm.notes";
    public const string AutoConfirm = "bookings.auto_confirm";
    public const string PrioritySupport = "support.priority";
    public const string GalleryMax = "gallery.max";
    public const string NoticesMax = "notices.max";
    public const string PageLayout = "page.layout";

    public const int Unlimited = -1;
}

public record PlanDef(string Key, short Id, string NameBn, string NameEn, string TaglineBn, decimal PriceMonthly, decimal PriceYearly,
    Dictionary<string, int> Entitlements);

public record FeatureDef(string Key, string LabelBn, string Kind, string? UnitBn = null);

/// <summary>
/// The plans and what they unlock. Synced into subscription_plans / plan_entitlements on startup
/// (entitlements always follow the code; prices are only set when a plan is first created, then editable by admins).
/// Paid plans never change search ranking or the verified badge — trust is not for sale.
/// </summary>
public static class PlanCatalog
{
    public const string Free = "free";
    public const string Pro = "pro";
    public const string BusinessPlan = "business";

    /// <summary>The plan a new trial runs on.</summary>
    public const string TrialPlan = Pro;

    public static readonly PlanDef[] All =
    [
        new(Free, 1, "ফ্রি", "Free", "শুরু করার জন্য যা দরকার", 0, 0, new()
        {
            [E.ServicesMax] = 5, [E.MembersMax] = 2, [E.BookingFieldsMax] = 2, [E.AnalyticsMaxDays] = 7,
            [E.CrmNotes] = 0, [E.AutoConfirm] = 0, [E.PrioritySupport] = 0,
            [E.GalleryMax] = 6, [E.NoticesMax] = 1, [E.PageLayout] = 0,
        }),
        new(Pro, 2, "প্রো", "Pro", "বাড়তে থাকা ব্যবসার জন্য", 499, 4990, new()
        {
            [E.ServicesMax] = E.Unlimited, [E.MembersMax] = 5, [E.BookingFieldsMax] = 10, [E.AnalyticsMaxDays] = 90,
            [E.CrmNotes] = 1, [E.AutoConfirm] = 1, [E.PrioritySupport] = 0,
            [E.GalleryMax] = 30, [E.NoticesMax] = 10, [E.PageLayout] = 1,
        }),
        new(BusinessPlan, 3, "বিজনেস", "Business", "বড় টিম ও একাধিক টেকনিশিয়ানের জন্য", 1499, 14990, new()
        {
            [E.ServicesMax] = E.Unlimited, [E.MembersMax] = E.Unlimited, [E.BookingFieldsMax] = E.Unlimited, [E.AnalyticsMaxDays] = 90,
            [E.CrmNotes] = 1, [E.AutoConfirm] = 1, [E.PrioritySupport] = 1,
            [E.GalleryMax] = 100, [E.NoticesMax] = E.Unlimited, [E.PageLayout] = 1,
        }),
    ];

    /// <summary>Rows of the comparison table, in display order.</summary>
    public static readonly FeatureDef[] Features =
    [
        new(E.ServicesMax, "সেবার সংখ্যা", "limit", "টি"),
        new(E.MembersMax, "টিম সদস্য (মালিক ছাড়া)", "limit", "জন"),
        new(E.BookingFieldsMax, "প্রতি সেবায় বুকিং ফর্মের প্রশ্ন", "limit", "টি"),
        new(E.AnalyticsMaxDays, "বিশ্লেষণের সময়সীমা", "limit", "দিন"),
        new(E.CrmNotes, "গ্রাহকের নোট, VIP ও ট্যাগ", "switch"),
        new(E.AutoConfirm, "বুকিং স্বয়ংক্রিয় নিশ্চিতকরণ", "switch"),
        new(E.GalleryMax, "গ্যালারির ছবি", "limit", "টি"),
        new(E.NoticesMax, "নোটিশ বোর্ডে চালু নোটিশ", "limit", "টি"),
        new(E.PageLayout, "পাবলিক পেজের সেকশন সাজানো", "switch"),
        new(E.PrioritySupport, "অগ্রাধিকার সাপোর্ট", "switch"),
    ];

    public static PlanDef Get(string key) => All.FirstOrDefault(p => p.Key == key) ?? throw new KeyNotFoundException(key);
}

public class BillingOptions
{
    public const string Section = "Billing";

    public int TrialDays { get; set; } = 14;
    public int GraceDays { get; set; } = 7;
    public int ReminderDays { get; set; } = 3;
    public bool RunMaintenance { get; set; } = true;
    public int MaintenanceIntervalMinutes { get; set; } = 60;

    /// <summary>Where businesses send money for manual verification. Shown on the billing page.</summary>
    public List<PaymentAccount> PaymentAccounts { get; set; } = [];
}

public class PaymentAccount
{
    public string Method { get; set; } = "";      // bkash | nagad | bank_transfer
    public string Label { get; set; } = "";
    public string Number { get; set; } = "";
    public string? AccountName { get; set; }
    public string? Instructions { get; set; }
}
