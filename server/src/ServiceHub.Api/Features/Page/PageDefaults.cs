using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Page;

public record PageDefaultsDto(string ThemeKey, string HeaderStyle, List<PageSection> Sections);

/// <summary>
/// Platform-wide starting look for business pages, set by admins. A business that never chose a theme, header style
/// or section layout (its column is null) follows these; once the owner saves their own, theirs wins. Stored as the
/// <c>page_defaults</c> row of <c>site_contents</c>; without a row the built-in defaults apply.
/// </summary>
public class PageDefaults(AppDbContext db)
{
    public const string Key = "page_defaults";
    public static readonly PageDefaultsDto BuiltIn = new("blue", "cover", PageCatalog.Normalize(null));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private PageDefaultsDto? cached;

    public async Task<PageDefaultsDto> GetAsync()
    {
        if (cached is not null) return cached;
        var content = await db.SiteContents.AsNoTracking().Where(x => x.Key == Key).Select(x => x.Content).FirstOrDefaultAsync();
        PageDefaultsDto? saved = null;
        try { if (content is not null) saved = JsonSerializer.Deserialize<PageDefaultsDto>(content, Json); }
        catch (JsonException) { /* a broken row falls back to the built-in defaults */ }
        cached = saved is null ? BuiltIn : new PageDefaultsDto(
            PageCatalog.Themes.Contains(saved.ThemeKey) ? saved.ThemeKey : BuiltIn.ThemeKey,
            PageCatalog.HeaderStyles.Contains(saved.HeaderStyle) ? saved.HeaderStyle : BuiltIn.HeaderStyle,
            PageCatalog.Normalize(saved.Sections));
        return cached;
    }

    public async Task SaveAsync(PageDefaultsDto value, Guid userId)
    {
        var row = await db.SiteContents.FirstOrDefaultAsync(x => x.Key == Key);
        if (row is null) db.SiteContents.Add(row = new SiteContent { Key = Key });
        row.Content = JsonSerializer.Serialize(value, Json);
        row.UpdatedById = userId;
        cached = null;
    }

    /// <summary>The look a business actually gets: its own choice, else the platform default.</summary>
    public async Task<(string Theme, string Header, List<PageSection> Layout)> EffectiveAsync(Business b) =>
        await EffectiveAsync(b.ThemeKey, b.HeaderStyle, b.PageLayout);

    public async Task<(string Theme, string Header, List<PageSection> Layout)> EffectiveAsync(string? theme, string? header, List<PageSection>? layout)
    {
        var d = await GetAsync();
        return (theme ?? d.ThemeKey, header ?? d.HeaderStyle, layout is null ? d.Sections : PageCatalog.Normalize(layout));
    }
}
