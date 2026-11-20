// Mirrors the server DTOs (server/src/ServiceHub.Api/Features/**). Enums are snake_case strings.

export type Guid = string;
export type IsoDateTime = string; // UTC ISO-8601
export type IsoDate = string; // yyyy-MM-dd (Bangladesh local)
export type TimeStr = string; // HH:mm:ss

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

// ---------- auth / me ----------
export type WorkspaceType = "customer" | "business" | "moderation" | "inspection" | "marketing" | "administration";

export interface TokenPair {
  accessToken: string;
  accessTokenExpiresAt: IsoDateTime;
  refreshToken: string;
  refreshTokenExpiresAt: IsoDateTime;
}

export interface User {
  id: Guid;
  fullName: string;
  phone: string;
  email: string | null;
  avatarUrl: string | null;
  phoneVerified: boolean;
  lastWorkspaceType: WorkspaceType;
  lastActiveBusinessId: Guid | null;
}

export interface AuthResponse {
  user: User;
  tokens: TokenPair;
}

export type VerificationStatus = "draft" | "submitted" | "under_review" | "info_requested" | "approved" | "rejected";

export interface Workspace {
  type: WorkspaceType;
  name: string;
  subtitle: string;
  businessId: Guid | null;
  roleKey: string | null;
  roleName: string | null;
  verificationStatus: VerificationStatus | null;
  zoneId: number | null;
  unreadNotifications: number;
  permissions: string[];
}

export interface NotificationItem {
  id: Guid;
  workspace: WorkspaceType;
  businessId: Guid | null;
  type: string;
  title: string;
  body: string | null;
  actionUrl: string | null;
  data: Record<string, string> | null;
  readAt: IsoDateTime | null;
  createdAt: IsoDateTime;
}

// ---------- taxonomy ----------
export type ZoneLevel = "division" | "district" | "city_corporation" | "upazila" | "thana" | "pourashava" | "union" | "ward";

export interface Zone {
  id: number;
  parentId: number | null;
  level: ZoneLevel;
  depth: number;
  nameBn: string;
  nameEn: string;
  slug: string;
  path: string;
}

export interface Category {
  id: number;
  parentId: number | null;
  nameBn: string;
  nameEn: string;
  slug: string;
  icon: string | null;
  sortOrder: number;
  status: string;
  children: Category[];
}

/** Admin view of a category: every status, with how much uses it. */
export interface AdminCategory {
  id: number;
  parentId: number | null;
  nameBn: string;
  nameEn: string;
  slug: string;
  icon: string | null;
  sortOrder: number;
  status: RecordStatus;
  businessCount: number;
  serviceCount: number;
  childCount: number;
}

// ---------- businesses ----------
export type BusinessStatus = "draft" | "published" | "suspended" | "deactivated";
export type PriceType = "fixed" | "starting_from" | "contact_for_price" | "range";
export type ServiceAvailability = "available" | "unavailable" | "temporarily_closed" | "seasonal";
export type RecordStatus = "active" | "inactive" | "archived";
export type BookingFieldType = "short_text" | "long_text" | "number" | "phone" | "address" | "select" | "multi_select" | "date" | "image";

export interface Hour {
  dayOfWeek: number; // 0 = Saturday … 6 = Friday
  opensAt: TimeStr;
  closesAt: TimeStr;
}

export interface BookingSettings {
  autoConfirm: boolean;
  slotIntervalMinutes: number;
  minNoticeMinutes: number;
  maxAdvanceDays: number;
  bufferMinutes: number;
  maxConcurrentBookings: number;
  cancellationPolicy: string | null;
}

export interface BusinessDetail {
  id: Guid;
  name: string;
  slug: string;
  categoryId: number;
  categoryName: string;
  tagline: string | null;
  shortDescription: string | null;
  description: string | null;
  establishedYear: number | null;
  logoUrl: string | null;
  coverUrl: string | null;
  phone: string | null;
  email: string | null;
  websiteUrl: string | null;
  facebookUrl: string | null;
  addressLine: string | null;
  zoneId: number | null;
  zoneName: string | null;
  latitude: number | null;
  longitude: number | null;
  coverageNote: string | null;
  languages: string[];
  offersHomeService: boolean;
  offersEmergencyService: boolean;
  status: BusinessStatus;
  verificationStatus: VerificationStatus;
  onboardingStep: number;
  avgRating: number;
  reviewCount: number;
  completedBookingCount: number;
  hours: Hour[];
  bookingSettings: BookingSettings;
  myRole: string;
  myPermissions: string[];
  /** Set when platform staff (not a member) opened this business: the platform role that let them in. */
  staffRole?: string | null;
}

export interface BookingField {
  id: Guid | null;
  label: string;
  fieldType: BookingFieldType;
  options: string[] | null;
  placeholder: string | null;
  isRequired: boolean;
  sortOrder: number;
}

export interface Service {
  id: Guid;
  businessId: Guid;
  name: string;
  slug: string;
  categoryId: number;
  shortDescription: string | null;
  description: string | null;
  priceType: PriceType;
  price: number | null;
  priceMax: number | null;
  durationMinutes: number;
  availability: ServiceAvailability;
  status: RecordStatus;
  isFeatured: boolean;
  preparationNotes: string | null;
  policies: string | null;
  internalNotes: string | null;
  bookingCount: number;
  avgRating: number;
  lastBookedAt: IsoDateTime | null;
  bookingFields: BookingField[];
  imageUrl: string | null;
}

export interface BusinessCard {
  id: Guid;
  name: string;
  slug: string;
  logoUrl: string | null;
  coverUrl: string | null;
  categoryId: number;
  categoryName: string;
  zoneName: string | null;
  avgRating: number;
  reviewCount: number;
  startingPrice: number | null;
  offersHomeService: boolean;
  offersEmergencyService: boolean;
  isVerified: boolean;
}

export interface PublicReview {
  id: Guid;
  customerName: string;
  serviceName: string;
  rating: number;
  comment: string | null;
  createdAt: IsoDateTime;
  replyBody: string | null;
  repliedAt: IsoDateTime | null;
  photos: string[];
}

export interface PublicBusiness {
  id: Guid;
  name: string;
  slug: string;
  tagline: string | null;
  shortDescription: string | null;
  description: string | null;
  logoUrl: string | null;
  coverUrl: string | null;
  categoryId: number;
  categoryName: string;
  addressLine: string | null;
  zoneName: string | null;
  coverageNote: string | null;
  languages: string[];
  establishedYear: number | null;
  offersHomeService: boolean;
  offersEmergencyService: boolean;
  avgRating: number;
  reviewCount: number;
  completedBookingCount: number;
  avgResponseMinutes: number | null;
  cancellationPolicy: string | null;
  hours: Hour[];
  services: Service[];
  ratingDistribution: Record<string, number>;
  latestReviews: PublicReview[];
  themeKey: ThemeKey;
  headerStyle: HeaderStyle;
  /** Visible sections, in the owner's order. */
  sections: SectionKey[];
  notices: PublicNotice[];
  faqs: Faq[];
  highlights: Highlight[];
  gallery: GalleryItem[];
  team: PublicTeamMember[];
  /** Always true on public pages; false in the owner's preview of an unverified page. */
  isVerified: boolean;
}

// ---------- Public page customisation ----------
export type ThemeKey = "blue" | "navy" | "teal" | "green" | "violet" | "rose" | "orange" | "slate";
export type HeaderStyle = "cover" | "color";
export type SectionKey = "notices" | "highlights" | "services" | "gallery" | "reviews" | "faqs" | "team" | "about";
export interface PageSection { key: SectionKey; visible: boolean }
export interface PublicNotice { id: Guid; title: string; body: string; isPinned: boolean; createdAt: IsoDateTime; expiresAt: IsoDateTime | null }
export interface Notice extends PublicNotice { isPublished: boolean; updatedAt: IsoDateTime; isLive: boolean }
export interface Faq { id: Guid | null; question: string; answer: string }
export interface Highlight { id: Guid | null; iconKey: string; title: string; description: string | null }
export interface GalleryItem { id: Guid | null; imageUrl: string; caption: string | null }
export interface PublicTeamMember { name: string; title: string | null; avatarUrl: string | null; roleName: string }
export interface TeamMember { memberId: Guid; name: string; title: string | null; avatarUrl: string | null; roleName: string; showOnProfile: boolean }
export interface PageSettings {
  themeKey: ThemeKey; headerStyle: HeaderStyle; sections: PageSection[]; notices: Notice[];
  faqs: Faq[]; highlights: Highlight[]; gallery: GalleryItem[]; team: TeamMember[];
}

export interface Slot {
  time: TimeStr;
  startAt: IsoDateTime;
  endAt: IsoDateTime;
  available: boolean;
}

export interface DayAvailability {
  date: IsoDate;
  isOpen: boolean;
  closedReason: string | null;
  slots: Slot[];
}

export interface VerificationSummary {
  id: Guid;
  attemptNo: number;
  status: VerificationStatus;
  requiresInspection: boolean;
  decisionReason: string | null;
  submittedAt: IsoDateTime;
  decidedAt: IsoDateTime | null;
  documentTypes: DocumentType[];
}

export type DocumentType = "national_id" | "trade_license" | "tin_certificate" | "bin_certificate" | "professional_certificate" | "utility_bill" | "other";

// ---------- bookings ----------
export type BookingStatus = "pending" | "confirmed" | "rejected" | "rescheduled" | "completed" | "cancelled" | "no_show";
export type ActorType = "customer" | "business" | "moderator" | "admin" | "system";

export interface Booking {
  id: Guid;
  bookingNumber: string;
  businessId: Guid;
  businessName: string;
  businessSlug: string;
  serviceId: Guid;
  serviceName: string;
  startAt: IsoDateTime;
  endAt: IsoDateTime;
  status: BookingStatus;
  source: string;
  priceType: PriceType;
  quotedPrice: number | null;
  finalPrice: number | null;
  durationMinutes: number;
  customerName: string;
  customerPhone: string;
  serviceAddress: string | null;
  customerNote: string | null;
  businessNote: string | null;
  businessCustomerId: Guid;
  assignedTo: { memberId: Guid; userId: Guid; name: string } | null;
  cancellationReason: string | null;
  hasReview: boolean;
  answers: { label: string; value: string | null; fileUrl: string | null }[];
  history: { from: BookingStatus | null; to: BookingStatus; actor: ActorType; reason: string | null; at: IsoDateTime }[];
  /** Field progress set by the assigned staff ("done" is status completed). */
  progress: WorkProgress | null;
  progressAt: IsoDateTime | null;
  /** The latest reschedule request: pending, or the last decision. */
  reschedule: RescheduleRequest | null;
}

export type WorkProgress = "on_the_way" | "started";
export type RescheduleStatus = "pending" | "accepted" | "rejected" | "cancelled";
export interface RescheduleRequest {
  id: Guid; proposedStartAt: IsoDateTime; proposedEndAt: IsoDateTime; previousStartAt: IsoDateTime; reason: string | null;
  status: RescheduleStatus; responseNote: string | null; createdAt: IsoDateTime; respondedAt: IsoDateTime | null;
}

// ---------- staff day & leave ----------
export type LeaveStatus = "pending" | "approved" | "rejected" | "cancelled";
export interface Leave {
  id: Guid; memberId: Guid; memberName: string; startDate: IsoDate; endDate: IsoDate; reason: string | null;
  status: LeaveStatus; decisionNote: string | null; createdAt: IsoDateTime; decidedAt: IsoDateTime | null;
}
export interface MyDay { date: IsoDate; bookings: Booking[]; leave: Leave | null }

// ---------- CRM ----------
export interface Customer {
  id: Guid;
  userId: Guid | null;
  displayName: string;
  phone: string;
  email: string | null;
  isVip: boolean;
  tags: string[];
  badges: ("vip" | "repeat" | "new" | "inactive")[];
  firstBookingAt: IsoDateTime | null;
  lastVisitAt: IsoDateTime | null;
  nextBookingAt: IsoDateTime | null;
  totalBookings: number;
  completedBookings: number;
  lifetimeValue: number;
  favouriteService: string | null;
}

export interface CustomerDetail {
  customer: Customer;
  notes: { id: Guid; body: string; isPinned: boolean; authorName: string; createdAt: IsoDateTime }[];
  bookings: { bookingId: Guid; bookingNumber: string; serviceName: string; startAt: IsoDateTime; status: BookingStatus; finalPrice: number | null }[];
}

// ---------- reviews ----------
export interface ManagedReview {
  id: Guid;
  bookingId: Guid;
  customerName: string;
  serviceName: string;
  rating: number;
  comment: string | null;
  status: string;
  createdAt: IsoDateTime;
  reply: string | null;
  repliedAt: IsoDateTime | null;
  photos: string[];
}

// ---------- team ----------
export interface Role {
  id: number;
  key: string;
  scope: "platform" | "zone" | "business";
  nameBn: string;
  nameEn: string;
  rank: number;
  permissions: string[];
}

export interface Member {
  id: Guid;
  userId: Guid;
  fullName: string;
  phone: string;
  roleKey: string;
  roleName: string;
  title: string | null;
  status: "invited" | "active" | "suspended" | "removed";
  joinedAt: IsoDateTime;
}

export interface Invitation {
  id: Guid;
  businessId: Guid;
  businessName: string;
  inviteePhone: string | null;
  roleKey: string;
  roleName: string;
  status: string;
  expiresAt: IsoDateTime;
  createdAt: IsoDateTime;
}

// ---------- moderation ----------
export type PriorityLevel = "low" | "normal" | "high" | "urgent";

export interface QueueItem {
  id: Guid;
  businessId: Guid;
  businessName: string;
  logoUrl: string | null;
  ownerName: string;
  categoryName: string;
  zoneName: string | null;
  attemptNo: number;
  status: VerificationStatus;
  priority: PriorityLevel;
  requiresInspection: boolean;
  assignedModeratorId: Guid | null;
  submittedAt: IsoDateTime;
}

export interface VerificationDetail {
  request: QueueItem;
  snapshot: Record<string, unknown>;
  decisionReason: string | null;
  documents: { id: Guid; documentType: DocumentType; fileUrl: string; fileName: string | null; status: string; reviewerNote: string | null }[];
  inspections: { id: Guid; status: InspectionStatus; outcome: InspectionOutcome | null; findings: string | null; inspectorName: string | null; visitedAt: IsoDateTime | null; photos: InspectionPhoto[] }[];
  previousAttempts: QueueItem[];
  history: { id: Guid; action: string; reason: string; moderatorName: string; createdAt: IsoDateTime }[];
}

export interface ReportItem {
  id: Guid;
  targetType: "business" | "review" | "user";
  targetBusinessId: Guid | null;
  targetBusinessName: string | null;
  targetReviewId: Guid | null;
  reviewComment: string | null;
  targetUserId: Guid | null;
  /** The reported notice or gallery photo, as it is now (removed = already deleted). */
  content: { type: "notice" | "gallery_item"; id: Guid; removed: boolean; title: string | null; text: string | null; imageUrl: string | null } | null;
  reason: string;
  description: string | null;
  severity: PriorityLevel;
  status: string;
  sameTargetReports: number;
  createdAt: IsoDateTime;
}

// ---------- inspections ----------
export type InspectionStatus = "scheduled" | "in_progress" | "completed" | "cancelled";
export type InspectionOutcome = "passed" | "failed" | "needs_followup" | "business_not_found";

export interface InspectionItem {
  id: Guid;
  type: "verification_visit" | "complaint_followup" | "routine_audit";
  status: InspectionStatus;
  outcome: InspectionOutcome | null;
  businessId: Guid;
  businessName: string;
  addressLine: string | null;
  businessPhone: string | null;
  zoneId: number;
  zoneName: string;
  verificationRequestId: Guid | null;
  reportId: Guid | null;
  inspectorId: Guid | null;
  scheduledFor: IsoDateTime | null;
  visitedAt: IsoDateTime | null;
  findings: string | null;
  checklist: Record<string, boolean> | null;
  createdAt: IsoDateTime;
  latitude: number | null;
  longitude: number | null;
  photos: InspectionPhoto[];
}

export interface InspectionPhoto { id: Guid; imageUrl: string; caption: string | null }

// ---------- admin ----------
export interface PlatformStats {
  totalUsers: number;
  totalBusinesses: number;
  publishedBusinesses: number;
  pendingVerifications: number;
  bookingsToday: number;
  newRegistrationsToday: number;
  openReports: number;
}

export interface AdminUser {
  id: Guid;
  fullName: string;
  phone: string;
  email: string | null;
  status: "active" | "suspended" | "deactivated";
  createdAt: IsoDateTime;
  lastLoginAt: IsoDateTime | null;
  businessesOwned: number;
  platformRoles: string[];
}

export interface Assignment {
  id: Guid;
  userId: Guid;
  userName: string;
  roleKey: string;
  roleName: string;
  zoneId: number | null;
  zoneName: string | null;
  categoryId: number | null;
  categoryName: string | null;
  validFrom: IsoDateTime;
  validUntil: IsoDateTime | null;
  revokedAt: IsoDateTime | null;
}

export interface AuditLogItem {
  id: number;
  actorId: Guid | null;
  actorName: string | null;
  scope: string | null;
  businessId: Guid | null;
  zoneId: number | null;
  action: string;
  entityType: string;
  entityId: string;
  changes: Record<string, string | null> | null;
  reason: string | null;
  createdAt: IsoDateTime;
}

// ---------- Analytics ----------

export interface Metric { value: number; previous: number | null }
export interface SeriesPoint { date: string; values: number[] }
export interface TimeSeries { keys: string[]; points: SeriesPoint[] }
export interface Slice { key: string; label: string; value: number; secondary: number | null }

interface PeriodInfo { days: number; from: string; to: string; kpis: Record<string, Metric> }

export interface BusinessAnalytics extends PeriodInfo {
  trend: TimeSeries; statusMix: Slice[]; topServices: Slice[]; staff: Slice[];
  heatmap: number[][]; ratings: Slice[]; sources: Slice[];
}
export interface ModerationAnalytics extends PeriodInfo {
  trend: TimeSeries; queue: Slice[]; aging: Slice[]; outcomes: Slice[]; reportReasons: Slice[];
  moderators: Slice[]; pendingByCategory: Slice[];
}
export interface InspectionAnalytics extends PeriodInfo {
  trend: TimeSeries; outcomes: Slice[]; byZone: Slice[]; byType: Slice[];
}
export interface AdminAnalytics extends PeriodInfo {
  growth: TimeSeries; bookings: TimeSeries; funnel: Slice[]; byCategory: Slice[]; byDivision: Slice[];
  statusMix: Slice[]; topBusinesses: Slice[]; roles: Slice[];
}
export interface CustomerStats {
  totalBookings: number; completed: number; upcoming: number; cancelled: number; totalSpent: number;
  reviewsGiven: number; businessesUsed: number; topCategory: string | null;
}

// ---------- Subscriptions ----------

export type SubscriptionStatus = "trialing" | "active" | "past_due" | "cancelled" | "expired";
export type BillingCycle = "monthly" | "yearly";
export type InvoiceStatus = "open" | "pending_verification" | "paid" | "void";
export type PaymentMethod = "bkash" | "nagad" | "bank_transfer" | "cash" | "complimentary";
export type PaymentClaimStatus = "pending" | "verified" | "rejected";

export interface Plan { key: string; nameBn: string; nameEn: string; taglineBn: string | null; priceMonthly: number; priceYearly: number; entitlements: Record<string, number> }
export interface PlanFeature { key: string; labelBn: string; kind: "limit" | "switch"; unitBn: string | null }
export interface PaymentAccount { method: PaymentMethod; label: string; number: string; accountName: string | null; instructions: string | null }
export interface PlansResponse { plans: Plan[]; features: PlanFeature[]; trialDays: number; graceDays: number; paymentAccounts: PaymentAccount[] }

export interface BusinessPlan {
  planKey: string; planName: string; status: SubscriptionStatus | null; cycle: BillingCycle | null;
  trialEndsAt: IsoDateTime | null; currentPeriodEnd: IsoDateTime | null; graceEndsAt: IsoDateTime | null;
  cancelAtPeriodEnd: boolean; isPaid: boolean; isTrial: boolean; trialUsed: boolean;
  limits: Record<string, number>; usage: { services: number; members: number };
}

export interface PaymentClaim {
  id: Guid; method: PaymentMethod; amount: number; transactionId: string | null; senderNumber: string | null;
  status: PaymentClaimStatus; createdAt: IsoDateTime; reviewedAt: IsoDateTime | null; reviewNote: string | null;
}
export interface Invoice {
  id: Guid; number: string; planKey: string; planName: string; cycle: BillingCycle; subtotal: number; credit: number; total: number;
  status: InvoiceStatus; periodStart: IsoDateTime | null; periodEnd: IsoDateTime | null; createdAt: IsoDateTime; paidAt: IsoDateTime | null;
  payments: PaymentClaim[];
}

export interface AdminPayment {
  id: Guid; businessId: Guid; businessName: string; invoiceNumber: string; planName: string; cycle: BillingCycle; invoiceTotal: number;
  method: PaymentMethod; amount: number; transactionId: string | null; senderNumber: string | null; status: PaymentClaimStatus;
  submittedByName: string; createdAt: IsoDateTime; reviewedAt: IsoDateTime | null; reviewNote: string | null;
}
export interface AdminSubscription {
  businessId: Guid; businessName: string; slug: string; businessStatus: string; ownerName: string; planKey: string; planName: string;
  status: SubscriptionStatus | null; cycle: BillingCycle | null; trialEndsAt: IsoDateTime | null; currentPeriodEnd: IsoDateTime | null;
  cancelAtPeriodEnd: boolean;
}
export interface BillingSummary { mrr: number; activePaid: number; trialing: number; pastDue: number; pendingPayments: number; revenue30d: number; paidByPlan: Record<string, number> }

// ---------- Platform staff across businesses ----------

export interface StaffBusiness {
  id: Guid; name: string; slug: string; categoryName: string; zoneName: string | null; status: BusinessStatus;
  verificationStatus: VerificationStatus; ownerName: string; createdAt: IsoDateTime; deletedAt: IsoDateTime | null;
  lastStaffEditAt: IsoDateTime | null;
}

export interface StaffAction {
  id: number; businessId: Guid; businessName: string; actorId: Guid; actorName: string; roleKey: string; roleName: string;
  summary: string; createdAt: IsoDateTime; undoneAt: IsoDateTime | null; undoneByName: string | null; undoReason: string | null;
  undoOf: number | null; rowCount: number;
}

export interface StaffRowChange {
  entity: string; entityLabel: string; kind: "added" | "removed" | "updated"; title: string | null;
  fields: { field: string; before: string | null; after: string | null }[];
}

export interface StaffActionDetail { action: StaffAction; rows: StaffRowChange[] }

export interface PageDefaults { themeKey: ThemeKey; headerStyle: HeaderStyle; sections: PageSection[] }
