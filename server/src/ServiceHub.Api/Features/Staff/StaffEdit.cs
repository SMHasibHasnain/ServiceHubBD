using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Staff;

/// <summary>
/// One staff edit of a business, for the length of one request. Started by
/// <see cref="Access.AccessService.RequireBusinessOrStaffAsync"/> when a non-member gets in through a platform
/// permission on a write request: it opens a transaction and snapshots the business's editable tables. When the action
/// succeeds, <see cref="StaffEditFilter"/> diffs, stores a <see cref="StaffAction"/> and commits, so the edit and its log
/// entry are saved together or not at all.
/// </summary>
public class StaffEdit(AppDbContext db, IHttpContextAccessor http)
{
    private IDbContextTransaction? tx;
    private Dictionary<string, Dictionary<string, BusinessSnapshot.Row>>? before;
    private StaffAction? pending;

    public bool Active => pending is not null;

    /// <summary>While active, replaced uploads are kept on disk so an undo can point back at them.</summary>
    public bool KeepFiles => Active;

    /// <summary>Set by the undo endpoint so the new log entry links to the one it reverts.</summary>
    public long? UndoOf { set { if (pending is not null) pending.UndoOf = value; } }

    public async Task BeginAsync(Guid businessId, Guid actorId, short? roleId, string roleKey)
    {
        var method = http.HttpContext?.Request.Method;
        if (method is null || HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return;
        if (pending is not null)
        {
            if (pending.BusinessId != businessId) throw new InvalidOperationException("One staff edit per request.");
            return;
        }
        tx = await db.Database.BeginTransactionAsync();
        before = await BusinessSnapshot.TakeAsync(db, businessId);
        pending = new StaffAction { BusinessId = businessId, ActorId = actorId, RoleId = roleId, RoleKey = roleKey };
    }

    public async Task CompleteAsync(string action)
    {
        if (pending is null || tx is null || before is null) return;
        var after = await BusinessSnapshot.TakeAsync(db, pending.BusinessId);
        var changes = BusinessSnapshot.Diff(before, after);
        if (changes.Count > 0)
        {
            pending.Action = action;
            pending.Changes = changes;
            pending.Summary = (pending.UndoOf is null ? "" : "ফিরিয়ে নেওয়া: ") + BusinessSnapshot.Summarize(changes);
            db.StaffActions.Add(pending);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        Reset();
    }

    public async Task AbortAsync()
    {
        if (tx is not null) await tx.RollbackAsync();
        Reset();
    }

    private void Reset()
    {
        tx?.Dispose();
        tx = null;
        before = null;
        pending = null;
    }
}

/// <summary>Finishes or rolls back the request's <see cref="StaffEdit"/> after the action runs.</summary>
public class StaffEditFilter(StaffEdit edit) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        if (!edit.Active) return;
        var status = executed.Result is IStatusCodeActionResult r ? r.StatusCode ?? 200 : 200;
        if (executed.Exception is not null || status >= 400)
        {
            await edit.AbortAsync();
            return;
        }
        var route = context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.ActionDescriptor.DisplayName ?? "";
        await edit.CompleteAsync($"{context.HttpContext.Request.Method} {route}");
    }
}
