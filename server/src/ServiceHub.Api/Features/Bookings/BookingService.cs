using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Bookings;

public record FieldAnswer(Guid FieldId, string? Value, string? FileUrl);

public record CreateBookingCommand(Guid ServiceId, DateOnly Date, TimeOnly Time, string? CustomerName, string? CustomerPhone,
    string? ServiceAddress, string? Note, List<FieldAnswer>? Answers, string? ClientRequestId, BookingSource Source);

/// <summary>Booking creation and the booking state machine (see docs/database/ERD.md §11).</summary>
public class BookingService(AppDbContext db, AvailabilityService availability, Notifier notifier, ICurrentUser me)
{
    /// <summary>Allowed transitions. Every transition is written to booking_status_history.</summary>
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Transitions = new()
    {
        [BookingStatus.Pending] = [BookingStatus.Confirmed, BookingStatus.Rejected, BookingStatus.Cancelled],
        [BookingStatus.Confirmed] = [BookingStatus.Rescheduled, BookingStatus.Cancelled, BookingStatus.Completed, BookingStatus.NoShow],
        [BookingStatus.Rescheduled] = [BookingStatus.Confirmed, BookingStatus.Cancelled],
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        Transitions.TryGetValue(from, out var next) && next.Contains(to);

    public async Task<Booking> CreateAsync(CreateBookingCommand cmd)
    {
        var userId = me.RequireId();
        if (cmd.ClientRequestId is not null)
        {
            var existing = await db.Bookings.FirstOrDefaultAsync(b => b.CustomerUserId == userId && b.ClientRequestId == cmd.ClientRequestId);
            if (existing is not null) return existing; // duplicate submission → same booking
        }

        var service = await db.Services.Include(s => s.BookingFields).Include(s => s.Business).ThenInclude(b => b.BookingSettings)
            .FirstOrDefaultAsync(s => s.Id == cmd.ServiceId && s.Status == RecordStatus.Active)
            ?? throw new NotFoundException("সেবাটি পাওয়া যায়নি।", "service_not_found");
        var business = service.Business;
        if (business.Status != BusinessStatus.Published)
            throw new BadRequestException("এই ব্যবসাটি এখন বুকিং নিচ্ছে না।", "business_not_bookable");
        if (await db.BusinessMembers.AnyAsync(m => m.BusinessId == business.Id && m.UserId == userId && m.Status == MemberStatus.Active))
            throw new BadRequestException("নিজের ব্যবসায় গ্রাহক হিসেবে বুকিং করা যায় না।", "own_business");

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        var answers = ValidateAnswers(service, cmd.Answers ?? []);
        var phone = Phone.NormalizeBd(cmd.CustomerPhone) ?? user.Phone;
        var name = string.IsNullOrWhiteSpace(cmd.CustomerName) ? user.FullName : cmd.CustomerName.Trim();

        await using var tx = await db.Database.BeginTransactionAsync();
        // Serialize bookings per business so two customers can't take the last seat of a slot.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({business.Id.ToString()}, 0))");

        var day = await availability.GetAsync(business.Id, service.Id, cmd.Date);
        var slot = day.Slots.FirstOrDefault(s => s.Time == cmd.Time);
        if (slot is null)
            throw new BadRequestException("এই সময়ে বুকিং নেওয়া হয় না। অন্য সময় বেছে নিন।", "invalid_slot");
        if (!slot.Available)
            throw new ConflictException("দুঃখিত, এই সময়টি আর ফাঁকা নেই। অন্য সময় বেছে নিন।", "slot_taken");

        var customer = await UpsertCustomerAsync(business.Id, userId, name, phone, user.Email);
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('booking_number_seq') AS \"Value\"").SingleAsync();
        var autoConfirm = business.BookingSettings?.AutoConfirm == true;
        var booking = new Booking
        {
            BookingNumber = $"SH-{BdTime.Today():yy}-{seq:D6}",
            BusinessId = business.Id,
            ServiceId = service.Id,
            BusinessCustomer = customer,
            CustomerUserId = userId,
            ClientRequestId = cmd.ClientRequestId,
            StartAt = slot.StartAt,
            EndAt = slot.EndAt,
            Status = autoConfirm ? BookingStatus.Confirmed : BookingStatus.Pending,
            ConfirmedAt = autoConfirm ? DateTime.UtcNow : null,
            Source = cmd.Source,
            ServiceName = service.Name,
            DurationMinutes = service.DurationMinutes,
            PriceType = service.PriceType,
            QuotedPrice = service.Price,
            CustomerName = name,
            CustomerPhone = phone,
            ServiceAddress = cmd.ServiceAddress,
            CustomerNote = cmd.Note,
            FieldResponses = answers,
        };
        booking.History.Add(new BookingStatusHistory { ToStatus = booking.Status, ChangedBy = userId, Actor = ActorType.Customer });
        db.Bookings.Add(booking);

        customer.TotalBookings++;
        customer.FirstBookingAt ??= DateTime.UtcNow;
        if (customer.NextBookingAt is null || booking.StartAt < customer.NextBookingAt) customer.NextBookingAt = booking.StartAt;
        service.BookingCount++;
        service.LastBookedAt = DateTime.UtcNow;

        var when = $"{BdTime.ToLocal(booking.StartAt):dd MMM, hh:mm tt}";
        await notifier.NotifyBusinessAsync(business.Id, P.BookingsManage, "booking.created",
            "আপনার জন্য একটি নতুন বুকিং এসেছে।", $"{name} — {service.Name}, {when}",
            data: new() { ["bookingId"] = booking.Id.ToString() });
        notifier.Notify(userId, WorkspaceType.Customer, null, "booking.submitted",
            autoConfirm ? "আপনার বুকিং নিশ্চিত করা হয়েছে।" : "আপনার বুকিং সফলভাবে সম্পন্ন হয়েছে।",
            $"{business.Name} — {service.Name}, {when}", data: new() { ["bookingId"] = booking.Id.ToString() });

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return booking;
    }

    /// <summary>Applies a status change with all side effects (counters, CRM, notifications, history).</summary>
    public async Task TransitionAsync(Booking booking, BookingStatus to, ActorType actor, string? reason = null, decimal? finalPrice = null)
    {
        if (!CanTransition(booking.Status, to))
            throw new ConflictException("এই অবস্থায় এই কাজটি করা যাবে না।", "invalid_transition");
        if (to is BookingStatus.Rejected or BookingStatus.Cancelled && string.IsNullOrWhiteSpace(reason))
            throw new BadRequestException("কারণ লিখুন।", "reason_required");
        if (to is BookingStatus.Completed or BookingStatus.NoShow && booking.StartAt > DateTime.UtcNow)
            throw new ConflictException("অ্যাপয়েন্টমেন্টের সময় শুরু হওয়ার আগে এটি করা যাবে না।", "too_early");

        var from = booking.Status;
        booking.Status = to;
        var now = DateTime.UtcNow;
        var customer = await db.BusinessCustomers.FirstAsync(c => c.Id == booking.BusinessCustomerId);
        switch (to)
        {
            case BookingStatus.Confirmed:
                booking.ConfirmedAt ??= now;
                break;
            case BookingStatus.Completed:
                booking.CompletedAt = now;
                booking.FinalPrice = finalPrice ?? booking.QuotedPrice;
                customer.CompletedBookings++;
                customer.LastVisitAt = booking.StartAt;
                customer.LifetimeValue += booking.FinalPrice ?? 0;
                customer.FavouriteServiceId = await db.Bookings
                    .Where(b => b.BusinessCustomerId == customer.Id && (b.Status == BookingStatus.Completed || b.Id == booking.Id))
                    .GroupBy(b => b.ServiceId).OrderByDescending(g => g.Count()).Select(g => (Guid?)g.Key).FirstOrDefaultAsync();
                var business = await db.Businesses.FirstAsync(b => b.Id == booking.BusinessId);
                business.CompletedBookingCount++;
                break;
            case BookingStatus.Cancelled or BookingStatus.Rejected:
                booking.CancelledAt = now;
                booking.CancelledBy = actor;
                booking.CancellationReason = reason;
                break;
        }
        if (to is not (BookingStatus.Confirmed or BookingStatus.Rescheduled) && customer.NextBookingAt == booking.StartAt)
        {
            customer.NextBookingAt = await db.Bookings
                .Where(b => b.BusinessCustomerId == customer.Id && b.Id != booking.Id && b.StartAt > now &&
                            AvailabilityService.BlockingStatuses.Contains(b.Status))
                .OrderBy(b => b.StartAt).Select(b => (DateTime?)b.StartAt).FirstOrDefaultAsync();
        }

        db.BookingStatusHistory.Add(new BookingStatusHistory
        {
            BookingId = booking.Id, FromStatus = from, ToStatus = to, ChangedBy = me.Id, Actor = actor, Reason = reason,
        });

        var data = new Dictionary<string, string> { ["bookingId"] = booking.Id.ToString() };
        if (actor == ActorType.Customer)
        {
            await notifier.NotifyBusinessAsync(booking.BusinessId, P.BookingsManage, $"booking.{SnakeCase.FromEnum(to)}",
                "একটি বুকিং বাতিল করা হয়েছে।", $"{booking.CustomerName} — {booking.ServiceName}। কারণ: {reason}", data: data);
        }
        else if (booking.CustomerUserId is { } customerUserId)
        {
            var (type, title) = to switch
            {
                BookingStatus.Confirmed => ("booking.confirmed", "আপনার বুকিং নিশ্চিত করা হয়েছে।"),
                BookingStatus.Rejected => ("booking.rejected", "দুঃখিত, আপনার বুকিংটি গ্রহণ করা যায়নি।"),
                BookingStatus.Cancelled => ("booking.cancelled", "আপনার বুকিং বাতিল করা হয়েছে।"),
                BookingStatus.Completed => ("booking.review_request", "সেবাটি কেমন লেগেছে? একটি রিভিউ দিন।"),
                BookingStatus.NoShow => ("booking.no_show", "আপনি অ্যাপয়েন্টমেন্টে উপস্থিত ছিলেন না বলে চিহ্নিত হয়েছে।"),
                _ => ("booking.updated", "আপনার বুকিংয়ে পরিবর্তন হয়েছে।"),
            };
            notifier.Notify(customerUserId, WorkspaceType.Customer, null, type, title, $"{booking.ServiceName} — {booking.BookingNumber}", data: data);
        }
    }

    private async Task<BusinessCustomer> UpsertCustomerAsync(Guid businessId, Guid userId, string name, string phone, string? email)
    {
        var customer = await db.BusinessCustomers.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.UserId == userId)
                       ?? await db.BusinessCustomers.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.UserId == null && c.Phone == phone);
        if (customer is null)
        {
            customer = new BusinessCustomer { BusinessId = businessId, UserId = userId, DisplayName = name, Phone = phone, Email = email };
            db.BusinessCustomers.Add(customer);
        }
        else
        {
            customer.UserId ??= userId; // a walk-in record is linked once the person books online
        }
        return customer;
    }

    private static List<BookingFieldResponse> ValidateAnswers(Service service, List<FieldAnswer> answers)
    {
        var byId = answers.GroupBy(a => a.FieldId).ToDictionary(g => g.Key, g => g.Last());
        var unknown = byId.Keys.Except(service.BookingFields.Select(f => f.Id)).ToList();
        if (unknown.Count > 0) throw new BadRequestException("অজানা প্রশ্নের উত্তর পাঠানো হয়েছে।", "unknown_field");

        var result = new List<BookingFieldResponse>();
        foreach (var field in service.BookingFields.OrderBy(f => f.SortOrder))
        {
            byId.TryGetValue(field.Id, out var a);
            var empty = a is null || (string.IsNullOrWhiteSpace(a.Value) && string.IsNullOrWhiteSpace(a.FileUrl));
            if (empty)
            {
                if (field.IsRequired) throw new BadRequestException($"“{field.Label}” — এই তথ্যটি আবশ্যক।", "field_required");
                continue;
            }
            if (field.FieldType is BookingFieldType.Select && field.Options is not null && !field.Options.Contains(a!.Value!))
                throw new BadRequestException($"“{field.Label}” — তালিকা থেকে একটি অপশন বেছে নিন।", "invalid_option");
            if (field.FieldType is BookingFieldType.Phone && Phone.NormalizeBd(a!.Value) is null)
                throw new BadRequestException("সঠিক মোবাইল নম্বর লিখুন।", "invalid_phone");
            result.Add(new BookingFieldResponse { FieldId = field.Id, Label = field.Label, ValueText = a!.Value, FileUrl = a.FileUrl });
        }
        return result;
    }
}
