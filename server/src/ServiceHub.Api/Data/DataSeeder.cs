using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Data;

public class SeedOptions
{
    public const string Section = "Seed";
    /// <summary>Creates the first super admin if no account with this phone exists. Leave empty in production after bootstrap.</summary>
    public string? SuperAdminPhone { get; set; }
    public string? SuperAdminPassword { get; set; }
    public string SuperAdminName { get; set; } = "সুপার এ্যাডমিন";
    public bool SampleZones { get; set; } = true;
}

/// <summary>
/// Idempotent startup seeding. Roles and permissions are synchronized with Access/Catalog.cs on
/// every start, so adding a permission in code is enough — no migration needed.
/// </summary>
public class DataSeeder(AppDbContext db, IPasswordHasher<User> hasher, ILogger<DataSeeder> logger)
{
    public async Task SeedAsync(SeedOptions options, CancellationToken ct = default)
    {
        await SyncAccessCatalogAsync(ct);
        await SeedCategoriesAsync(ct);
        if (options.SampleZones) await SeedZonesAsync(ct);
        await SeedSuperAdminAsync(options, ct);
    }

    private async Task SyncAccessCatalogAsync(CancellationToken ct)
    {
        var permissions = await db.Permissions.ToDictionaryAsync(p => p.Key, ct);
        foreach (var (key, scope, module, description) in P.All)
        {
            if (!permissions.TryGetValue(key, out var p))
            {
                p = new Permission { Key = key };
                db.Permissions.Add(p);
                permissions[key] = p;
            }
            p.Scope = scope;
            p.Module = module;
            p.Description = description;
        }

        var roles = await db.Roles.Include(r => r.RolePermissions).Where(r => r.BusinessId == null).ToDictionaryAsync(r => r.Key, ct);
        foreach (var def in R.All)
        {
            if (!roles.TryGetValue(def.Key, out var role))
            {
                role = new Role { Key = def.Key };
                db.Roles.Add(role);
                roles[def.Key] = role;
            }
            role.Scope = def.Scope;
            role.NameBn = def.NameBn;
            role.NameEn = def.NameEn;
            role.Rank = def.Rank;
            role.IsSystem = true;
        }
        await db.SaveChangesAsync(ct);

        foreach (var def in R.All)
        {
            var role = roles[def.Key];
            var wanted = def.Permissions.Select(k => permissions[k].Id).ToHashSet();
            role.RolePermissions.RemoveAll(rp => !wanted.Contains(rp.PermissionId));
            foreach (var pid in wanted.Where(pid => role.RolePermissions.All(rp => rp.PermissionId != pid)))
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = pid });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct)) return;
        (int Id, int? Parent, string Bn, string En, string Slug, string Icon)[] categories =
        [
            (1, null, "ইলেকট্রিক্যাল", "Electrical", "electrical", "zap"),
            (2, null, "ক্লিনিং", "Cleaning", "cleaning", "sparkles"),
            (3, null, "সেলুন ও বিউটি", "Salon & Beauty", "salon", "scissors"),
            (4, null, "মেরামত", "Repair", "repair", "wrench"),
            (5, 4, "এসি মেরামত", "AC Repair", "ac-repair", "air-vent"),
            (6, 4, "হোম অ্যাপ্লায়েন্স", "Home Appliance", "home-appliance", "refrigerator"),
            (7, null, "ফটোগ্রাফি", "Photography", "photography", "camera"),
            (8, null, "আইটি সাপোর্ট", "IT Support", "it-support", "laptop"),
            (9, null, "স্বাস্থ্য", "Health", "health", "heart-pulse"),
            (10, null, "শিক্ষা", "Education", "education", "graduation-cap"),
            (11, null, "পরামর্শ", "Consultation", "consultation", "messages-square"),
        ];
        var order = 0;
        db.Categories.AddRange(categories.Select(c => new Category
        {
            Id = c.Id, ParentId = c.Parent, NameBn = c.Bn, NameEn = c.En, Slug = c.Slug, Icon = c.Icon, SortOrder = order++,
        }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A small sample of the Bangladesh hierarchy for development. Import the full BBS geocode
    /// dataset (division → district → upazila/thana → union/ward) for production.
    /// </summary>
    private async Task SeedZonesAsync(CancellationToken ct)
    {
        if (await db.GeoZones.AnyAsync(ct)) return;
        (int Id, int? Parent, ZoneLevel Level, string Bn, string En)[] zones =
        [
            (1, null, ZoneLevel.Division, "ঢাকা বিভাগ", "Dhaka Division"),
            (2, null, ZoneLevel.Division, "চট্টগ্রাম বিভাগ", "Chattogram Division"),
            (3, null, ZoneLevel.Division, "রাজশাহী বিভাগ", "Rajshahi Division"),
            (4, null, ZoneLevel.Division, "খুলনা বিভাগ", "Khulna Division"),
            (5, null, ZoneLevel.Division, "বরিশাল বিভাগ", "Barishal Division"),
            (6, null, ZoneLevel.Division, "সিলেট বিভাগ", "Sylhet Division"),
            (7, null, ZoneLevel.Division, "রংপুর বিভাগ", "Rangpur Division"),
            (8, null, ZoneLevel.Division, "ময়মনসিংহ বিভাগ", "Mymensingh Division"),

            (101, 1, ZoneLevel.District, "ঢাকা", "Dhaka"),
            (102, 1, ZoneLevel.District, "গাজীপুর", "Gazipur"),
            (103, 1, ZoneLevel.District, "নারায়ণগঞ্জ", "Narayanganj"),
            (201, 2, ZoneLevel.District, "চট্টগ্রাম", "Chattogram"),
            (601, 6, ZoneLevel.District, "সিলেট", "Sylhet"),

            (10101, 101, ZoneLevel.Thana, "ধানমন্ডি", "Dhanmondi"),
            (10102, 101, ZoneLevel.Thana, "মিরপুর", "Mirpur"),
            (10103, 101, ZoneLevel.Thana, "উত্তরা", "Uttara"),
            (10104, 101, ZoneLevel.Thana, "গুলশান", "Gulshan"),
            (10105, 101, ZoneLevel.Thana, "মোহাম্মদপুর", "Mohammadpur"),
            (10201, 102, ZoneLevel.Upazila, "গাজীপুর সদর", "Gazipur Sadar"),
            (10202, 102, ZoneLevel.Upazila, "কালিয়াকৈর", "Kaliakair"),
            (10203, 102, ZoneLevel.Upazila, "শ্রীপুর", "Sreepur"),
            (10204, 102, ZoneLevel.Thana, "টঙ্গী", "Tongi"),
            (10301, 103, ZoneLevel.Upazila, "নারায়ণগঞ্জ সদর", "Narayanganj Sadar"),
            (20101, 201, ZoneLevel.Thana, "কোতোয়ালী", "Kotwali"),
            (20102, 201, ZoneLevel.Thana, "পাহাড়তলী", "Pahartali"),
            (60101, 601, ZoneLevel.Upazila, "সিলেট সদর", "Sylhet Sadar"),

            (1010101, 10101, ZoneLevel.Ward, "ওয়ার্ড ১৫ (ধানমন্ডি)", "Ward 15 (Dhanmondi)"),
            (1010301, 10103, ZoneLevel.Ward, "উত্তরা সেক্টর ৭", "Uttara Sector 7"),
            (1020201, 10202, ZoneLevel.Union, "মৌচাক ইউনিয়ন", "Mouchak Union"),
            (1020202, 10202, ZoneLevel.Union, "ফুলবাড়িয়া ইউনিয়ন", "Fulbaria Union"),
        ];
        var paths = new Dictionary<int, string>();
        foreach (var z in zones)
        {
            var path = z.Parent is null ? z.Id.ToString() : $"{paths[z.Parent.Value]}.{z.Id}";
            paths[z.Id] = path;
            db.GeoZones.Add(new GeoZone
            {
                Id = z.Id, ParentId = z.Parent, Level = z.Level, Depth = (short)(path.Count(c => c == '.') + 1),
                Code = $"Z{z.Id}", NameBn = z.Bn, NameEn = z.En, Slug = Slugs.Create(z.En, withSuffix: false) + "-" + z.Id,
                Path = new LTree(path),
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedSuperAdminAsync(SeedOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.SuperAdminPhone) || string.IsNullOrWhiteSpace(options.SuperAdminPassword)) return;
        var phone = Phone.NormalizeBd(options.SuperAdminPhone)
                    ?? throw new InvalidOperationException("Seed:SuperAdminPhone is not a valid Bangladeshi mobile number.");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == phone, ct)) return;

        var user = new User { FullName = options.SuperAdminName, Phone = phone, PhoneVerifiedAt = DateTime.UtcNow, MfaEnabled = false };
        user.PasswordHash = hasher.HashPassword(user, options.SuperAdminPassword);
        db.Users.Add(user);
        var roleId = await db.Roles.Where(r => r.Key == R.SuperAdmin && r.BusinessId == null).Select(r => r.Id).FirstAsync(ct);
        db.PlatformRoleAssignments.Add(new PlatformRoleAssignment
        {
            UserId = user.Id, RoleId = roleId, GrantedBy = user.Id, ValidFrom = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Created bootstrap super admin {Phone}. Change its password and clear Seed:SuperAdmin* settings.", phone);
    }
}
