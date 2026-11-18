namespace ServiceHub.Api.Domain.Entities;

/// <summary>A business page: a workspace owned by a user, never an account of its own.</summary>
public class Business : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Tagline { get; set; }
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public short? EstablishedYear { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }
    /// <summary>Not exposed publicly (anti-disintermediation).</summary>
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? FacebookUrl { get; set; }
    public string? AddressLine { get; set; }
    public int? ZoneId { get; set; }
    public GeoZone? Zone { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? CoverageNote { get; set; }
    public List<string> Languages { get; set; } = [];
    public bool OffersHomeService { get; set; }
    public bool OffersEmergencyService { get; set; }
    public BusinessStatus Status { get; set; } = BusinessStatus.Draft;
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Draft;
    public short OnboardingStep { get; set; } = 1;
    public DateTime? PublishedAt { get; set; }
    public decimal AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public int CompletedBookingCount { get; set; }
    public int? AvgResponseMinutes { get; set; }
    public short? HealthScore { get; set; }
    public DateTime? HealthScoreUpdatedAt { get; set; }
    public Guid? OnboardedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<BusinessMember> Members { get; set; } = [];
    public List<BusinessHour> Hours { get; set; } = [];
    public List<Service> Services { get; set; } = [];
    public BusinessBookingSettings? BookingSettings { get; set; }
}

/// <summary>Page role of a person on one business (owner / manager / staff / page moderator / analyst).</summary>
public class BusinessMember
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public short RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string? Title { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Active;
    public Guid? InvitedBy { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public Guid? RemovedBy { get; set; }
}

public class BusinessInvitation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public short RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string? InviteePhone { get; set; }
    public string? InviteeEmail { get; set; }
    public Guid? InviteeId { get; set; }
    public string TokenHash { get; set; } = "";
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public Guid InvitedBy { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BusinessBookingSettings
{
    public Guid BusinessId { get; set; }
    public bool AutoConfirm { get; set; }
    public short SlotIntervalMinutes { get; set; } = 30;
    public int MinNoticeMinutes { get; set; } = 60;
    public short MaxAdvanceDays { get; set; } = 30;
    public short BufferMinutes { get; set; }
    public short MaxConcurrentBookings { get; set; } = 1;
    public string? CancellationPolicy { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Opening interval. Several rows per day allow split shifts. No row for a day = closed.</summary>
public class BusinessHour
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    /// <summary>0 = Saturday … 6 = Friday (Bangladesh week).</summary>
    public short DayOfWeek { get; set; }
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }
}

public class BusinessClosure
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    public DateOnly ClosedOn { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Service : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public PriceType PriceType { get; set; } = PriceType.Fixed;
    public decimal? Price { get; set; }
    public decimal? PriceMax { get; set; }
    public short DurationMinutes { get; set; }
    public ServiceAvailability Availability { get; set; } = ServiceAvailability.Available;
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public bool IsFeatured { get; set; }
    public short? FeaturedOrder { get; set; }
    public string? PreparationNotes { get; set; }
    public string? Policies { get; set; }
    public string? InternalNotes { get; set; }
    public int BookingCount { get; set; }
    public decimal AvgRating { get; set; }
    public int ViewCount { get; set; }
    public DateTime? LastBookedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<ServiceBookingField> BookingFields { get; set; } = [];
}

/// <summary>Dynamic booking form question defined by the business for one service.</summary>
public class ServiceBookingField
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ServiceId { get; set; }
    public string Label { get; set; } = "";
    public BookingFieldType FieldType { get; set; }
    public List<string>? Options { get; set; }
    public string? Placeholder { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
}
