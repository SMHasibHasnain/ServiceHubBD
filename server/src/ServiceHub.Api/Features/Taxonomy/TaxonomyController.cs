using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Taxonomy;

public record ZoneDto(int Id, int? ParentId, ZoneLevel Level, short Depth, string NameBn, string NameEn, string Slug, string Path);

public record CategoryDto(int Id, int? ParentId, string NameBn, string NameEn, string Slug, string? Icon, int SortOrder,
    RecordStatus Status, List<CategoryDto> Children);

public record UpsertCategoryRequest(
    [Range(1, int.MaxValue)] int Id,
    int? ParentId,
    [Required, StringLength(80)] string NameBn,
    [Required, StringLength(80)] string NameEn,
    [Required, RegularExpression("^[a-z0-9-]{2,100}$")] string Slug,
    string? Icon,
    int SortOrder = 0,
    RecordStatus Status = RecordStatus.Active);

public record CreateCategoryRequest(
    int? ParentId,
    [Required, StringLength(80)] string NameBn,
    [Required, StringLength(80)] string NameEn,
    [Required, RegularExpression("^[a-z0-9-]{2,100}$")] string Slug,
    string? Icon,
    int SortOrder = 0,
    RecordStatus Status = RecordStatus.Active);

/// <summary>Admin view: every category regardless of status, with how much uses it (deletion is only allowed when unused).</summary>
public record AdminCategoryDto(int Id, int? ParentId, string NameBn, string NameEn, string Slug, string? Icon, int SortOrder,
    RecordStatus Status, int BusinessCount, int ServiceCount, int ChildCount);

[ApiController]
[Route("api")]
public class TaxonomyController(AppDbContext db, AccessService access, AuditLogger audit) : ControllerBase
{
    /// <summary>Children of a zone (or all divisions when parentId is omitted).</summary>
    [HttpGet("zones")]
    public async Task<ActionResult<List<ZoneDto>>> Zones([FromQuery] int? parentId, [FromQuery] string? q)
    {
        var query = db.GeoZones.Where(z => z.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(z => z.NameBn.Contains(q) || EF.Functions.ILike(z.NameEn, $"%{q}%"));
        else
            query = query.Where(z => z.ParentId == parentId);
        return await query.OrderBy(z => z.NameEn).Take(200)
            .Select(z => new ZoneDto(z.Id, z.ParentId, z.Level, z.Depth, z.NameBn, z.NameEn, z.Slug, z.Path.ToString()))
            .ToListAsync();
    }

    [HttpGet("zones/{id:int}")]
    public async Task<ActionResult<ZoneDto>> Zone(int id) =>
        await db.GeoZones.Where(z => z.Id == id)
            .Select(z => new ZoneDto(z.Id, z.ParentId, z.Level, z.Depth, z.NameBn, z.NameEn, z.Slug, z.Path.ToString()))
            .FirstOrDefaultAsync() ?? throw new NotFoundException("এলাকাটি পাওয়া যায়নি।");

    /// <summary>Active categories as a tree (top level → children).</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<List<CategoryDto>>> Categories()
    {
        var all = await db.Categories.Where(c => c.Status == RecordStatus.Active).OrderBy(c => c.SortOrder).ToListAsync();
        List<CategoryDto> Build(int? parent) => all.Where(c => c.ParentId == parent)
            .Select(c => new CategoryDto(c.Id, c.ParentId, c.NameBn, c.NameEn, c.Slug, c.Icon, c.SortOrder, c.Status, Build(c.Id)))
            .ToList();
        return Build(null);
    }

    [Authorize]
    [HttpPut("admin/categories")]
    public async Task<ActionResult<CategoryDto>> UpsertCategory(UpsertCategoryRequest req)
    {
        await access.RequireGlobalAsync(P.CategoriesManage);
        if (req.ParentId is not null && !await db.Categories.AnyAsync(c => c.Id == req.ParentId))
            throw new BadRequestException("মূল ক্যাটাগরি পাওয়া যায়নি।", "invalid_parent");
        if (await db.Categories.AnyAsync(c => c.Slug == req.Slug && c.Id != req.Id))
            throw new ConflictException("এই slug আগেই ব্যবহৃত হয়েছে।", "slug_taken");

        await ValidateParentAsync(req.ParentId, req.Id);
        var c = await db.Categories.FindAsync(req.Id);
        var isNew = c is null;
        c ??= db.Categories.Add(new Category { Id = req.Id }).Entity;
        c.ParentId = req.ParentId;
        c.NameBn = req.NameBn;
        c.NameEn = req.NameEn;
        c.Slug = req.Slug;
        c.Icon = req.Icon;
        c.SortOrder = req.SortOrder;
        c.Status = req.Status;
        audit.Log(isNew ? "category.create" : "category.update", "category", c.Id, scope: RoleScope.Platform);
        await db.SaveChangesAsync();
        return new CategoryDto(c.Id, c.ParentId, c.NameBn, c.NameEn, c.Slug, c.Icon, c.SortOrder, c.Status, []);
    }

    [Authorize]
    [HttpGet("admin/categories")]
    public async Task<ActionResult<List<AdminCategoryDto>>> AdminCategories()
    {
        await access.RequireGlobalAsync(P.CategoriesManage);
        return await db.Categories.OrderBy(c => c.ParentId ?? c.Id).ThenBy(c => c.ParentId != null).ThenBy(c => c.SortOrder)
            .Select(c => new AdminCategoryDto(c.Id, c.ParentId, c.NameBn, c.NameEn, c.Slug, c.Icon, c.SortOrder, c.Status,
                db.Businesses.Count(b => b.CategoryId == c.Id),
                db.Services.Count(x => x.CategoryId == c.Id),
                db.Categories.Count(x => x.ParentId == c.Id)))
            .ToListAsync();
    }

    [Authorize]
    [HttpPost("admin/categories")]
    public async Task<ActionResult<CategoryDto>> CreateCategory(CreateCategoryRequest req)
    {
        await access.RequireGlobalAsync(P.CategoriesManage);
        await ValidateParentAsync(req.ParentId, null);
        if (await db.Categories.AnyAsync(c => c.Slug == req.Slug))
            throw new ConflictException("এই slug আগেই ব্যবহৃত হয়েছে।", "slug_taken");
        // Ids are assigned by hand (seeded categories keep stable ids), so take the next free one.
        var id = (await db.Categories.MaxAsync(c => (int?)c.Id) ?? 0) + 1;
        var c = db.Categories.Add(new Category
        {
            Id = id, ParentId = req.ParentId, NameBn = req.NameBn, NameEn = req.NameEn, Slug = req.Slug,
            Icon = req.Icon, SortOrder = req.SortOrder, Status = req.Status,
        }).Entity;
        audit.Log("category.create", "category", c.Id, scope: RoleScope.Platform);
        await db.SaveChangesAsync();
        return new CategoryDto(c.Id, c.ParentId, c.NameBn, c.NameEn, c.Slug, c.Icon, c.SortOrder, c.Status, []);
    }

    /// <summary>Deletes an unused category. One that businesses, services or sub-categories use must be hidden instead.</summary>
    [Authorize]
    [HttpDelete("admin/categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        await access.RequireGlobalAsync(P.CategoriesManage);
        var c = await db.Categories.FindAsync(id) ?? throw new NotFoundException("ক্যাটাগরিটি পাওয়া যায়নি।");
        if (await db.Categories.AnyAsync(x => x.ParentId == id))
            throw new ConflictException("এই ক্যাটাগরির ভেতরে সাব-ক্যাটাগরি আছে। আগে সেগুলো সরান, অথবা ক্যাটাগরিটি লুকিয়ে রাখুন।", "category_has_children");
        if (await db.Businesses.AnyAsync(b => b.CategoryId == id) || await db.Services.AnyAsync(s => s.CategoryId == id))
            throw new ConflictException("কিছু ব্যবসা বা সেবা এই ক্যাটাগরিতে আছে, তাই মোছা যাবে না। ক্যাটাগরিটি লুকিয়ে রাখুন।", "category_in_use");
        if (await db.PlatformRoleAssignments.AnyAsync(a => a.CategoryId == id))
            throw new ConflictException("কোনো ভূমিকা এই ক্যাটাগরির সাথে যুক্ত, তাই মোছা যাবে না। ক্যাটাগরিটি লুকিয়ে রাখুন।", "category_in_use");
        db.Categories.Remove(c);
        audit.Log("category.delete", "category", id, scope: RoleScope.Platform);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Two levels only: a parent must exist, be top-level, and not be the category itself.</summary>
    private async Task ValidateParentAsync(int? parentId, int? selfId)
    {
        if (parentId is null) return;
        if (parentId == selfId) throw new BadRequestException("ক্যাটাগরি নিজের মূল হতে পারে না।", "invalid_parent");
        var parent = await db.Categories.FindAsync(parentId) ?? throw new BadRequestException("মূল ক্যাটাগরি পাওয়া যায়নি।", "invalid_parent");
        if (parent.ParentId is not null) throw new BadRequestException("শুধু প্রধান ক্যাটাগরির ভেতরে সাব-ক্যাটাগরি রাখা যায়।", "invalid_parent");
        if (selfId is not null && await db.Categories.AnyAsync(x => x.ParentId == selfId))
            throw new BadRequestException("যে ক্যাটাগরির নিজের সাব-ক্যাটাগরি আছে, সেটি অন্যের ভেতরে রাখা যায় না।", "invalid_parent");
    }
}
