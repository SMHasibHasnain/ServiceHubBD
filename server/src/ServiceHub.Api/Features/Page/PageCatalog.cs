using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Page;

/// <summary>
/// What a business may customise on its public page. Trust signals (verified badge, rating, reviews,
/// the booking button and "report") are not in here: they always render the same way on every page.
/// </summary>
public static class PageCatalog
{
    public const string Notices = "notices";
    public const string Highlights = "highlights";
    public const string Services = "services";
    public const string Gallery = "gallery";
    public const string Reviews = "reviews";
    public const string Faqs = "faqs";
    public const string Team = "team";
    public const string About = "about";

    /// <summary>Default order of the movable sections.</summary>
    public static readonly string[] DefaultOrder = [Notices, Highlights, Services, Gallery, Reviews, Faqs, Team, About];

    /// <summary>Sections an owner may hide. Services, reviews and about (hours, address, report) always show.</summary>
    public static readonly HashSet<string> Hideable = [Notices, Highlights, Gallery, Faqs, Team];

    /// <summary>Accent colours, each checked for AA contrast against white text and on a white page.</summary>
    public static readonly HashSet<string> Themes = ["blue", "navy", "teal", "green", "violet", "rose", "orange", "slate"];

    /// <summary>"cover": cover photo header. "color": a gradient band in the accent colour (for pages without a good photo).</summary>
    public static readonly HashSet<string> HeaderStyles = ["cover", "color"];

    public static readonly HashSet<string> HighlightIcons =
        ["shield", "clock", "award", "wallet", "users", "wrench", "sparkles", "heart", "truck", "thumbs-up", "badge-percent", "leaf"];

    /// <summary>Page items a customer can report on their own (<c>reports.target_content_type</c>).</summary>
    public const string ContentNotice = "notice";
    public const string ContentGalleryItem = "gallery_item";

    public const int MaxFaqs = 20;
    public const int MaxHighlights = 6;

    /// <summary>Saved layout cleaned up: unknown or duplicate keys dropped, missing sections appended, required ones forced visible.</summary>
    public static List<PageSection> Normalize(IEnumerable<PageSection>? saved)
    {
        var result = new List<PageSection>();
        foreach (var s in saved ?? [])
            if (DefaultOrder.Contains(s.Key) && result.All(r => r.Key != s.Key))
                result.Add(new PageSection(s.Key, s.Visible || !Hideable.Contains(s.Key)));
        foreach (var key in DefaultOrder)
            if (result.All(r => r.Key != key)) result.Add(new PageSection(key, true));
        return result;
    }

    public static bool IsVisible(List<PageSection> layout, string key) => layout.First(s => s.Key == key).Visible;
}
