using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Features.Billing;

namespace ServiceHub.Api.Features.Crm;

public enum CustomerSegment { All, New, Repeat, Vip, Inactive }

public record CustomerDto(Guid Id, Guid? UserId, string DisplayName, string Phone, string? Email, bool IsVip,
    List<string> Tags, string[] Badges, DateTime? FirstBookingAt, DateTime? LastVisitAt, DateTime? NextBookingAt,
    int TotalBookings, int CompletedBookings, decimal LifetimeValue, string? FavouriteService);

public record NoteDto(Guid Id, string Body, bool IsPinned, string AuthorName, DateTime CreatedAt);

public record CustomerVisitDto(Guid BookingId, string BookingNumber, string ServiceName, DateTime StartAt, BookingStatus Status, decimal? FinalPrice);

public record CustomerDetailDto(CustomerDto Customer, List<NoteDto> Notes, List<CustomerVisitDto> Bookings);

public record UpdateCustomerRequest(bool? IsVip, List<string>? Tags);

public record AddNoteRequest([Required, StringLength(2000, MinimumLength = 1)] string Body, bool IsPinned = false);

/// <summary>Lightweight per-workspace CRM. Notes are private to the business.</summary>
[ApiController]
[Authorize]
[Route("api/businesses/{businessId:guid}/customers")]
public class CustomersController(AppDbContext db, AccessService access, ICurrentUser me, EntitlementService plans) : ControllerBase
{
    /// <summary>No visit in this many days marks a customer inactive.</summary>
    private const int InactiveDays = 90;

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List(Guid businessId, [FromQuery] string? q,
        [FromQuery] CustomerSegment segment, [FromQuery] PageQuery paging)
    {
        await access.RequireBusinessAsync(businessId, P.CustomersView);
        var cutoff = DateTime.UtcNow.AddDays(-InactiveDays);
        var query = db.BusinessCustomers.Where(c => c.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            var phone = Phone.NormalizeBd(q);
            query = query.Where(c => EF.Functions.ILike(c.DisplayName, like) || (phone != null && c.Phone == phone));
        }
        query = segment switch
        {
            CustomerSegment.New => query.Where(c => c.CompletedBookings <= 1),
            CustomerSegment.Repeat => query.Where(c => c.CompletedBookings >= 2),
            CustomerSegment.Vip => query.Where(c => c.IsVip),
            CustomerSegment.Inactive => query.Where(c => c.LastVisitAt != null && c.LastVisitAt < cutoff && c.NextBookingAt == null),
            _ => query,
        };
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(c => c.IsVip).ThenByDescending(c => c.LastVisitAt ?? c.CreatedAt)
            .Skip(paging.Skip).Take(paging.SafeSize).ToListAsync();
        var favs = await FavouriteNamesAsync(items);
        return new PagedResult<CustomerDto>(items.Select(c => ToDto(c, favs)).ToList(), paging.SafePage, paging.SafeSize, total);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> Get(Guid businessId, Guid id)
    {
        await access.RequireBusinessAsync(businessId, P.CustomersView);
        var c = await LoadAsync(businessId, id);
        var notes = await db.CustomerNotes.Where(n => n.BusinessCustomerId == id)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt)
            .Join(db.Users, n => n.AuthorId, u => u.Id, (n, u) => new NoteDto(n.Id, n.Body, n.IsPinned, u.FullName, n.CreatedAt))
            .ToListAsync();
        var visits = await db.Bookings.Where(b => b.BusinessCustomerId == id).OrderByDescending(b => b.StartAt).Take(50)
            .Select(b => new CustomerVisitDto(b.Id, b.BookingNumber, b.ServiceName, b.StartAt, b.Status, b.FinalPrice))
            .ToListAsync();
        return new CustomerDetailDto(ToDto(c, await FavouriteNamesAsync([c])), notes, visits);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Update(Guid businessId, Guid id, UpdateCustomerRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.CustomersManage);
        await plans.RequireFeatureAsync(businessId, E.CrmNotes);
        var c = await LoadAsync(businessId, id);
        if (req.IsVip is not null) c.IsVip = req.IsVip.Value;
        if (req.Tags is not null) c.Tags = req.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().Take(20).ToList();
        await db.SaveChangesAsync();
        return ToDto(c, await FavouriteNamesAsync([c]));
    }

    [HttpPost("{id:guid}/notes")]
    public async Task<ActionResult<NoteDto>> AddNote(Guid businessId, Guid id, AddNoteRequest req)
    {
        await access.RequireBusinessAsync(businessId, P.CustomersNotes);
        await plans.RequireFeatureAsync(businessId, E.CrmNotes);
        await LoadAsync(businessId, id);
        var userId = me.RequireId();
        var note = new CustomerNote { BusinessCustomerId = id, AuthorId = userId, Body = req.Body.Trim(), IsPinned = req.IsPinned };
        db.CustomerNotes.Add(note);
        await db.SaveChangesAsync();
        var author = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstAsync();
        return new NoteDto(note.Id, note.Body, note.IsPinned, author, note.CreatedAt);
    }

    [HttpDelete("{id:guid}/notes/{noteId:guid}")]
    public async Task<IActionResult> DeleteNote(Guid businessId, Guid id, Guid noteId)
    {
        await access.RequireBusinessAsync(businessId, P.CustomersNotes);
        await LoadAsync(businessId, id);
        var userId = me.RequireId();
        var manager = await access.HasBusinessPermissionAsync(businessId, P.CustomersManage);
        var deleted = await db.CustomerNotes
            .Where(n => n.Id == noteId && n.BusinessCustomerId == id && (manager || n.AuthorId == userId))
            .ExecuteDeleteAsync();
        if (deleted == 0) throw new NotFoundException("নোটটি পাওয়া যায়নি।", "note_not_found");
        return NoContent();
    }

    private static CustomerDto ToDto(BusinessCustomer c, Dictionary<Guid, string> favs)
    {
        var badges = new List<string>();
        if (c.IsVip) badges.Add("vip");
        if (c.CompletedBookings >= 2) badges.Add("repeat");
        else badges.Add("new");
        if (c.LastVisitAt < DateTime.UtcNow.AddDays(-InactiveDays) && c.NextBookingAt == null) badges.Add("inactive");
        return new CustomerDto(c.Id, c.UserId, c.DisplayName, c.Phone, c.Email, c.IsVip, c.Tags, badges.ToArray(),
            c.FirstBookingAt, c.LastVisitAt, c.NextBookingAt, c.TotalBookings, c.CompletedBookings, c.LifetimeValue,
            c.FavouriteServiceId is { } f && favs.TryGetValue(f, out var name) ? name : null);
    }

    private async Task<Dictionary<Guid, string>> FavouriteNamesAsync(IEnumerable<BusinessCustomer> customers)
    {
        var ids = customers.Where(c => c.FavouriteServiceId != null).Select(c => c.FavouriteServiceId!.Value).Distinct().ToList();
        return await db.Services.IgnoreQueryFilters().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
    }

    private async Task<BusinessCustomer> LoadAsync(Guid businessId, Guid id) =>
        await db.BusinessCustomers.FirstOrDefaultAsync(c => c.Id == id && c.BusinessId == businessId)
        ?? throw new NotFoundException("গ্রাহক পাওয়া যায়নি।", "customer_not_found");
}
