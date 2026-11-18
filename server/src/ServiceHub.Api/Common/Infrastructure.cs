using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Common;

/// <summary>Append-only audit trail of privileged actions (platform staff and business staff alike).</summary>
public class AuditLogger(AppDbContext db, ICurrentUser me)
{
    /// <summary>Adds an audit row to the current unit of work; it is saved with the caller's SaveChanges.</summary>
    public void Log(string action, string entityType, object entityId, string? reason = null,
        Guid? businessId = null, int? zoneId = null, RoleScope? scope = null, short? roleId = null,
        Dictionary<string, string?>? changes = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = me.Id,
            RoleId = roleId,
            Scope = scope,
            BusinessId = businessId,
            ZoneId = zoneId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString()!,
            Reason = reason,
            Changes = changes,
            IpAddress = me.IpAddress,
        });
    }
}

/// <summary>Creates in-app notifications scoped to a workspace (drives per-workspace unread badges).</summary>
public class Notifier(AppDbContext db)
{
    public void Notify(Guid recipientId, WorkspaceType workspace, Guid? businessId, string type, string title,
        string? body = null, string? actionUrl = null, Dictionary<string, string>? data = null)
    {
        db.Notifications.Add(new Notification
        {
            RecipientId = recipientId,
            Workspace = workspace,
            BusinessId = businessId,
            Type = type,
            Title = title,
            Body = body,
            ActionUrl = actionUrl,
            Data = data,
        });
    }

    /// <summary>Notifies every active member of a business whose role grants <paramref name="permission"/>.</summary>
    public async Task NotifyBusinessAsync(Guid businessId, string permission, string type, string title,
        string? body = null, string? actionUrl = null, Dictionary<string, string>? data = null)
    {
        var recipients = db.BusinessMembers
            .Where(m => m.BusinessId == businessId && m.Status == MemberStatus.Active &&
                        m.Role.RolePermissions.Any(rp => rp.Permission.Key == permission))
            .Select(m => m.UserId);
        foreach (var userId in await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(recipients))
            Notify(userId, WorkspaceType.Business, businessId, type, title, body, actionUrl, data);
    }
}

public interface ISmsSender
{
    Task SendAsync(string phone, string message, CancellationToken ct = default);
}

/// <summary>Development SMS gateway: writes messages to the log instead of sending them.</summary>
public class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phone, string message, CancellationToken ct = default)
    {
        logger.LogInformation("SMS to {Phone}: {Message}", phone, message);
        return Task.CompletedTask;
    }
}

/// <summary>Maps AppException to problem details; hides unexpected errors behind a friendly message.</summary>
public class ErrorHandler(ILogger<ErrorHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails details;
        if (exception is AppException app)
        {
            details = new ProblemDetails { Status = app.Status, Title = app.Code, Detail = app.Message };
            details.Extensions["code"] = app.Code;
        }
        else
        {
            logger.LogError(exception, "Unhandled error on {Path}", http.Request.Path);
            details = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "server_error",
                Detail = "দুঃখিত, একটি সমস্যা হয়েছে। আবার চেষ্টা করুন।",
            };
            details.Extensions["code"] = "server_error";
        }
        http.Response.StatusCode = details.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = details, Exception = exception });
    }
}
