namespace ServiceHub.Api.Domain;

// Enum values are stored and serialized in snake_case (e.g. InfoRequested -> "info_requested"),
// matching docs/database/schema.dbml.

public enum AccountStatus { Active, Suspended, Deactivated }

public enum OtpPurpose { PhoneVerification, EmailVerification, PasswordReset }

public enum RoleScope { Platform, Zone, Business }

public enum WorkspaceType { Customer, Business, Moderation, Inspection, Marketing, Administration }

public enum MemberStatus { Invited, Active, Suspended, Removed }

public enum InvitationStatus { Pending, Accepted, Declined, Expired, Revoked }

public enum ZoneLevel { Division, District, CityCorporation, Upazila, Thana, Pourashava, Union, Ward }

public enum RecordStatus { Active, Inactive, Archived }

public enum BusinessStatus { Draft, Published, Suspended, Deactivated }

public enum VerificationStatus { Draft, Submitted, UnderReview, InfoRequested, Approved, Rejected }

public enum PriorityLevel { Low, Normal, High, Urgent }

public enum DocumentType { NationalId, TradeLicense, TinCertificate, BinCertificate, ProfessionalCertificate, UtilityBill, Other }

public enum DocumentStatus { Pending, Accepted, Rejected }

public enum InspectionType { VerificationVisit, ComplaintFollowup, RoutineAudit }

public enum InspectionStatus { Scheduled, InProgress, Completed, Cancelled }

public enum InspectionOutcome { Passed, Failed, NeedsFollowup, BusinessNotFound }

public enum PriceType { Fixed, StartingFrom, ContactForPrice, Range }

public enum ServiceAvailability { Available, Unavailable, TemporarilyClosed, Seasonal }

public enum BookingFieldType { ShortText, LongText, Number, Phone, Address, Select, MultiSelect, Date, Image }

public enum BookingStatus { Pending, Confirmed, Rejected, Rescheduled, Completed, Cancelled, NoShow }

public enum BookingSource { Web, MobileApp, SharedLink, Promotion, Manual }

public enum ActorType { Customer, Business, Moderator, Admin, System }

public enum PaymentStatus { NotApplicable, Unpaid, PartiallyPaid, Paid, Refunded }

public enum ReviewStatus { Published, Hidden, Removed }

public enum ReportTarget { Business, Review, User }

public enum ReportReason { FakeBusiness, Spam, Fraud, AbusiveBehaviour, MisleadingInformation, Other }

public enum ReportStatus { Open, UnderReview, Resolved, Dismissed, Escalated }

public enum ModerationActionType
{
    StartReview, ApproveBusiness, RejectBusiness, RequestInformation, SetPriority,
    AcceptDocument, RejectDocument, ApproveReview, HideReview, RemoveReview, RestoreReview,
    SuspendBusiness, ReinstateBusiness, ResolveReport, DismissReport, EscalateReport,
    RequestInspection
}
