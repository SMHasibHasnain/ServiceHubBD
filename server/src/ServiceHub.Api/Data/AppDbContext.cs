using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ServiceHub.Api.Common;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string BookingNumberSequence = "booking_number_seq";

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<PlatformRoleAssignment> PlatformRoleAssignments => Set<PlatformRoleAssignment>();
    public DbSet<GeoZone> GeoZones => Set<GeoZone>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessMember> BusinessMembers => Set<BusinessMember>();
    public DbSet<BusinessInvitation> BusinessInvitations => Set<BusinessInvitation>();
    public DbSet<BusinessBookingSettings> BusinessBookingSettings => Set<BusinessBookingSettings>();
    public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
    public DbSet<BusinessClosure> BusinessClosures => Set<BusinessClosure>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceBookingField> ServiceBookingFields => Set<ServiceBookingField>();
    public DbSet<VerificationRequest> VerificationRequests => Set<VerificationRequest>();
    public DbSet<VerificationDocument> VerificationDocuments => Set<VerificationDocument>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<BusinessCustomer> BusinessCustomers => Set<BusinessCustomer>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingFieldResponse> BookingFieldResponses => Set<BookingFieldResponse>();
    public DbSet<BookingStatusHistory> BookingStatusHistory => Set<BookingStatusHistory>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReply> ReviewReplies => Set<ReviewReply>();
    public DbSet<Favourite> Favourites => Set<Favourite>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Entity is ITimestamped t)
            {
                if (entry.State == EntityState.Added && t.CreatedAt == default) t.CreatedAt = now;
                t.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Added)
            {
                // Plain created_at columns on append-only tables.
                var created = entry.Metadata.FindProperty("CreatedAt");
                if (created is not null && entry.Property("CreatedAt").CurrentValue is DateTime d && d == default)
                    entry.Property("CreatedAt").CurrentValue = now;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("ltree");
        b.HasPostgresExtension("btree_gist");
        b.HasSequence<long>(BookingNumberSequence).StartsAt(1000);

        // ---------- Identity ----------
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Phone).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(255);
            e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.LastActiveBusinessId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });
        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
        });
        b.Entity<VerificationCode>(e => e.HasIndex(x => new { x.Target, x.Purpose }));

        // ---------- Access control ----------
        b.Entity<Role>(e =>
        {
            e.HasIndex(x => new { x.BusinessId, x.Key }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => x.Scope);
            e.Property(x => x.Key).HasMaxLength(50);
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Permission>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(80);
        });
        b.Entity<RolePermission>(e => e.HasKey(x => new { x.RoleId, x.PermissionId }));
        b.Entity<PlatformRoleAssignment>(e =>
        {
            e.HasOne(x => x.User).WithMany(u => u.RoleAssignments).HasForeignKey(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RevokedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.RoleId, x.ZoneId, x.CategoryId }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => new { x.RoleId, x.ZoneId });
        });

        // ---------- Taxonomy ----------
        b.Entity<GeoZone>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => x.Path).HasMethod("gist");
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
        });
        b.Entity<Category>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.ParentId, x.SortOrder });
        });

        // ---------- Business workspace ----------
        b.Entity<Business>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.OwnerId);
            e.HasIndex(x => new { x.CategoryId, x.Status });
            e.HasIndex(x => new { x.ZoneId, x.Status });
            e.HasIndex(x => x.VerificationStatus);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Slug).HasMaxLength(170);
            e.Property(x => x.AvgRating).HasPrecision(2, 1);
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OnboardedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.BookingSettings).WithOne().HasForeignKey<BusinessBookingSettings>(s => s.BusinessId);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });
        b.Entity<BusinessMember>(e =>
        {
            e.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.BusinessId, x.RoleId });
            e.HasOne(x => x.Business).WithMany(b2 => b2.Members).HasForeignKey(x => x.BusinessId);
            e.HasOne(x => x.User).WithMany(u => u.Memberships).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.Business.DeletedAt == null);
        });
        b.Entity<BusinessInvitation>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.Status });
            e.HasIndex(x => x.InviteePhone);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.Business.DeletedAt == null);
        });
        b.Entity<BusinessBookingSettings>(e => e.HasKey(x => x.BusinessId));
        b.Entity<BusinessHour>(e =>
        {
            e.HasIndex(x => new { x.BusinessId, x.DayOfWeek, x.OpensAt }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_business_hours_range", "day_of_week BETWEEN 0 AND 6 AND closes_at > opens_at"));
            e.HasOne<Business>().WithMany(x => x.Hours).HasForeignKey(x => x.BusinessId);
        });
        b.Entity<BusinessClosure>(e =>
        {
            e.HasIndex(x => new { x.BusinessId, x.ClosedOn });
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId);
        });

        // ---------- Catalog ----------
        b.Entity<Service>(e =>
        {
            e.HasIndex(x => new { x.BusinessId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.Status });
            e.HasIndex(x => x.CategoryId);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Price).HasPrecision(12, 2);
            e.Property(x => x.PriceMax).HasPrecision(12, 2);
            e.Property(x => x.AvgRating).HasPrecision(2, 1);
            e.HasOne(x => x.Business).WithMany(x => x.Services).HasForeignKey(x => x.BusinessId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_services_price",
                "price_type = 'contact_for_price' OR price IS NOT NULL"));
            e.HasQueryFilter(x => x.DeletedAt == null);
        });
        b.Entity<ServiceBookingField>(e =>
            e.HasOne<Service>().WithMany(s => s.BookingFields).HasForeignKey(x => x.ServiceId));

        // ---------- Verification & inspection ----------
        b.Entity<VerificationRequest>(e =>
        {
            e.HasIndex(x => new { x.Status, x.Priority, x.SubmittedAt });
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => x.AssignedModeratorId);
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedModeratorId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => x.Business.DeletedAt == null);
        });
        b.Entity<VerificationDocument>(e =>
            e.HasOne<VerificationRequest>().WithMany(v => v.Documents).HasForeignKey(x => x.VerificationRequestId));
        b.Entity<Inspection>(e =>
        {
            e.HasIndex(x => new { x.InspectorId, x.Status, x.ScheduledFor });
            e.HasIndex(x => new { x.ZoneId, x.Status });
            e.HasIndex(x => x.BusinessId);
            e.Property(x => x.VisitLatitude).HasPrecision(9, 6);
            e.Property(x => x.VisitLongitude).HasPrecision(9, 6);
            e.Property(x => x.Checklist).HasJson();
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId);
            e.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VerificationRequest>().WithMany().HasForeignKey(x => x.VerificationRequestId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Report>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.InspectorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.Business.DeletedAt == null);
        });
        b.Entity<ModerationAction>(e =>
        {
            e.HasIndex(x => new { x.ModeratorId, x.CreatedAt });
            e.HasIndex(x => x.BusinessId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ModeratorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VerificationRequest>().WithMany().HasForeignKey(x => x.VerificationRequestId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Review>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Report>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<VerificationDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- CRM ----------
        b.Entity<BusinessCustomer>(e =>
        {
            e.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.Phone }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.IsVip, x.LastVisitAt });
            e.Property(x => x.LifetimeValue).HasPrecision(12, 2);
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Service>().WithMany().HasForeignKey(x => x.FavouriteServiceId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<CustomerNote>(e =>
        {
            e.HasOne<BusinessCustomer>().WithMany(c => c.Notes).HasForeignKey(x => x.BusinessCustomerId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---------- Bookings ----------
        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => x.BookingNumber).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.StartAt });
            e.HasIndex(x => new { x.BusinessId, x.Status, x.StartAt });
            e.HasIndex(x => new { x.CustomerUserId, x.StartAt });
            e.HasIndex(x => x.BusinessCustomerId);
            e.HasIndex(x => x.ServiceId);
            e.HasIndex(x => new { x.AssignedMemberId, x.StartAt });
            e.HasIndex(x => new { x.CustomerUserId, x.ClientRequestId }).IsUnique();
            e.Property(x => x.BookingNumber).HasMaxLength(20);
            e.Property(x => x.QuotedPrice).HasPrecision(12, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(12, 2);
            e.Property(x => x.FinalPrice).HasPrecision(12, 2);
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BusinessCustomer).WithMany().HasForeignKey(x => x.BusinessCustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CustomerUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AssignedMember).WithMany().HasForeignKey(x => x.AssignedMemberId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("ck_bookings_time", "end_at > start_at"));
            e.HasQueryFilter(x => x.Business.DeletedAt == null);
        });
        b.Entity<BookingFieldResponse>(e =>
        {
            e.HasIndex(x => new { x.BookingId, x.FieldId }).IsUnique();
            e.HasOne<Booking>().WithMany(x => x.FieldResponses).HasForeignKey(x => x.BookingId);
            e.HasOne<ServiceBookingField>().WithMany().HasForeignKey(x => x.FieldId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<BookingStatusHistory>(e =>
        {
            e.HasIndex(x => new { x.BookingId, x.CreatedAt });
            e.HasOne<Booking>().WithMany(x => x.History).HasForeignKey(x => x.BookingId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- Reviews & trust ----------
        b.Entity<Review>(e =>
        {
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.Status, x.CreatedAt });
            e.HasIndex(x => x.CustomerId);
            e.HasOne(x => x.Booking).WithOne().HasForeignKey<Review>(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Reply).WithOne().HasForeignKey<ReviewReply>(x => x.ReviewId);
            e.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5"));
            e.HasQueryFilter(x => x.Booking.Business.DeletedAt == null);
        });
        b.Entity<ReviewReply>(e =>
        {
            e.HasIndex(x => x.ReviewId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Favourite>(e =>
        {
            e.HasKey(x => new { x.UserId, x.BusinessId });
            e.HasIndex(x => x.BusinessId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId);
            e.HasQueryFilter(x => x.Business.DeletedAt == null);
        });
        b.Entity<Report>(e =>
        {
            e.HasIndex(x => new { x.Status, x.Severity, x.CreatedAt });
            e.HasIndex(x => x.TargetBusinessId);
            e.HasIndex(x => x.TargetReviewId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.ReporterBusinessId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TargetBusiness).WithMany().HasForeignKey(x => x.TargetBusinessId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TargetReview).WithMany().HasForeignKey(x => x.TargetReviewId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedModeratorId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t => t.HasCheckConstraint("ck_reports_target",
                "(target_type = 'business' AND target_business_id IS NOT NULL AND target_review_id IS NULL AND target_user_id IS NULL) OR " +
                "(target_type = 'review' AND target_review_id IS NOT NULL AND target_business_id IS NULL AND target_user_id IS NULL) OR " +
                "(target_type = 'user' AND target_user_id IS NOT NULL AND target_business_id IS NULL AND target_review_id IS NULL)"));
        });

        // ---------- Platform ----------
        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.RecipientId, x.Workspace, x.BusinessId, x.ReadAt });
            e.HasIndex(x => new { x.RecipientId, x.CreatedAt });
            e.Property(x => x.Data).HasJson();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientId);
            e.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => new { x.ActorId, x.CreatedAt });
            e.HasIndex(x => new { x.BusinessId, x.CreatedAt });
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Changes).HasJson();
        });

        ApplyEnumsAsSnakeCaseStrings(b);
        MarkGuidKeysClientGenerated(b);
    }

    /// <summary>
    /// Guid ids are created in code (Guid.CreateVersion7). Telling EF they are never generated makes
    /// new children attached to an existing parent (e.g. a reply on a review) INSERTs, not UPDATEs.
    /// </summary>
    private static void MarkGuidKeysClientGenerated(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
                key.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }

    /// <summary>Stores every enum as its snake_case name (e.g. "info_requested"), matching the ERD.</summary>
    private static void ApplyEnumsAsSnakeCaseStrings(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var prop in entity.GetProperties())
        {
            var type = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
            if (!type.IsEnum) continue;
            var converterType = typeof(SnakeCaseEnumConverter<>).MakeGenericType(type);
            prop.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType)!);
            prop.SetMaxLength(40);
        }
    }
}

public class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => SnakeCase.FromEnum(v),
    v => SnakeCase.ToEnum<TEnum>(v)) where TEnum : struct, Enum;

internal static class JsonColumnExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Maps a POCO/collection property to a jsonb column via System.Text.Json.</summary>
    public static PropertyBuilder<T> HasJson<T>(this PropertyBuilder<T> p) where T : class?
    {
        p.HasConversion(
            v => v == null ? null : JsonSerializer.Serialize(v, Options),
            v => v == null ? default! : JsonSerializer.Deserialize<T>(v, Options)!,
            new ValueComparer<T>(
                (a, c) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(c, Options),
                v => v == null ? 0 : JsonSerializer.Serialize(v, Options).GetHashCode(),
                v => v == null ? default! : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!));
        p.HasColumnType("jsonb");
        return p;
    }
}
