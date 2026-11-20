using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Features.Bookings;
using ServiceHub.Api.Features.Page;
using ServiceHub.Api.Features.Services;

namespace ServiceHub.Api.Features.Businesses;

public enum BusinessSort { Recommended, HighestRated, MostReviewed, Newest }

public record SearchQuery(
    string? Q,
    int? CategoryId,
    int? ZoneId,
    bool? HomeService,
    bool? EmergencyService,
    decimal? MinRating,
    BusinessSort Sort = BusinessSort.Recommended,
    int Page = 1,
    int PageSize = 20);

public record BusinessCardDto(Guid Id, string Name, string Slug, string? LogoUrl, string? CoverUrl, int CategoryId,
    string CategoryName, string? ZoneName, decimal AvgRating, int ReviewCount, decimal? StartingPrice,
    bool OffersHomeService, bool OffersEmergencyService, bool IsVerified);

public record PublicHourDto(short DayOfWeek, TimeOnly OpensAt, TimeOnly ClosesAt);

public record ReviewDto(Guid Id, string CustomerName, string ServiceName, short Rating, string? Comment, DateTime CreatedAt,
    string? ReplyBody, DateTime? RepliedAt, List<string> Photos);

public record PublicBusinessDto(Guid Id, string Name, string Slug, string? Tagline, string? ShortDescription,
    string? Description, string? LogoUrl, string? CoverUrl, int CategoryId, string CategoryName, string? AddressLine,
    string? ZoneName, string? CoverageNote, List<string> Languages, short? EstablishedYear, bool OffersHomeService,
    bool OffersEmergencyService, decimal AvgRating, int ReviewCount, int CompletedBookingCount, int? AvgResponseMinutes,
    string? CancellationPolicy, List<PublicHourDto> Hours,
    List<ServiceDto> Services, Dictionary<short, int> RatingDistribution, List<ReviewDto> LatestReviews,
    string ThemeKey, string HeaderStyle, List<string> Sections, List<PublicNoticeDto> Notices, List<FaqDto> Faqs,
    List<HighlightDto> Highlights, List<GalleryItemDto> Gallery, List<PublicTeamMemberDto> Team, bool IsVerified);

public record PublicNoticeDto(Guid Id, string Title, string Body, bool IsPinned, DateTime CreatedAt, DateTime? ExpiresAt);

public record PublicTeamMemberDto(string Name, string? Title, string? AvatarUrl);

/// <summary>Guest-accessible discovery endpoints. Only published (verified) businesses are visible.</summary>
[ApiController]
[Route("api/public/businesses")]
public class PublicController(AppDbContext db, AvailabilityService availability, PublicProfileBuilder profiles) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<BusinessCardDto>>> Search([FromQuery] SearchQuery query)
    {
        var page = new PageQuery(query.Page, query.PageSize);
        var q = db.Businesses.AsNoTracking().Where(b => b.Status == BusinessStatus.Published);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            var like = $"%{term}%";
            q = q.Where(b => EF.Functions.ILike(b.Name, like) || EF.Functions.ILike(b.Category.NameBn, like) ||
                             EF.Functions.ILike(b.Category.NameEn, like) ||
                             b.Services.Any(s => s.Status == RecordStatus.Active && EF.Functions.ILike(s.Name, like)) ||
                             (b.Zone != null && (EF.Functions.ILike(b.Zone.NameBn, like) || EF.Functions.ILike(b.Zone.NameEn, like))));
        }
        if (query.CategoryId is not null)
            q = q.Where(b => b.CategoryId == query.CategoryId || b.Category.ParentId == query.CategoryId);
        if (query.ZoneId is not null)
        {
            var path = await db.GeoZones.Where(z => z.Id == query.ZoneId).Select(z => (LTree?)z.Path).FirstOrDefaultAsync()
                ?? throw new BadRequestException("এলাকাটি পাওয়া যায়নি।", "invalid_zone");
            q = q.Where(b => b.Zone != null && b.Zone.Path.IsDescendantOf(path));
        }
        if (query.HomeService == true) q = q.Where(b => b.OffersHomeService);
        if (query.EmergencyService == true) q = q.Where(b => b.OffersEmergencyService);
        if (query.MinRating is not null) q = q.Where(b => b.AvgRating >= query.MinRating);

        q = query.Sort switch
        {
            BusinessSort.HighestRated => q.OrderByDescending(b => b.AvgRating).ThenByDescending(b => b.ReviewCount),
            BusinessSort.MostReviewed => q.OrderByDescending(b => b.ReviewCount),
            BusinessSort.Newest => q.OrderByDescending(b => b.PublishedAt),
            // Bayesian-style blend: rating weighted by evidence, so 1 five-star review doesn't beat 200 4.8s.
            // (computed in double: decimal arithmetic would inherit avg_rating's numeric(2,1) and overflow)
            _ => q.OrderByDescending(b => ((double)b.AvgRating * b.ReviewCount + 4.0 * 10) / (b.ReviewCount + 10))
                  .ThenByDescending(b => b.CompletedBookingCount),
        };

        var total = await q.CountAsync();
        var items = await q.Skip(page.Skip).Take(page.SafeSize)
            .Select(b => new BusinessCardDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.CoverUrl, b.CategoryId, b.Category.NameBn,
                b.Zone != null ? b.Zone.NameBn : null, b.AvgRating, b.ReviewCount,
                b.Services.Where(s => s.Status == RecordStatus.Active && s.Price != null).Min(s => s.Price),
                b.OffersHomeService, b.OffersEmergencyService, b.VerificationStatus == VerificationStatus.Approved))
            .ToListAsync();
        return new PagedResult<BusinessCardDto>(items, page.SafePage, page.SafeSize, total);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicBusinessDto>> Profile(string slug) =>
        await profiles.BuildAsync(x => x.Slug == slug && x.Status == BusinessStatus.Published)
        ?? throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");

    [HttpGet("{id:guid}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewDto>>> Reviews(Guid id, [FromQuery] PageQuery paging, [FromQuery] short? rating)
    {
        var q = db.Reviews.Where(r => r.BusinessId == id && r.Status == ReviewStatus.Published);
        if (rating is not null) q = q.Where(r => r.Rating == rating);
        var total = await q.CountAsync();
        var items = await profiles.ReviewsQuery(id, rating).Skip(paging.Skip).Take(paging.SafeSize).ToListAsync();
        return new PagedResult<ReviewDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    [HttpGet("{id:guid}/availability")]
    public async Task<ActionResult<DayAvailabilityDto>> Availability(Guid id, [FromQuery] Guid serviceId, [FromQuery] DateOnly date)
    {
        if (!await db.Businesses.AnyAsync(b => b.Id == id && b.Status == BusinessStatus.Published))
            throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");
        return await availability.GetAsync(id, serviceId, date);
    }

}
