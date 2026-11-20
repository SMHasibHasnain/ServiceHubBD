using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Bookings;

public record CreateBookingRequest(
    Guid ServiceId,
    DateOnly Date,
    TimeOnly Time,
    [StringLength(120)] string? CustomerName,
    string? CustomerPhone,
    string? ServiceAddress,
    [StringLength(1000)] string? Note,
    List<FieldAnswer>? Answers,
    [StringLength(64)] string? ClientRequestId,
    BookingSource Source = BookingSource.Web);

public record ReasonRequest([Required, StringLength(500, MinimumLength = 3)] string Reason);

public record CompleteRequest([Range(0, 10_000_000)] decimal? FinalPrice);

public record AssignRequest(Guid? MemberId);

public record BusinessNoteRequest([StringLength(2000)] string? Note);

public enum MyBookingScope { Upcoming, Past, Cancelled }

public record AnswerDto(string Label, string? Value, string? FileUrl);

public record HistoryDto(BookingStatus? From, BookingStatus To, ActorType Actor, string? Reason, DateTime At);

public record AssigneeDto(Guid MemberId, Guid UserId, string Name);

public record RescheduleDto(Guid Id, DateTime ProposedStartAt, DateTime ProposedEndAt, DateTime PreviousStartAt, string? Reason,
    RescheduleStatus Status, string? ResponseNote, DateTime CreatedAt, DateTime? RespondedAt);

public record BookingDto(
    Guid Id, string BookingNumber, Guid BusinessId, string BusinessName, string BusinessSlug, Guid ServiceId,
    string ServiceName, DateTime StartAt, DateTime EndAt, BookingStatus Status, BookingSource Source, PriceType PriceType,
    decimal? QuotedPrice, decimal? FinalPrice, short DurationMinutes, string CustomerName, string CustomerPhone,
    string? ServiceAddress, string? CustomerNote, string? BusinessNote, Guid BusinessCustomerId, AssigneeDto? AssignedTo,
    string? CancellationReason, bool HasReview, List<AnswerDto> Answers, List<HistoryDto> History,
    WorkProgress? Progress, DateTime? ProgressAt, RescheduleDto? Reschedule);

internal static class BookingQueries
{
    public static IQueryable<Booking> WithDetails(this IQueryable<Booking> q) => q
        .Include(b => b.Business).Include(b => b.FieldResponses).Include(b => b.History).Include(b => b.RescheduleRequests)
        .Include(b => b.AssignedMember!).ThenInclude(m => m.User);

    public static BookingDto ToDto(Booking b, bool forBusiness, bool hasReview) => new(
        b.Id, b.BookingNumber, b.BusinessId, b.Business.Name, b.Business.Slug, b.ServiceId, b.ServiceName, b.StartAt,
        b.EndAt, b.Status, b.Source, b.PriceType, b.QuotedPrice, b.FinalPrice, b.DurationMinutes, b.CustomerName,
        b.CustomerPhone, b.ServiceAddress, b.CustomerNote, forBusiness ? b.BusinessNote : null, b.BusinessCustomerId,
        b.AssignedMember is null ? null : new AssigneeDto(b.AssignedMember.Id, b.AssignedMember.UserId, b.AssignedMember.User.FullName),
        b.CancellationReason, hasReview,
        b.FieldResponses.Select(f => new AnswerDto(f.Label, f.ValueText, f.FileUrl)).ToList(),
        b.History.OrderBy(h => h.CreatedAt).Select(h => new HistoryDto(h.FromStatus, h.ToStatus, h.Actor, h.Reason, h.CreatedAt)).ToList(),
        b.Progress, b.ProgressAt,
        // The latest request: pending, or the last decision so the customer sees why it was turned down.
        b.RescheduleRequests.OrderByDescending(r => r.CreatedAt).Select(r => new RescheduleDto(r.Id, r.ProposedStartAt, r.ProposedEndAt,
            r.PreviousStartAt, r.Reason, r.Status, r.ResponseNote, r.CreatedAt, r.RespondedAt)).FirstOrDefault());
}

/// <summary>Customer side: book, list my bookings, cancel.</summary>
[ApiController]
[Authorize]
[Route("api")]
public class CustomerBookingsController(AppDbContext db, ICurrentUser me, BookingService bookings) : ControllerBase
{
    [HttpPost("bookings")]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingRequest req)
    {
        var booking = await bookings.CreateAsync(new CreateBookingCommand(req.ServiceId, req.Date, req.Time, req.CustomerName,
            req.CustomerPhone, req.ServiceAddress, req.Note, req.Answers, req.ClientRequestId, req.Source));
        var dto = await LoadAsync(booking.Id);
        return CreatedAtAction(nameof(Get), new { id = booking.Id }, dto);
    }

    [HttpGet("me/bookings")]
    public async Task<ActionResult<PagedResult<BookingDto>>> Mine([FromQuery] MyBookingScope scope, [FromQuery] PageQuery paging)
    {
        var userId = me.RequireId();
        var now = DateTime.UtcNow;
        var q = db.Bookings.Where(b => b.CustomerUserId == userId);
        q = scope switch
        {
            MyBookingScope.Upcoming => q.Where(b => b.EndAt >= now && AvailabilityService.BlockingStatuses.Contains(b.Status) ||
                                                    b.Status == BookingStatus.Rescheduled).OrderBy(b => b.StartAt),
            MyBookingScope.Cancelled => q.Where(b => b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Rejected)
                .OrderByDescending(b => b.StartAt),
            _ => q.Where(b => b.Status == BookingStatus.Completed || b.Status == BookingStatus.NoShow ||
                              (b.EndAt < now && AvailabilityService.BlockingStatuses.Contains(b.Status)))
                .OrderByDescending(b => b.StartAt),
        };
        var total = await q.CountAsync();
        var list = await q.WithDetails().Skip(paging.Skip).Take(paging.SafeSize).AsSplitQuery().ToListAsync();
        var ids = list.Select(b => b.Id).ToList();
        var reviewed = await db.Reviews.Where(r => ids.Contains(r.BookingId)).Select(r => r.BookingId).ToListAsync();
        return new PagedResult<BookingDto>(list.Select(b => BookingQueries.ToDto(b, false, reviewed.Contains(b.Id))).ToList(),
            paging.SafePage, paging.SafeSize, total);
    }

    [HttpGet("me/bookings/{id:guid}")]
    public async Task<ActionResult<BookingDto>> Get(Guid id) => await LoadAsync(id);

    [HttpPost("me/bookings/{id:guid}/cancel")]
    public async Task<ActionResult<BookingDto>> Cancel(Guid id, ReasonRequest req)
    {
        var booking = await OwnAsync(id);
        if (booking.StartAt <= DateTime.UtcNow)
            throw new ConflictException("শুরু হয়ে যাওয়া অ্যাপয়েন্টমেন্ট বাতিল করা যায় না।", "already_started");
        await bookings.TransitionAsync(booking, BookingStatus.Cancelled, ActorType.Customer, req.Reason);
        await db.SaveChangesAsync();
        return await LoadAsync(id);
    }

    private async Task<Booking> OwnAsync(Guid id)
    {
        var userId = me.RequireId();
        return await db.Bookings.FirstOrDefaultAsync(b => b.Id == id && b.CustomerUserId == userId)
               ?? throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");
    }

    private async Task<BookingDto> LoadAsync(Guid id)
    {
        var userId = me.RequireId();
        var b = await db.Bookings.WithDetails().AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id && x.CustomerUserId == userId)
                ?? throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");
        return BookingQueries.ToDto(b, false, await db.Reviews.AnyAsync(r => r.BookingId == id));
    }
}

/// <summary>Business side: the appointments module.</summary>
[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/bookings")]
public class BusinessBookingsController(AppDbContext db, AccessService access, BookingService bookings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<BookingDto>>> List(Guid businessId, [FromQuery] BookingStatus? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? q, [FromQuery] bool mine, [FromQuery] PageQuery paging)
    {
        var (member, seeAll) = await ViewerAsync(businessId);
        var query = db.Bookings.Where(b => b.BusinessId == businessId);
        if (!seeAll || mine) query = query.Where(b => b.AssignedMemberId == member.Id);
        if (status is not null) query = query.Where(b => b.Status == status);
        if (from is not null) { var f = BdTime.ToUtc(from.Value, TimeOnly.MinValue); query = query.Where(b => b.StartAt >= f); }
        if (to is not null) { var t = BdTime.ToUtc(to.Value.AddDays(1), TimeOnly.MinValue); query = query.Where(b => b.StartAt < t); }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            var phone = Phone.NormalizeBd(q);
            query = query.Where(b => EF.Functions.ILike(b.CustomerName, like) || EF.Functions.ILike(b.ServiceName, like) ||
                                     EF.Functions.ILike(b.BookingNumber, like) || (phone != null && b.CustomerPhone == phone));
        }
        var total = await query.CountAsync();
        var list = await query.OrderBy(b => b.StartAt).WithDetails().AsSplitQuery().Skip(paging.Skip).Take(paging.SafeSize).ToListAsync();
        var ids = list.Select(b => b.Id).ToList();
        var reviewed = await db.Reviews.Where(r => ids.Contains(r.BookingId)).Select(r => r.BookingId).ToListAsync();
        return new PagedResult<BookingDto>(list.Select(b => BookingQueries.ToDto(b, true, reviewed.Contains(b.Id))).ToList(),
            paging.SafePage, paging.SafeSize, total);
    }

    /// <summary>Counts for the summary cards (today, pending, confirmed, completed, cancelled, no-show).</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<Dictionary<string, int>>> Summary(Guid businessId, [FromQuery] DateOnly? date)
    {
        var (member, seeAll) = await ViewerAsync(businessId);
        var day = date ?? BdTime.Today();
        var start = BdTime.ToUtc(day, TimeOnly.MinValue);
        var end = start.AddDays(1);
        var q = db.Bookings.Where(b => b.BusinessId == businessId);
        if (!seeAll) q = q.Where(b => b.AssignedMemberId == member.Id);
        var byStatus = await q.Where(b => b.StartAt >= start && b.StartAt < end)
            .GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var result = Enum.GetValues<BookingStatus>().ToDictionary(SnakeCase.FromEnum, s => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0);
        result["today"] = byStatus.Sum(x => x.Count);
        result["pending_all"] = await q.CountAsync(b => b.Status == BookingStatus.Pending);
        return result;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDto>> Get(Guid businessId, Guid id)
    {
        var (member, seeAll) = await ViewerAsync(businessId);
        var b = await LoadAsync(businessId, id);
        if (!seeAll && b.AssignedMemberId != member.Id) throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");
        return BookingQueries.ToDto(b, true, await db.Reviews.AnyAsync(r => r.BookingId == id));
    }

    [HttpPost("{id:guid}/confirm")]
    public Task<ActionResult<BookingDto>> Confirm(Guid businessId, Guid id) =>
        ManageAsync(businessId, id, BookingStatus.Confirmed, null);

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<BookingDto>> Reject(Guid businessId, Guid id, ReasonRequest req) =>
        ManageAsync(businessId, id, BookingStatus.Rejected, req.Reason);

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<BookingDto>> Cancel(Guid businessId, Guid id, ReasonRequest req) =>
        ManageAsync(businessId, id, BookingStatus.Cancelled, req.Reason);

    [HttpPost("{id:guid}/no-show")]
    public Task<ActionResult<BookingDto>> NoShow(Guid businessId, Guid id) =>
        ManageAsync(businessId, id, BookingStatus.NoShow, null);

    /// <summary>Owner/manager, or the assigned staff member, marks the service as delivered.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<BookingDto>> Complete(Guid businessId, Guid id, CompleteRequest? req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BookingsComplete);
        var booking = await LoadAsync(businessId, id);
        var manages = await access.HasBusinessPermissionAsync(businessId, P.BookingsManage);
        if (!manages && booking.AssignedMemberId != member.Id)
            throw new ForbiddenException("শুধু আপনাকে বরাদ্দ করা অ্যাপয়েন্টমেন্ট সম্পন্ন করতে পারবেন।", "not_assigned");
        await bookings.TransitionAsync(booking, BookingStatus.Completed, ActorType.Business, null, req?.FinalPrice);
        await db.SaveChangesAsync();
        return BookingQueries.ToDto(booking, true, false);
    }

    [HttpPut("{id:guid}/assignee")]
    public async Task<ActionResult<BookingDto>> Assign(Guid businessId, Guid id, AssignRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.BookingsManage);
        var booking = await LoadAsync(businessId, id);
        if (req.MemberId is not null)
        {
            var member = await db.BusinessMembers.Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == req.MemberId && m.BusinessId == businessId && m.Status == MemberStatus.Active)
                ?? throw new BadRequestException("টিম সদস্য পাওয়া যায়নি।", "invalid_member");
            if (await Leaves.IsOnLeaveAsync(db, member.Id, DateOnly.FromDateTime(BdTime.ToLocal(booking.StartAt))))
                throw new ConflictException($"{member.User.FullName} ওই দিন ছুটিতে আছেন। অন্য কাউকে বরাদ্দ দিন।", "member_on_leave");
            booking.AssignedMember = member;
            await db.Notifications.AddAsync(new Notification
            {
                RecipientId = member.UserId, Workspace = WorkspaceType.Business, BusinessId = businessId, Type = "booking.assigned",
                Title = "আপনাকে একটি অ্যাপয়েন্টমেন্ট দেওয়া হয়েছে।", Body = $"{booking.CustomerName} — {booking.ServiceName}",
                Data = new() { ["bookingId"] = booking.Id.ToString() },
            });
        }
        booking.AssignedMemberId = req.MemberId;
        if (req.MemberId is null) booking.AssignedMember = null;
        await db.SaveChangesAsync();
        return BookingQueries.ToDto(booking, true, false);
    }

    [HttpPut("{id:guid}/note")]
    public async Task<ActionResult<BookingDto>> Note(Guid businessId, Guid id, BusinessNoteRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.BookingsManage);
        var booking = await LoadAsync(businessId, id);
        booking.BusinessNote = req.Note;
        await db.SaveChangesAsync();
        return BookingQueries.ToDto(booking, true, false);
    }

    private async Task<ActionResult<BookingDto>> ManageAsync(Guid businessId, Guid id, BookingStatus to, string? reason)
    {
        await access.RequireBusinessAsync(businessId, P.BookingsManage);
        var booking = await LoadAsync(businessId, id);
        await bookings.TransitionAsync(booking, to, ActorType.Business, reason);
        await db.SaveChangesAsync();
        return BookingQueries.ToDto(booking, true, false);
    }

    private async Task<(BusinessMember Member, bool SeeAll)> ViewerAsync(Guid businessId)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessView);
        if (await access.HasBusinessPermissionAsync(businessId, P.BookingsViewAll)) return (member, true);
        if (await access.HasBusinessPermissionAsync(businessId, P.BookingsViewAssigned)) return (member, false);
        throw new ForbiddenException();
    }

    private async Task<Booking> LoadAsync(Guid businessId, Guid id) =>
        await db.Bookings.WithDetails().AsSplitQuery().FirstOrDefaultAsync(b => b.Id == id && b.BusinessId == businessId)
        ?? throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");
}
