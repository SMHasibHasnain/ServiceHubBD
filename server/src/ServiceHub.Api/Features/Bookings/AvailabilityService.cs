using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;

namespace ServiceHub.Api.Features.Bookings;

public record SlotDto(TimeOnly Time, DateTime StartAt, DateTime EndAt, bool Available);

public record DayAvailabilityDto(DateOnly Date, bool IsOpen, string? ClosedReason, List<SlotDto> Slots);

/// <summary>
/// Computes bookable slots for one service on one Bangladesh-local date from working hours,
/// closures, booking settings and existing pending/confirmed bookings.
/// </summary>
public class AvailabilityService(AppDbContext db)
{
    public static readonly BookingStatus[] BlockingStatuses = [BookingStatus.Pending, BookingStatus.Confirmed];

    public async Task<DayAvailabilityDto> GetAsync(Guid businessId, Guid serviceId, DateOnly date, CancellationToken ct = default)
    {
        var settings = await db.BusinessBookingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BusinessId == businessId, ct)
            ?? throw new NotFoundException("ব্যবসাটি পাওয়া যায়নি।", "business_not_found");
        var service = await db.Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.BusinessId == businessId && s.Status == RecordStatus.Active, ct)
            ?? throw new NotFoundException("সেবাটি পাওয়া যায়নি।", "service_not_found");

        var today = BdTime.Today();
        if (date < today) return Closed(date, "অতীতের তারিখ");
        if (date > today.AddDays(settings.MaxAdvanceDays)) return Closed(date, "এত আগে বুকিং নেওয়া হয় না");
        if (service.Availability != ServiceAvailability.Available) return Closed(date, "সেবাটি এখন পাওয়া যাচ্ছে না");

        var dow = BdTime.BdDayOfWeek(date);
        var hours = await db.BusinessHours.AsNoTracking()
            .Where(h => h.BusinessId == businessId && h.DayOfWeek == dow).OrderBy(h => h.OpensAt).ToListAsync(ct);
        if (hours.Count == 0) return Closed(date, "সাপ্তাহিক বন্ধ");

        var closures = await db.BusinessClosures.AsNoTracking()
            .Where(c => c.BusinessId == businessId && c.ClosedOn == date).ToListAsync(ct);
        var fullDay = closures.FirstOrDefault(c => c.StartTime == null);
        if (fullDay is not null) return Closed(date, fullDay.Reason ?? "ছুটি");

        var dayStart = BdTime.ToUtc(date, TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var busy = await db.Bookings.AsNoTracking()
            .Where(b => b.BusinessId == businessId && BlockingStatuses.Contains(b.Status) && b.StartAt < dayEnd && b.EndAt > dayStart)
            .Select(b => new { b.StartAt, b.EndAt })
            .ToListAsync(ct);

        var earliest = DateTime.UtcNow.AddMinutes(settings.MinNoticeMinutes);
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var buffer = TimeSpan.FromMinutes(settings.BufferMinutes);
        var step = TimeSpan.FromMinutes(settings.SlotIntervalMinutes);
        var slots = new List<SlotDto>();

        foreach (var h in hours)
        {
            for (var t = h.OpensAt; t.Add(duration) <= h.ClosesAt && t >= h.OpensAt; t = t.Add(step))
            {
                var start = BdTime.ToUtc(date, t);
                var end = start + duration;
                var localEnd = t.Add(duration);
                var inClosure = closures.Any(c => t < c.EndTime && localEnd > c.StartTime);
                var overlapping = busy.Count(b => b.StartAt < end + buffer && b.EndAt + buffer > start);
                var available = start >= earliest && !inClosure && overlapping < settings.MaxConcurrentBookings;
                slots.Add(new SlotDto(t, start, end, available));
                if (t.Add(step) < t) break; // wrapped past midnight
            }
        }
        return new DayAvailabilityDto(date, true, null, slots);
    }

    private static DayAvailabilityDto Closed(DateOnly date, string reason) => new(date, false, reason, []);
}
