namespace ServiceHub.Api.Domain.Entities;

/// <summary>A sellable plan. Keys and entitlements are synced from <c>Billing.PlanCatalog</c>; prices are editable.</summary>
public class SubscriptionPlan : ITimestamped
{
    public short Id { get; set; }
    public string Key { get; set; } = "";
    public string NameBn { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? TaglineBn { get; set; }
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public short SortOrder { get; set; }
    public bool IsPublic { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<PlanEntitlement> Entitlements { get; set; } = [];
}

/// <summary>One limit or switch of a plan. -1 = unlimited; 0/1 for on/off features.</summary>
public class PlanEntitlement
{
    public short PlanId { get; set; }
    public string Key { get; set; } = "";
    public int Value { get; set; }
}

/// <summary>A business's current subscription (one row per business; history lives in subscription_events).</summary>
public class BusinessSubscription : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public short PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;
    public SubscriptionStatus Status { get; set; }
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SubscriptionInvoice : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Number { get; set; } = "";
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public short PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;
    public BillingCycle Cycle { get; set; }
    public decimal Subtotal { get; set; }
    /// <summary>Unused value of the previous paid period, credited when switching plans.</summary>
    public decimal Credit { get; set; }
    public decimal Total { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<SubscriptionPayment> Payments { get; set; } = [];
}

/// <summary>
/// A payment claim against an invoice. Mobile-money and bank payments are submitted with a transaction id
/// and verified by platform staff; cash/complimentary grants are recorded as already verified.
/// </summary>
public class SubscriptionPayment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid InvoiceId { get; set; }
    public SubscriptionInvoice Invoice { get; set; } = null!;
    public SubscriptionPaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? TransactionId { get; set; }
    public string? SenderNumber { get; set; }
    public string? Note { get; set; }
    public SubscriptionPaymentStatus Status { get; set; } = SubscriptionPaymentStatus.Pending;
    public Guid SubmittedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}

/// <summary>Append-only history: trial started, plan changed, payment verified, expired, reminders sent…</summary>
public class SubscriptionEvent
{
    public long Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Type { get; set; } = "";
    public string? FromPlan { get; set; }
    public string? ToPlan { get; set; }
    public Guid? ActorId { get; set; }
    public Dictionary<string, string?>? Data { get; set; }
    public DateTime CreatedAt { get; set; }
}
