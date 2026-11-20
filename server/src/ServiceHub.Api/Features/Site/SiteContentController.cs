using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Site;

/// <summary>Content is null when nothing has been saved yet; the client then shows its built-in defaults.</summary>
public record SiteContentDto(string Key, JsonElement? Content, DateTime? UpdatedAt);

public record SaveSiteContentRequest(JsonElement Content);

/// <summary>
/// Super-admin editable page content (homepage copy, section order and visibility, header/footer text).
/// The document is opaque JSON to the server: it is size-limited and must be an object, and the client renders
/// every string as text (never HTML) and allows only relative or https links, so stored content can't inject markup.
/// </summary>
[ApiController]
[Route("api")]
public class SiteContentController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit) : ControllerBase
{
    private static readonly HashSet<string> Keys = ["home"];
    private const int MaxBytes = 256 * 1024;

    [HttpGet("site/{key}")]
    public async Task<ActionResult<SiteContentDto>> Get(string key)
    {
        RequireKnown(key);
        var row = await db.SiteContents.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        if (row is null) return new SiteContentDto(key, null, null);
        using var doc = JsonDocument.Parse(row.Content);
        return new SiteContentDto(key, doc.RootElement.Clone(), row.UpdatedAt);
    }

    [Authorize]
    [HttpPut("admin/site/{key}")]
    public async Task<ActionResult<SiteContentDto>> Save(string key, SaveSiteContentRequest req)
    {
        await access.RequireGlobalAsync(P.SiteContentManage);
        RequireKnown(key);
        if (req.Content.ValueKind != JsonValueKind.Object)
            throw new BadRequestException("কনটেন্ট অবশ্যই একটি JSON অবজেক্ট হতে হবে।", "invalid_content");
        var json = req.Content.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new BadRequestException("কনটেন্ট অনেক বড় হয়ে গেছে (সর্বোচ্চ ২৫৬ কেবি)।", "content_too_large");

        var row = await db.SiteContents.FirstOrDefaultAsync(x => x.Key == key);
        row ??= db.SiteContents.Add(new SiteContent { Key = key }).Entity;
        row.Content = json;
        row.UpdatedById = me.RequireId();
        audit.Log("site_content.update", "site_content", key, scope: RoleScope.Platform);
        await db.SaveChangesAsync();
        return new SiteContentDto(key, req.Content, row.UpdatedAt);
    }

    /// <summary>Drops the saved document so the page falls back to the built-in defaults.</summary>
    [Authorize]
    [HttpDelete("admin/site/{key}")]
    public async Task<IActionResult> Reset(string key)
    {
        await access.RequireGlobalAsync(P.SiteContentManage);
        RequireKnown(key);
        var row = await db.SiteContents.FirstOrDefaultAsync(x => x.Key == key);
        if (row is not null)
        {
            db.SiteContents.Remove(row);
            audit.Log("site_content.reset", "site_content", key, scope: RoleScope.Platform);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    private static void RequireKnown(string key)
    {
        if (!Keys.Contains(key)) throw new NotFoundException("এই পেজের কনটেন্ট পাওয়া যায়নি।");
    }
}
