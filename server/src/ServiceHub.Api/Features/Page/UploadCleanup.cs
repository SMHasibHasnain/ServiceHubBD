using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;

namespace ServiceHub.Api.Features.Page;

/// <summary>
/// Deletes uploaded images that nothing uses any more. Call it after SaveChanges with the URLs that were just
/// replaced or removed; a file is kept while the logo, the cover, any service (deleted ones included, for history)
/// or the gallery still points at it. Links to other sites and other businesses' files are never touched.
/// During a staff edit nothing is deleted: the edit can be undone, and the undo points back at the old files.
/// </summary>
public class UploadCleanup(AppDbContext db, IFileStorage storage, Staff.StaffEdit staffEdit)
{
    public async Task DeleteIfUnusedAsync(Guid businessId, params string?[] urls)
    {
        if (staffEdit.KeepFiles) return;
        var candidates = urls.OfType<string>().Distinct().Where(u => storage.Owns(u, PageController.Folder(businessId))).ToList();
        if (candidates.Count == 0) return;

        var inUse = await db.Services.IgnoreQueryFilters().Where(s => s.BusinessId == businessId && s.ImageUrl != null)
            .Select(s => s.ImageUrl!).ToListAsync();
        inUse.AddRange(await db.GalleryItems.Where(g => g.BusinessId == businessId).Select(g => g.ImageUrl).ToListAsync());
        var images = await db.Businesses.IgnoreQueryFilters().Where(x => x.Id == businessId)
            .Select(x => new { x.LogoUrl, x.CoverUrl }).FirstAsync();
        inUse.AddRange(new[] { images.LogoUrl, images.CoverUrl }.OfType<string>());

        foreach (var url in candidates.Where(u => !inUse.Contains(u)))
            await storage.DeleteAsync(url);
    }
}
