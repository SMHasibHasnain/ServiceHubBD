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

public record RescheduleRequestBody(DateOnly Date, TimeOnly Time, [StringLength(500)] string? Reason);

public record ProgressRequest(WorkProgress Progress);

public record LeaveRequestBody(DateOnly StartDate, DateOnly EndDate, [StringLength(300)] string? Reason);

public record LeaveDecisionBody([StringLength(300)] string? Note);

public record LeaveDto(Guid Id, Guid MemberId, string MemberName, DateOnly StartDate, DateOnly EndDate, string? Reason,
    LeaveStatus Status, string? DecisionNote, DateTime CreatedAt, DateTime? DecidedAt);

public record MyDayDto(DateOnly Date, List<BookingDto> Bookings, LeaveDto? Leave);

public static class Leaves
{
    public const int MaxDays = 30;

    /// <summary>True when the member has approved leave covering that Bangladesh date.</summary>
    public static Task<bool> IsOnLeaveAsync(AppDbContext db, Guid memberId, DateOnly date) =>
        db.StaffLeaves.AnyAsync(l => l.MemberId == memberId && l.Status == LeaveStatus.Approved && l.StartDate <= date && l.EndDate >= date);

    public static LeaveDto ToDto(StaffLeave l) => new(l.Id, l.MemberId, l.Member.User.FullName, l.StartDate, l.EndDate, l.Reason,
        l.Status, l.DecisionNote, l.CreatedAt, l.DecidedAt);
}

/// <summary>
/// Reschedule requests. The customer proposes a new slot (checked against availability, ignoring this booking);
/// the business accepts (slot rechecked under the booking lock) or rejects with a reason. One open request at a time.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public class RescheduleController(AppDbContext db, ICurrentUser me, AccessService access, AvailabilityService availability,
    Notifier notifier) : ControllerBase
{
    private static readonly BookingStatus[] Movable = [BookingStatus.Pending, BookingStatus.Confirmed];

    /// <summary>Free slots for moving my booking to <paramref name="date"/> (my own booking doesn't block them).</summary>
    [HttpGet("me/bookings/{id:guid}/reschedule-slots")]
    public async Task<ActionResult<DayAvailabilityDto>> Slots(Guid id, [FromQuery] DateOnly date)
    {
        var booking = await OwnAsync(id);
        return await availability.GetAsync(booking.BusinessId, booking.ServiceId, date, booking.Id);
    }

    [HttpPost("me/bookings/{id:guid}/reschedule")]
    public async Task<IActionResult> Propose(Guid id, RescheduleRequestBody req)
    {
        var booking = await OwnAsync(id);
        if (!Movable.Contains(booking.Status) || booking.StartAt <= DateTime.UtcNow)
            throw new ConflictException("এই বুকিংয়ের সময় আর পরিবর্তন করা যায় না।", "not_reschedulable");
        if (booking.RescheduleRequests.Any(r => r.Status == RescheduleStatus.Pending))
            throw new ConflictException("আগের অনুরোধটির উত্তর এখনও আসেনি।", "reschedule_pending");
        var slot = await FreeSlotAsync(booking, req.Date, req.Time);
        if (slot.StartAt == booking.StartAt)
            throw new BadRequestException("নতুন একটি সময় বেছে নিন।", "same_time");

        var request = new BookingRescheduleRequest
        {
            BookingId = booking.Id, RequestedBy = me.RequireId(), ProposedStartAt = slot.StartAt, ProposedEndAt = slot.EndAt,
            PreviousStartAt = booking.StartAt, Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
        };
        db.BookingRescheduleRequests.Add(request);
        await notifier.NotifyBusinessAsync(booking.BusinessId, P.BookingsManage, "booking.reschedule_requested",
            "একজন গ্রাহক সময় পরিবর্তনের অনুরোধ করেছেন।", $"{booking.CustomerName} — {booking.ServiceName}: {When(booking.StartAt)} → {When(slot.StartAt)}",
            data: new() { ["bookingId"] = booking.Id.ToString() });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("me/bookings/{id:guid}/reschedule")]
    public async Task<IActionResult> Withdraw(Guid id)
    {
        var booking = await OwnAsync(id);
        var pending = booking.RescheduleRequests.FirstOrDefault(r => r.Status == RescheduleStatus.Pending)
            ?? throw new NotFoundException("কোনো খোলা অনুরোধ নেই।", "no_pending_reschedule");
        pending.Status = RescheduleStatus.Cancelled;
        pending.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("businesses/{businessId:guid}/bookings/{id:guid}/reschedule/accept")]
    public async Task<IActionResult> Accept(Guid businessId, Guid id)
    {
        await access.RequireBusinessAsync(businessId, P.BookingsManage);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({businessId.ToString()}, 0))");

        var booking = await BusinessBookingAsync(businessId, id);
        var request = Pending(booking);
        if (!Movable.Contains(booking.Status))
            throw new ConflictException("এই বুকিংয়ের সময় আর পরিবর্তন করা যায় না।", "not_reschedulable");
        var local = BdTime.ToLocal(request.ProposedStartAt);
        var slot = await FreeSlotAsync(booking, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));

        var previous = booking.StartAt;
        booking.StartAt = slot.StartAt;
        booking.EndAt = slot.EndAt;
        booking.Progress = null;
        booking.ProgressAt = null;
        request.Status = RescheduleStatus.Accepted;
        request.RespondedBy = me.RequireId();
        request.RespondedAt = DateTime.UtcNow;
        db.BookingStatusHistory.Add(new BookingStatusHistory
        {
            BookingId = booking.Id, FromStatus = booking.Status, ToStatus = booking.Status, ChangedBy = me.Id, Actor = ActorType.Business,
            Reason = $"সময় পরিবর্তন: {When(previous)} → {When(slot.StartAt)}",
        });
        var customer = await db.BusinessCustomers.FirstAsync(c => c.Id == booking.BusinessCustomerId);
        var nextOther = await db.Bookings
            .Where(b => b.BusinessCustomerId == customer.Id && b.Id != booking.Id && b.StartAt > DateTime.UtcNow &&
                        AvailabilityService.BlockingStatuses.Contains(b.Status))
            .OrderBy(b => b.StartAt).Select(b => (DateTime?)b.StartAt).FirstOrDefaultAsync();
        customer.NextBookingAt = nextOther is { } other && other < booking.StartAt ? other : booking.StartAt;

        var data = new Dictionary<string, string> { ["bookingId"] = booking.Id.ToString() };
        if (booking.CustomerUserId is { } customerId)
            notifier.Notify(customerId, WorkspaceType.Customer, null, "booking.rescheduled", "আপনার বুকিংয়ের নতুন সময় নিশ্চিত হয়েছে।",
                $"{booking.ServiceName}: {When(slot.StartAt)}", data: data);
        // The assigned person keeps the job unless they're on leave that day; then a manager reassigns it.
        if (booking.AssignedMember is { } assignee)
        {
            if (await Leaves.IsOnLeaveAsync(db, assignee.Id, DateOnly.FromDateTime(BdTime.ToLocal(slot.StartAt))))
            {
                booking.AssignedMemberId = null;
                booking.AssignedMember = null;
                await notifier.NotifyBusinessAsync(businessId, P.BookingsManage, "booking.needs_assignee",
                    "নতুন সময়ে বরাদ্দ করা সদস্য ছুটিতে আছেন। অন্য কাউকে বরাদ্দ দিন।", $"{booking.CustomerName} — {booking.ServiceName}", data: data);
            }
            else
            {
                notifier.Notify(assignee.UserId, WorkspaceType.Business, businessId, "booking.time_changed", "আপনার একটি কাজের সময় বদলেছে।",
                    $"{booking.CustomerName} — {booking.ServiceName}: {When(slot.StartAt)}", data: data);
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return NoContent();
    }

    [HttpPost("businesses/{businessId:guid}/bookings/{id:guid}/reschedule/reject")]
    public async Task<IActionResult> Reject(Guid businessId, Guid id, ReasonRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.BookingsManage);
        var booking = await BusinessBookingAsync(businessId, id);
        var request = Pending(booking);
        request.Status = RescheduleStatus.Rejected;
        request.ResponseNote = req.Reason.Trim();
        request.RespondedBy = me.RequireId();
        request.RespondedAt = DateTime.UtcNow;
        if (booking.CustomerUserId is { } customerId)
            notifier.Notify(customerId, WorkspaceType.Customer, null, "booking.reschedule_rejected", "সময় পরিবর্তনের অনুরোধটি গ্রহণ করা যায়নি।",
                $"{booking.ServiceName} আগের সময়েই থাকছে ({When(booking.StartAt)})। কারণ: {request.ResponseNote}",
                data: new() { ["bookingId"] = booking.Id.ToString() });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<SlotDto> FreeSlotAsync(Booking booking, DateOnly date, TimeOnly time)
    {
        var day = await availability.GetAsync(booking.BusinessId, booking.ServiceId, date, booking.Id);
        var slot = day.Slots.FirstOrDefault(s => s.Time == time)
            ?? throw new BadRequestException("এই সময়ে বুকিং নেওয়া হয় না। অন্য সময় বেছে নিন।", "invalid_slot");
        if (!slot.Available) throw new ConflictException("দুঃখিত, এই সময়টি আর ফাঁকা নেই। অন্য সময় বেছে নিন।", "slot_taken");
        return slot;
    }

    private static BookingRescheduleRequest Pending(Booking booking) =>
        booking.RescheduleRequests.FirstOrDefault(r => r.Status == RescheduleStatus.Pending)
        ?? throw new NotFoundException("কোনো খোলা অনুরোধ নেই।", "no_pending_reschedule");

    private async Task<Booking> OwnAsync(Guid id)
    {
        var userId = me.RequireId();
        return await db.Bookings.Include(b => b.RescheduleRequests).FirstOrDefaultAsync(b => b.Id == id && b.CustomerUserId == userId)
               ?? throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");
    }

    private async Task<Booking> BusinessBookingAsync(Guid businessId, Guid id) =>
        await db.Bookings.Include(b => b.RescheduleRequests).Include(b => b.AssignedMember)
            .FirstOrDefaultAsync(b => b.Id == id && b.BusinessId == businessId)
        ?? throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");

    internal static string When(DateTime utc) => BdTime.ToLocal(utc).ToString("dd MMM, hh:mm tt");
}

/// <summary>
/// The field side of a business: a member's own day (assigned bookings), on-the-way/started progress that the
/// customer sees, and leave requests that managers approve. Approved leave keeps bookings off that member.
/// </summary>
[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}")]
public class StaffController(AppDbContext db, ICurrentUser me, AccessService access, Notifier notifier) : ControllerBase
{
    [HttpGet("my-day")]
    public async Task<ActionResult<MyDayDto>> MyDay(Guid businessId, [FromQuery] DateOnly? date)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessView);
        var day = date ?? BdTime.Today();
        var start = BdTime.ToUtc(day, TimeOnly.MinValue);
        var end = start.AddDays(1);
        var list = await db.Bookings.WithDetails().AsSplitQuery()
            .Where(b => b.BusinessId == businessId && b.AssignedMemberId == member.Id && b.StartAt >= start && b.StartAt < end &&
                        (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed))
            .OrderBy(b => b.StartAt).ToListAsync();
        var leave = await db.StaffLeaves.Include(l => l.Member).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(l => l.MemberId == member.Id && l.Status == LeaveStatus.Approved && l.StartDate <= day && l.EndDate >= day);
        return new MyDayDto(day, list.Select(b => BookingQueries.ToDto(b, true, false)).ToList(), leave is null ? null : Leaves.ToDto(leave));
    }

    /// <summary>"On the way" / "started", by the assigned member (or a manager). The customer is notified.</summary>
    [HttpPost("bookings/{id:guid}/progress")]
    public async Task<ActionResult<BookingDto>> Progress(Guid businessId, Guid id, ProgressRequest req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessView);
        var booking = await db.Bookings.WithDetails().AsSplitQuery().FirstOrDefaultAsync(b => b.Id == id && b.BusinessId == businessId)
                      ?? throw new NotFoundException("বুকিংটি পাওয়া যায়নি।", "booking_not_found");
        if (booking.AssignedMemberId != member.Id && !await access.HasBusinessPermissionAsync(businessId, P.BookingsManage))
            throw new ForbiddenException("শুধু আপনাকে বরাদ্দ করা কাজের অবস্থা বদলাতে পারবেন।", "not_assigned");
        if (booking.Status != BookingStatus.Confirmed)
            throw new ConflictException("শুধু নিশ্চিত বুকিংয়ের কাজ শুরু করা যায়।", "not_confirmed");
        if (booking.Progress == WorkProgress.Started && req.Progress == WorkProgress.OnTheWay)
            throw new ConflictException("কাজ ইতিমধ্যে শুরু হয়েছে।", "invalid_progress");

        booking.Progress = req.Progress;
        booking.ProgressAt = DateTime.UtcNow;
        if (booking.CustomerUserId is { } customerId)
        {
            var who = booking.AssignedMember?.User.FullName ?? booking.Business.Name;
            var title = req.Progress == WorkProgress.OnTheWay ? $"{who} আপনার কাছে রওনা দিয়েছেন।" : $"{who} কাজ শুরু করেছেন।";
            notifier.Notify(customerId, WorkspaceType.Customer, null, $"booking.{SnakeCase.FromEnum(req.Progress)}", title,
                $"{booking.ServiceName} — {booking.BookingNumber}", data: new() { ["bookingId"] = booking.Id.ToString() });
        }
        await db.SaveChangesAsync();
        return BookingQueries.ToDto(booking, true, false);
    }

    // ---------------- leave ----------------

    /// <summary>Managers see everyone's leave; others see their own.</summary>
    [HttpGet("leaves")]
    public async Task<ActionResult<PagedResult<LeaveDto>>> ListLeaves(Guid businessId, [FromQuery] PageQuery paging, [FromQuery] bool mine = false)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessView);
        var all = !mine && await access.HasBusinessPermissionAsync(businessId, P.MembersManage);
        var since = BdTime.Today().AddDays(-30);
        var query = db.StaffLeaves.Where(l => l.BusinessId == businessId && (all || l.MemberId == member.Id) && l.EndDate >= since);
        var total = await query.CountAsync();
        var list = await query.Include(l => l.Member).ThenInclude(m => m.User)
            .OrderBy(l => l.Status == LeaveStatus.Pending ? 0 : 1).ThenBy(l => l.StartDate).ThenBy(l => l.Id)
            .Skip(paging.Skip).Take(paging.SafeSize).ToListAsync();
        return new PagedResult<LeaveDto>(list.Select(Leaves.ToDto).ToList(), paging.SafePage, paging.SafeSize, total);
    }

    [HttpPost("leaves")]
    public async Task<ActionResult<LeaveDto>> RequestLeave(Guid businessId, LeaveRequestBody req)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessView);
        if (req.StartDate < BdTime.Today() || req.EndDate < req.StartDate)
            throw new BadRequestException("সঠিক তারিখ দিন: শুরু আজ বা পরে, শেষ শুরুর দিন বা পরে।", "invalid_dates");
        if (req.EndDate.DayNumber - req.StartDate.DayNumber + 1 > Leaves.MaxDays)
            throw new BadRequestException("একবারে সর্বোচ্চ ৩০ দিনের ছুটি চাওয়া যায়।", "leave_too_long");
        if (await db.StaffLeaves.AnyAsync(l => l.MemberId == member.Id && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved) &&
                                               l.StartDate <= req.EndDate && l.EndDate >= req.StartDate))
            throw new ConflictException("এই সময়ের জন্য আগেই ছুটির আবেদন আছে।", "leave_overlaps");

        var leave = new StaffLeave
        {
            BusinessId = businessId, MemberId = member.Id, StartDate = req.StartDate, EndDate = req.EndDate,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
        };
        db.StaffLeaves.Add(leave);
        var name = await db.Users.Where(u => u.Id == member.UserId).Select(u => u.FullName).FirstAsync();
        await notifier.NotifyBusinessAsync(businessId, P.MembersManage, "leave.requested", $"{name} ছুটির আবেদন করেছেন।",
            $"{req.StartDate:dd MMM} – {req.EndDate:dd MMM}", $"/w/{businessId}/team/leave");
        await db.SaveChangesAsync();
        return await LoadAsync(leave.Id);
    }

    /// <summary>
    /// Approves leave. Bookings already assigned to the member on those days are unassigned, and managers are told
    /// how many need someone else.
    /// </summary>
    [HttpPost("leaves/{leaveId:guid}/approve")]
    public async Task<ActionResult<LeaveDto>> Approve(Guid businessId, Guid leaveId, LeaveDecisionBody req)
    {
        await access.RequireBusinessAsync(businessId, P.MembersManage);
        var leave = await PendingAsync(businessId, leaveId);
        leave.Status = LeaveStatus.Approved;
        Decide(leave, req.Note);

        var from = BdTime.ToUtc(leave.StartDate, TimeOnly.MinValue);
        var to = BdTime.ToUtc(leave.EndDate.AddDays(1), TimeOnly.MinValue);
        var affected = await db.Bookings.Where(b => b.BusinessId == businessId && b.AssignedMemberId == leave.MemberId &&
                                                    b.StartAt >= from && b.StartAt < to && AvailabilityService.BlockingStatuses.Contains(b.Status))
            .ToListAsync();
        foreach (var b in affected) b.AssignedMemberId = null;
        if (affected.Count > 0)
            await notifier.NotifyBusinessAsync(businessId, P.BookingsManage, "booking.needs_assignee",
                $"ছুটির কারণে {EntitlementBn(affected.Count)}টি অ্যাপয়েন্টমেন্ট থেকে {leave.Member.User.FullName}-কে সরানো হয়েছে।",
                "অন্য কাউকে বরাদ্দ দিন।", $"/w/{businessId}/bookings");
        notifier.Notify(leave.Member.UserId, WorkspaceType.Business, businessId, "leave.approved", "আপনার ছুটি মঞ্জুর হয়েছে।",
            $"{leave.StartDate:dd MMM} – {leave.EndDate:dd MMM}{(leave.DecisionNote is null ? "" : $"। {leave.DecisionNote}")}", $"/w/{businessId}/my-day");
        await db.SaveChangesAsync();
        return Leaves.ToDto(leave);
    }

    [HttpPost("leaves/{leaveId:guid}/reject")]
    public async Task<ActionResult<LeaveDto>> Reject(Guid businessId, Guid leaveId, ReasonRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.MembersManage);
        var leave = await PendingAsync(businessId, leaveId);
        leave.Status = LeaveStatus.Rejected;
        Decide(leave, req.Reason);
        notifier.Notify(leave.Member.UserId, WorkspaceType.Business, businessId, "leave.rejected", "আপনার ছুটির আবেদন মঞ্জুর হয়নি।",
            $"কারণ: {leave.DecisionNote}", $"/w/{businessId}/my-day");
        await db.SaveChangesAsync();
        return Leaves.ToDto(leave);
    }

    /// <summary>The member withdraws a pending leave, or an approved one that hasn't ended.</summary>
    [HttpDelete("leaves/{leaveId:guid}")]
    public async Task<IActionResult> Cancel(Guid businessId, Guid leaveId)
    {
        var member = await access.RequireBusinessAsync(businessId, P.BusinessView);
        var leave = await db.StaffLeaves.FirstOrDefaultAsync(l => l.Id == leaveId && l.BusinessId == businessId && l.MemberId == member.Id)
                    ?? throw new NotFoundException("ছুটির আবেদনটি পাওয়া যায়নি।", "leave_not_found");
        if (leave.Status is not (LeaveStatus.Pending or LeaveStatus.Approved) || leave.EndDate < BdTime.Today())
            throw new ConflictException("এই আবেদনটি আর বাতিল করা যায় না।", "leave_closed");
        leave.Status = LeaveStatus.Cancelled;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private void Decide(StaffLeave leave, string? note)
    {
        leave.DecidedBy = me.RequireId();
        leave.DecidedAt = DateTime.UtcNow;
        leave.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private async Task<StaffLeave> PendingAsync(Guid businessId, Guid leaveId)
    {
        var leave = await db.StaffLeaves.Include(l => l.Member).ThenInclude(m => m.User)
                        .FirstOrDefaultAsync(l => l.Id == leaveId && l.BusinessId == businessId)
                    ?? throw new NotFoundException("ছুটির আবেদনটি পাওয়া যায়নি।", "leave_not_found");
        if (leave.Status != LeaveStatus.Pending) throw new ConflictException("আবেদনটির সিদ্ধান্ত আগেই হয়েছে।", "leave_decided");
        return leave;
    }

    private async Task<LeaveDto> LoadAsync(Guid id) =>
        Leaves.ToDto(await db.StaffLeaves.Include(l => l.Member).ThenInclude(m => m.User).FirstAsync(l => l.Id == id));

    private static string EntitlementBn(int n) => Billing.EntitlementService.Bn(n);
}
