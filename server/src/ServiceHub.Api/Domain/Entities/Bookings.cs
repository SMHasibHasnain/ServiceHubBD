namespace ServiceHub.Api.Domain.Entities;

/// <summary>Per-workspace CRM record. One platform user is a separate customer in each business.</summary>
public class BusinessCustomer : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    /// <summary>NULL for walk-in / manually added customers.</summary>
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public bool IsVip { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTime? FirstBookingAt { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public DateTime? NextBookingAt { get; set; }
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public decimal LifetimeValue { get; set; }
    public Guid? FavouriteServiceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<CustomerNote> Notes { get; set; } = [];
}

public class CustomerNote : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessCustomerId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Booking : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string BookingNumber { get; set; } = "";
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public Guid ServiceId { get; set; }
    public Service Service { get; set; } = null!;
    public Guid BusinessCustomerId { get; set; }
    public BusinessCustomer BusinessCustomer { get; set; } = null!;
    public Guid? CustomerUserId { get; set; }
    public Guid? AssignedMemberId { get; set; }
    public BusinessMember? AssignedMember { get; set; }
    /// <summary>Client-supplied idempotency key; prevents duplicate submissions.</summary>
    public string? ClientRequestId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public BookingSource Source { get; set; } = BookingSource.Web;
    // Snapshot of the service at booking time.
    public string ServiceName { get; set; } = "";
    public short DurationMinutes { get; set; }
    public PriceType PriceType { get; set; }
    public decimal? QuotedPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal? FinalPrice { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string? ServiceAddress { get; set; }
    public string? CustomerNote { get; set; }
    public string? BusinessNote { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.NotApplicable;
    public WorkProgress? Progress { get; set; }
    public DateTime? ProgressAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public ActorType? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<BookingFieldResponse> FieldResponses { get; set; } = [];
    public List<BookingStatusHistory> History { get; set; } = [];
    public List<BookingRescheduleRequest> RescheduleRequests { get; set; } = [];
}

/// <summary>A customer's request to move a booking to another slot; the business accepts or rejects it.</summary>
public class BookingRescheduleRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BookingId { get; set; }
    public Guid RequestedBy { get; set; }
    public DateTime ProposedStartAt { get; set; }
    public DateTime ProposedEndAt { get; set; }
    /// <summary>Where the booking was when the request was made (for the history and notifications).</summary>
    public DateTime PreviousStartAt { get; set; }
    public string? Reason { get; set; }
    public RescheduleStatus Status { get; set; } = RescheduleStatus.Pending;
    public string? ResponseNote { get; set; }
    public Guid? RespondedBy { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BookingFieldResponse
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BookingId { get; set; }
    public Guid? FieldId { get; set; }
    public string Label { get; set; } = "";
    public string? ValueText { get; set; }
    public string? FileUrl { get; set; }
}

/// <summary>Append-only; powers the booking timeline.</summary>
public class BookingStatusHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BookingId { get; set; }
    public BookingStatus? FromStatus { get; set; }
    public BookingStatus ToStatus { get; set; }
    public Guid? ChangedBy { get; set; }
    public ActorType Actor { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RecipientId { get; set; }
    public WorkspaceType Workspace { get; set; } = WorkspaceType.Customer;
    public Guid? BusinessId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? ActionUrl { get; set; }
    public Dictionary<string, string>? Data { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public short? RoleId { get; set; }
    public RoleScope? Scope { get; set; }
    public Guid? BusinessId { get; set; }
    public int? ZoneId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public Dictionary<string, string?>? Changes { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
