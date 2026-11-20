using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Features.Page;
using ServiceHub.Api.Features.Services;

namespace ServiceHub.Api.Features.Businesses;

/// <summary>
/// Builds the public profile exactly as customers see it. Used by the public endpoint (published pages only)
/// and by the owner's preview (any status), so the preview can never drift from the real page.
/// Website and Facebook links are deliberately left out, like the phone: booking and contact stay on the platform.
/// </summary>
public class PublicProfileBuilder(AppDbContext db)
{
    public async Task<PublicBusinessDto?> BuildAsync(Expression<Func<Domain.Entities.Business, bool>> where)
    {
        var b = await db.Businesses.AsNoTracking()
            .Include(x => x.Category).Include(x => x.Zone).Include(x => x.Hours).Include(x => x.BookingSettings)
            .Include(x => x.Services.Where(s => s.Status == RecordStatus.Active)).ThenInclude(s => s.BookingFields)
            .FirstOrDefaultAsync(where);
        if (b is null) return null;

        var distribution = await db.Reviews.Where(r => r.BusinessId == b.Id && r.Status == ReviewStatus.Published)
            .GroupBy(r => r.Rating).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        for (short s = 1; s <= 5; s++) distribution.TryAdd(s, 0);

        var page = await LoadPageAsync(b);
        var services = b.Services.OrderByDescending(s => s.IsFeatured).ThenBy(s => s.FeaturedOrder).ThenBy(s => s.Name)
            .Select(s => ServicesController.ToDto(s, includeInternal: false)).ToList();

        return new PublicBusinessDto(b.Id, b.Name, b.Slug, b.Tagline, b.ShortDescription, b.Description, b.LogoUrl,
            b.CoverUrl, b.CategoryId, b.Category.NameBn, b.AddressLine, b.Zone?.NameBn, b.CoverageNote, b.Languages,
            b.EstablishedYear, b.OffersHomeService, b.OffersEmergencyService, b.AvgRating, b.ReviewCount,
            b.CompletedBookingCount, b.AvgResponseMinutes, b.BookingSettings?.CancellationPolicy,
            b.Hours.OrderBy(h => h.DayOfWeek).ThenBy(h => h.OpensAt).Select(h => new PublicHourDto(h.DayOfWeek, h.OpensAt, h.ClosesAt)).ToList(),
            services, distribution.OrderByDescending(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value),
            await ReviewsQuery(b.Id).Take(5).ToListAsync(),
            b.ThemeKey ?? "blue", b.HeaderStyle ?? "cover", page.Sections, page.Notices, page.Faqs, page.Highlights, page.Gallery, page.Team,
            b.VerificationStatus == VerificationStatus.Approved);
    }

    private record PageContent(List<string> Sections, List<PublicNoticeDto> Notices, List<FaqDto> Faqs,
        List<HighlightDto> Highlights, List<GalleryItemDto> Gallery, List<PublicTeamMemberDto> Team);

    /// <summary>Owner-controlled page content. Hidden sections are not loaded at all, so they can't leak.</summary>
    private async Task<PageContent> LoadPageAsync(Domain.Entities.Business b)
    {
        var layout = PageCatalog.Normalize(b.PageLayout);
        bool Show(string key) => PageCatalog.IsVisible(layout, key);
        var now = DateTime.UtcNow;

        var notices = Show(PageCatalog.Notices)
            ? await db.BusinessNotices.AsNoTracking()
                .Where(n => n.BusinessId == b.Id && n.IsPublished && (n.ExpiresAt == null || n.ExpiresAt > now))
                .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt).Take(20)
                .Select(n => new PublicNoticeDto(n.Id, n.Title, n.Body, n.IsPinned, n.CreatedAt, n.ExpiresAt)).ToListAsync()
            : [];
        var faqs = Show(PageCatalog.Faqs)
            ? await db.BusinessFaqs.AsNoTracking().Where(f => f.BusinessId == b.Id).OrderBy(f => f.SortOrder)
                .Select(f => new FaqDto(f.Id, f.Question, f.Answer)).ToListAsync()
            : [];
        var highlights = Show(PageCatalog.Highlights)
            ? await db.BusinessHighlights.AsNoTracking().Where(h => h.BusinessId == b.Id).OrderBy(h => h.SortOrder)
                .Select(h => new HighlightDto(h.Id, h.IconKey, h.Title, h.Description)).ToListAsync()
            : [];
        var gallery = Show(PageCatalog.Gallery)
            ? await db.GalleryItems.AsNoTracking().Where(g => g.BusinessId == b.Id).OrderBy(g => g.SortOrder)
                .Select(g => new GalleryItemDto(g.Id, g.ImageUrl, g.Caption)).ToListAsync()
            : [];
        var team = Show(PageCatalog.Team)
            ? await db.BusinessMembers.AsNoTracking()
                .Where(m => m.BusinessId == b.Id && m.Status == MemberStatus.Active && m.ShowOnProfile)
                .OrderByDescending(m => m.Role.Rank).ThenBy(m => m.JoinedAt)
                .Select(m => new PublicTeamMemberDto(m.User.FullName, m.Title, m.User.AvatarUrl, m.Role.NameBn)).ToListAsync()
            : [];

        // Only visible sections are listed, in the owner's order; the client also skips empty ones.
        var sections = layout.Where(s => s.Visible).Select(s => s.Key).ToList();
        return new PageContent(sections, notices, faqs, highlights, gallery, team);
    }

    public IQueryable<ReviewDto> ReviewsQuery(Guid businessId, short? rating = null) =>
        db.Reviews.AsNoTracking()
            .Where(r => r.BusinessId == businessId && r.Status == ReviewStatus.Published && (rating == null || r.Rating == rating))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewDto(r.Id, r.Customer.FullName, r.Booking.ServiceName, r.Rating, r.Comment, r.CreatedAt,
                r.Reply != null ? r.Reply.Body : null, r.Reply != null ? r.Reply.CreatedAt : null,
                r.Photos.OrderBy(p => p.SortOrder).Select(p => p.ImageUrl).ToList()));
}
