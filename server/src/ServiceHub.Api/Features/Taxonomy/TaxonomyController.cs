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
}
