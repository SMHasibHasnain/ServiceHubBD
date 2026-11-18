using Microsoft.EntityFrameworkCore;

namespace ServiceHub.Api.Domain.Entities;

public class Role : ITimestamped
{
    public short Id { get; set; }
    public string Key { get; set; } = "";
    public RoleScope Scope { get; set; }
    /// <summary>NULL = system role. Set = custom role defined by one business (future).</summary>
    public Guid? BusinessId { get; set; }
    public string NameBn { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystem { get; set; } = true;
    /// <summary>Higher rank can manage lower rank within the same scope.</summary>
    public short Rank { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<RolePermission> RolePermissions { get; set; } = [];
}

public class Permission
{
    public short Id { get; set; }
    public string Key { get; set; } = "";
    public RoleScope Scope { get; set; }
    public string Module { get; set; } = "";
    public string? Description { get; set; }
}

public class RolePermission
{
    public short RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public short PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}

/// <summary>Platform- or zone-scoped role held by a user (admin, moderator, inspector, marketer...).</summary>
public class PlatformRoleAssignment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public short RoleId { get; set; }
    public Role Role { get; set; } = null!;
    /// <summary>Required for zone-scoped roles; grants access to the whole subtree.</summary>
    public int? ZoneId { get; set; }
    public GeoZone? Zone { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public Guid GrantedBy { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Bangladesh administrative hierarchy as one tree (division → district → upazila/thana → union/ward).</summary>
public class GeoZone
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public GeoZone? Parent { get; set; }
    public ZoneLevel Level { get; set; }
    public short Depth { get; set; }
    public string? Code { get; set; }
    public string NameBn { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string Slug { get; set; } = "";
    /// <summary>Materialized path of ids, e.g. "1.12.1203". Subtree check: path &lt;@ ancestor.path.</summary>
    public LTree Path { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Category : ITimestamped
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public string NameBn { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Icon { get; set; }
    public string? BannerUrl { get; set; }
    public int SortOrder { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
