namespace ServiceHub.Api.Domain.Entities;

/// <summary>Marks entities whose created_at / updated_at are maintained by AppDbContext.</summary>
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One identity per person. Every account is a customer; all other capabilities come from
/// business_members (business-scoped roles) and platform_role_assignments (platform/zone roles).
/// </summary>
public class User : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public string? AddressLine { get; set; }
    public int? ZoneId { get; set; }
    public GeoZone? Zone { get; set; }
    public WorkspaceType LastWorkspaceType { get; set; } = WorkspaceType.Customer;
    public Guid? LastActiveBusinessId { get; set; }
    public bool MfaEnabled { get; set; }
    public DateTime? PhoneVerifiedAt { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<BusinessMember> Memberships { get; set; } = [];
    public List<PlatformRoleAssignment> RoleAssignments { get; set; } = [];
}

public class UserSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string RefreshTokenHash { get; set; } = "";
    public string? DeviceName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool RememberMe { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class VerificationCode
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string Target { get; set; } = "";
    public OtpPurpose Purpose { get; set; }
    public string CodeHash { get; set; } = "";
    public short Attempts { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
