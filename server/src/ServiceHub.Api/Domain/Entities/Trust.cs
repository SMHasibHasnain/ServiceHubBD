using System.Text.Json;

namespace ServiceHub.Api.Domain.Entities;

public class VerificationRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public Guid SubmittedBy { get; set; }
    public short AttemptNo { get; set; } = 1;
    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;
    public PriorityLevel Priority { get; set; } = PriorityLevel.Normal;
    public Guid? AssignedModeratorId { get; set; }
    public bool RequiresInspection { get; set; }
    /// <summary>Frozen copy of the profile at submission; moderators never edit the live business.</summary>
    public JsonDocument BusinessSnapshot { get; set; } = null!;
    public string? DecisionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewStartedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    public List<VerificationDocument> Documents { get; set; } = [];
}

public class VerificationDocument
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VerificationRequestId { get; set; }
    public DocumentType DocumentType { get; set; }
    public string FileUrl { get; set; } = "";
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public int? FileSizeBytes { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public string? ReviewerNote { get; set; }
    public DateTime UploadedAt { get; set; }
}

/// <summary>Field visit by a zone inspector. Inspectors report facts; moderators decide.</summary>
public class Inspection : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public int ZoneId { get; set; }
    public GeoZone Zone { get; set; } = null!;
    public InspectionType Type { get; set; }
    public Guid? VerificationRequestId { get; set; }
    public Guid? ReportId { get; set; }
    public Guid? InspectorId { get; set; }
    public Guid RequestedBy { get; set; }
    public InspectionStatus Status { get; set; } = InspectionStatus.Scheduled;
    public InspectionOutcome? Outcome { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime? VisitedAt { get; set; }
    public decimal? VisitLatitude { get; set; }
    public decimal? VisitLongitude { get; set; }
    public string? Findings { get; set; }
    public Dictionary<string, bool>? Checklist { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<InspectionPhoto> Photos { get; set; } = [];
}

/// <summary>Evidence photo taken by the inspector during a visit.</summary>
public class InspectionPhoto
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid InspectionId { get; set; }
    public string ImageUrl { get; set; } = "";
    public string? Caption { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Append-only moderator decision history. Every decision carries a reason.</summary>
public class ModerationAction
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ModeratorId { get; set; }
    public ModerationActionType Action { get; set; }
    public string Reason { get; set; } = "";
    public Guid? VerificationRequestId { get; set; }
    public Guid? BusinessId { get; set; }
    public Guid? ReviewId { get; set; }
    public Guid? ReportId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? InspectionId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Review : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Only completed bookings can be reviewed, once each.</summary>
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public Guid BusinessId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid CustomerId { get; set; }
    public User Customer { get; set; } = null!;
    public short Rating { get; set; }
    public string? Comment { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Published;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ReviewReply? Reply { get; set; }
    public List<ReviewPhoto> Photos { get; set; } = [];
}

/// <summary>Photo the customer attached to their review (uploaded by them).</summary>
public class ReviewPhoto
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReviewId { get; set; }
    public string ImageUrl { get; set; } = "";
    public short SortOrder { get; set; }
}

public class ReviewReply : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReviewId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Favourite
{
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class Report
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReporterId { get; set; }
    public Guid? ReporterBusinessId { get; set; }
    public ReportTarget TargetType { get; set; }
    public Guid? TargetBusinessId { get; set; }
    public Business? TargetBusiness { get; set; }
    public Guid? TargetReviewId { get; set; }
    public Review? TargetReview { get; set; }
    public Guid? TargetUserId { get; set; }
    /// <summary>Optional: one item of the business's public page the report is about (see <c>ReportContent</c>).</summary>
    public string? TargetContentType { get; set; }
    public Guid? TargetContentId { get; set; }
    public ReportReason Reason { get; set; }
    public string? Description { get; set; }
    public PriorityLevel Severity { get; set; } = PriorityLevel.Normal;
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public Guid? AssignedModeratorId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
