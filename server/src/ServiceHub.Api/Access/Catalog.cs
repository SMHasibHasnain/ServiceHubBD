using ServiceHub.Api.Domain;

namespace ServiceHub.Api.Access;

/// <summary>Permission keys. Seeded into the permissions table on startup (see DataSeeder).</summary>
public static class P
{
    // ----- business scope -----
    public const string BusinessView = "business.view";
    public const string BusinessUpdate = "business.update";
    public const string BusinessDelete = "business.delete";
    public const string VerificationSubmit = "business.verification.submit";
    public const string MembersView = "members.view";
    public const string MembersManage = "members.manage";
    public const string ServicesManage = "services.manage";
    public const string BookingsViewAll = "bookings.view_all";
    public const string BookingsViewAssigned = "bookings.view_assigned";
    public const string BookingsManage = "bookings.manage";
    public const string BookingsComplete = "bookings.complete";
    public const string CustomersView = "customers.view";
    public const string CustomersNotes = "customers.notes";
    public const string CustomersManage = "customers.manage";
    public const string ReviewsReply = "reviews.reply";
    public const string ReviewsReport = "reviews.report";
    public const string AnalyticsView = "analytics.view";
    public const string BillingManage = "billing.manage";

    // ----- platform / zone scope -----
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesAssign = "roles.assign";
    public const string RolesAssignAdmin = "roles.assign_admin";
    public const string CategoriesManage = "categories.manage";
    public const string ZonesManage = "zones.manage";
    public const string BusinessesSuspend = "businesses.suspend";
    public const string VerificationReview = "verification.review";
    public const string ReportsReview = "reports.review";
    public const string ReviewsModerate = "reviews.moderate";
    public const string InspectionsRequest = "inspections.request";
    public const string InspectionsPerform = "inspections.perform";
    public const string CampaignsManage = "campaigns.manage";
    public const string AuditView = "audit.view";
    public const string SubscriptionsManage = "subscriptions.manage";
    public const string SiteContentManage = "site_content.manage";

    public static readonly (string Key, RoleScope Scope, string Module, string Description)[] All =
    [
        (BusinessView, RoleScope.Business, "business", "ব্যবসার ওয়ার্কস্পেস দেখা"),
        (BusinessUpdate, RoleScope.Business, "business", "ব্যবসার তথ্য ও সময়সূচি সম্পাদনা"),
        (BusinessDelete, RoleScope.Business, "business", "ব্যবসা মুছে ফেলা"),
        (VerificationSubmit, RoleScope.Business, "business", "যাচাইকরণের আবেদন জমা"),
        (MembersView, RoleScope.Business, "team", "টিম সদস্য দেখা"),
        (MembersManage, RoleScope.Business, "team", "সদস্য আমন্ত্রণ, ভূমিকা পরিবর্তন ও অপসারণ"),
        (ServicesManage, RoleScope.Business, "services", "সেবা তৈরি ও সম্পাদনা"),
        (BookingsViewAll, RoleScope.Business, "bookings", "সব অ্যাপয়েন্টমেন্ট দেখা"),
        (BookingsViewAssigned, RoleScope.Business, "bookings", "নিজের অ্যাপয়েন্টমেন্ট দেখা"),
        (BookingsManage, RoleScope.Business, "bookings", "নিশ্চিত, প্রত্যাখ্যান, বাতিল ও বরাদ্দ"),
        (BookingsComplete, RoleScope.Business, "bookings", "অ্যাপয়েন্টমেন্ট সম্পন্ন চিহ্নিত"),
        (CustomersView, RoleScope.Business, "crm", "গ্রাহক দেখা"),
        (CustomersNotes, RoleScope.Business, "crm", "গ্রাহক নোট লেখা"),
        (CustomersManage, RoleScope.Business, "crm", "VIP ও ট্যাগ ব্যবস্থাপনা"),
        (ReviewsReply, RoleScope.Business, "reviews", "রিভিউর উত্তর"),
        (ReviewsReport, RoleScope.Business, "reviews", "রিভিউ রিপোর্ট"),
        (AnalyticsView, RoleScope.Business, "analytics", "বিশ্লেষণ দেখা"),
        (BillingManage, RoleScope.Business, "billing", "সাবস্ক্রিপশন ও পেমেন্ট ব্যবস্থাপনা"),

        (UsersView, RoleScope.Platform, "users", "ব্যবহারকারী দেখা"),
        (UsersManage, RoleScope.Platform, "users", "ব্যবহারকারী স্থগিত/সক্রিয়"),
        (RolesAssign, RoleScope.Platform, "access", "মডারেটর, ইন্সপেক্টর, মার্কেটার ভূমিকা প্রদান"),
        (RolesAssignAdmin, RoleScope.Platform, "access", "এ্যাডমিন ভূমিকা প্রদান"),
        (CategoriesManage, RoleScope.Platform, "taxonomy", "ক্যাটাগরি ব্যবস্থাপনা"),
        (ZonesManage, RoleScope.Platform, "taxonomy", "এলাকা ব্যবস্থাপনা"),
        (BusinessesSuspend, RoleScope.Platform, "moderation", "ব্যবসা স্থগিত/পুনর্বহাল"),
        (VerificationReview, RoleScope.Platform, "moderation", "যাচাইকরণ সিদ্ধান্ত"),
        (ReportsReview, RoleScope.Platform, "moderation", "রিপোর্ট পর্যালোচনা"),
        (ReviewsModerate, RoleScope.Platform, "moderation", "রিভিউ মডারেশন"),
        (InspectionsRequest, RoleScope.Platform, "inspection", "মাঠ পরিদর্শনের অনুরোধ"),
        (InspectionsPerform, RoleScope.Zone, "inspection", "জোনে পরিদর্শন সম্পাদন"),
        (CampaignsManage, RoleScope.Platform, "marketing", "ক্যাম্পেইন ব্যবস্থাপনা"),
        (AuditView, RoleScope.Platform, "admin", "অডিট লগ দেখা"),
        (SubscriptionsManage, RoleScope.Platform, "billing", "সাবস্ক্রিপশন, পেমেন্ট যাচাই ও প্ল্যান প্রদান"),
        (SiteContentManage, RoleScope.Platform, "admin", "হোমপেজের লেখা, সেকশন ও সাইটের হেডার-ফুটার সম্পাদনা"),
    ];
}

/// <summary>Seeded system roles. Ownership is a business role, not an account type.</summary>
public static class R
{
    public const string BusinessOwner = "business_owner";
    public const string BusinessManager = "business_manager";
    public const string BusinessStaff = "business_staff";
    public const string PageModerator = "page_moderator";
    public const string BusinessAnalyst = "business_analyst";

    public const string SuperAdmin = "super_admin";
    public const string Admin = "admin";
    public const string SystemModerator = "system_moderator";
    public const string Inspector = "inspector";
    public const string Marketer = "marketer";
    public const string SupportAgent = "support_agent";

    public record RoleDef(string Key, RoleScope Scope, string NameBn, string NameEn, short Rank,
        WorkspaceType Workspace, string[] Permissions);

    private static readonly string[] AllPlatform = P.All.Where(p => p.Scope == RoleScope.Platform).Select(p => p.Key).ToArray();

    public static readonly RoleDef[] All =
    [
        new(BusinessOwner, RoleScope.Business, "মালিক", "Owner", 100, WorkspaceType.Business,
            P.All.Where(p => p.Scope == RoleScope.Business).Select(p => p.Key).ToArray()),
        new(BusinessManager, RoleScope.Business, "ম্যানেজার", "Manager", 80, WorkspaceType.Business,
            [P.BusinessView, P.BusinessUpdate, P.MembersView, P.MembersManage, P.ServicesManage, P.BookingsViewAll,
             P.BookingsManage, P.BookingsComplete, P.CustomersView, P.CustomersNotes, P.CustomersManage,
             P.ReviewsReply, P.ReviewsReport, P.AnalyticsView]),
        new(PageModerator, RoleScope.Business, "পেজ মডারেটর", "Page moderator", 40, WorkspaceType.Business,
            [P.BusinessView, P.ReviewsReply, P.ReviewsReport, P.BookingsViewAll]),
        new(BusinessStaff, RoleScope.Business, "স্টাফ", "Staff", 30, WorkspaceType.Business,
            [P.BusinessView, P.BookingsViewAssigned, P.BookingsComplete, P.CustomersView, P.CustomersNotes]),
        new(BusinessAnalyst, RoleScope.Business, "বিশ্লেষক", "Analyst", 20, WorkspaceType.Business,
            [P.BusinessView, P.BookingsViewAll, P.CustomersView, P.AnalyticsView]),

        new(SuperAdmin, RoleScope.Platform, "সুপার এ্যাডমিন", "Super admin", 100, WorkspaceType.Administration, AllPlatform),
        new(Admin, RoleScope.Platform, "এ্যাডমিন", "Admin", 90, WorkspaceType.Administration,
            AllPlatform.Where(k => k != P.RolesAssignAdmin && k != P.SiteContentManage).ToArray()),
        new(SystemModerator, RoleScope.Platform, "সিস্টেম মডারেটর", "System moderator", 50, WorkspaceType.Moderation,
            [P.VerificationReview, P.ReportsReview, P.ReviewsModerate, P.BusinessesSuspend, P.InspectionsRequest]),
        new(Inspector, RoleScope.Zone, "ইন্সপেক্টর", "Inspector", 40, WorkspaceType.Inspection,
            [P.InspectionsPerform]),
        new(Marketer, RoleScope.Platform, "মার্কেটার", "Marketer", 40, WorkspaceType.Marketing,
            [P.CampaignsManage]),
        new(SupportAgent, RoleScope.Platform, "সাপোর্ট এজেন্ট", "Support agent", 30, WorkspaceType.Administration,
            [P.UsersView]),
    ];

    public static RoleDef Get(string key) => All.Single(r => r.Key == key);
}
