using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Reviews;

public record CreateReviewRequest([Range(1, 5)] short Rating, [StringLength(2000)] string? Comment);

public record ReplyRequest([Required, StringLength(2000, MinimumLength = 2)] string Body);

public record CreateReportRequest(ReportTarget TargetType, Guid? TargetBusinessId, Guid? TargetReviewId, Guid? TargetUserId,
    ReportReason Reason, [StringLength(2000)] string? Description);

public record ReviewManageDto(Guid Id, Guid BookingId, string CustomerName, string ServiceName, short Rating, string? Comment,
    ReviewStatus Status, DateTime CreatedAt, string? Reply, DateTime? RepliedAt);

public record ReportCreatedDto(Guid Id, ReportStatus Status);

[ApiController]
[Authorize]
[Route("api")]
public class ReviewsController(AppDbContext db, ICurrentUser me, AccessService access, Notifier notifier) : ControllerBase
{
    /// <summary>One review per completed booking, written by the customer who booked.</summary>
    [HttpPost("me/bookings/{bookingId:guid}/review")]
    public async Task<ActionResult<ReviewManageDto>> Create(Guid bookingId, CreateReviewRequest req)
    {
        var userId = me.RequireId();
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.CustomerUserId == userId)
                      ?? throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");
        if (booking.Status != BookingStatus.Completed)
            throw new ConflictException("সেবা সম্পন্ন হওয়ার পরেই রিভিউ দেওয়া যাবে।", "booking_not_completed");
        if (await db.Reviews.AnyAsync(r => r.BookingId == bookingId))
            throw new ConflictException("এই বুকিংয়ের রিভিউ আগেই দেওয়া হয়েছে।", "already_reviewed");

        var review = new Review
        {
            BookingId = bookingId, BusinessId = booking.BusinessId, ServiceId = booking.ServiceId, CustomerId = userId,
            Rating = req.Rating, Comment = req.Comment?.Trim(),
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        await RecalculateAsync(booking.BusinessId, booking.ServiceId);
        await notifier.NotifyBusinessAsync(booking.BusinessId, P.ReviewsReply, "review.created",
            $"নতুন {req.Rating} তারকা রিভিউ এসেছে।", req.Comment, data: new() { ["reviewId"] = review.Id.ToString() });
        await db.SaveChangesAsync();
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstAsync();
        return new ReviewManageDto(review.Id, bookingId, name, booking.ServiceName, review.Rating, review.Comment, review.Status,
            review.CreatedAt, null, null);
    }

    [HttpGet("businesses/{businessId:guid}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewManageDto>>> List(Guid businessId, [FromQuery] bool unrepliedOnly, [FromQuery] PageQuery paging)
    {
        await access.RequireBusinessAsync(businessId, P.BusinessView);
        var q = db.Reviews.Where(r => r.BusinessId == businessId && r.Status != ReviewStatus.Removed);
        if (unrepliedOnly) q = q.Where(r => r.Reply == null);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip(paging.Skip).Take(paging.SafeSize)
            .Select(r => new ReviewManageDto(r.Id, r.BookingId, r.Customer.FullName, r.Booking.ServiceName, r.Rating, r.Comment,
                r.Status, r.CreatedAt, r.Reply != null ? r.Reply.Body : null, r.Reply != null ? r.Reply.UpdatedAt : null))
            .ToListAsync();
        return new PagedResult<ReviewManageDto>(items, paging.SafePage, paging.SafeSize, total);
    }

    /// <summary>Public reply shown as the business. Replying again edits the existing reply.</summary>
    [HttpPut("businesses/{businessId:guid}/reviews/{reviewId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid businessId, Guid reviewId, ReplyRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.ReviewsReply);
        var review = await db.Reviews.Include(r => r.Reply).FirstOrDefaultAsync(r => r.Id == reviewId && r.BusinessId == businessId)
                     ?? throw new NotFoundException("রিভিউটি পাওয়া যায়নি।", "review_not_found");
        if (review.Reply is null)
        {
            review.Reply = new ReviewReply { AuthorId = me.RequireId(), Body = req.Body.Trim() };
            notifier.Notify(review.CustomerId, WorkspaceType.Customer, null, "review.replied", "ব্যবসাটি আপনার রিভিউর উত্তর দিয়েছে।",
                req.Body, data: new() { ["reviewId"] = review.Id.ToString() });
        }
        else
        {
            review.Reply.Body = req.Body.Trim();
            review.Reply.AuthorId = me.RequireId();
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>A business flags a review on its own page for moderators.</summary>
    [HttpPost("businesses/{businessId:guid}/reviews/{reviewId:guid}/report")]
    public async Task<ActionResult<ReportCreatedDto>> ReportReview(Guid businessId, Guid reviewId, CreateReportRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.ReviewsReport);
        if (!await db.Reviews.AnyAsync(r => r.Id == reviewId && r.BusinessId == businessId))
            throw new NotFoundException("রিভিউটি পাওয়া যায়নি।", "review_not_found");
        var report = new Report
        {
            ReporterId = me.RequireId(), ReporterBusinessId = businessId, TargetType = ReportTarget.Review,
            TargetReviewId = reviewId, Reason = req.Reason, Description = req.Description,
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return new ReportCreatedDto(report.Id, report.Status);
    }

    /// <summary>A customer reports a business, review or user.</summary>
    [HttpPost("reports")]
    public async Task<ActionResult<ReportCreatedDto>> Report(CreateReportRequest req)
    {
        var report = new Report
        {
            ReporterId = me.RequireId(), TargetType = req.TargetType, Reason = req.Reason, Description = req.Description,
            Severity = req.Reason is ReportReason.Fraud or ReportReason.FakeBusiness ? PriorityLevel.High : PriorityLevel.Normal,
        };
        switch (req.TargetType)
        {
            case ReportTarget.Business:
                if (req.TargetBusinessId is null || !await db.Businesses.AnyAsync(b => b.Id == req.TargetBusinessId))
                    throw new BadRequestException("ব্যবসাটি পাওয়া যায়নি।", "invalid_target");
                report.TargetBusinessId = req.TargetBusinessId;
                break;
            case ReportTarget.Review:
                if (req.TargetReviewId is null || !await db.Reviews.AnyAsync(r => r.Id == req.TargetReviewId))
                    throw new BadRequestException("রিভিউটি পাওয়া যায়নি।", "invalid_target");
                report.TargetReviewId = req.TargetReviewId;
                break;
            case ReportTarget.User:
                if (req.TargetUserId is null || !await db.Users.AnyAsync(u => u.Id == req.TargetUserId))
                    throw new BadRequestException("ব্যবহারকারী পাওয়া যায়নি।", "invalid_target");
                report.TargetUserId = req.TargetUserId;
                break;
        }
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return new ReportCreatedDto(report.Id, report.Status);
    }

    /// <summary>Recomputes denormalized rating counters from published reviews.</summary>
    public static async Task RecalculateRatingsAsync(AppDbContext db, Guid businessId, Guid serviceId)
    {
        var published = db.Reviews.Where(r => r.Status == ReviewStatus.Published);
        var bizReviews = published.Where(r => r.BusinessId == businessId);
        var bizAvg = await bizReviews.Select(r => (decimal?)r.Rating).AverageAsync();
        var bizCount = await bizReviews.CountAsync();
        var svc = await published.Where(r => r.ServiceId == serviceId)
            .Select(r => (decimal?)r.Rating).AverageAsync();
        var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
        business.AvgRating = Math.Round(bizAvg ?? 0, 1);
        business.ReviewCount = bizCount;
        var service = await db.Services.IgnoreQueryFilters().FirstAsync(s => s.Id == serviceId);
        service.AvgRating = Math.Round(svc ?? 0, 1);
    }

    private Task RecalculateAsync(Guid businessId, Guid serviceId) => RecalculateRatingsAsync(db, businessId, serviceId);
}
