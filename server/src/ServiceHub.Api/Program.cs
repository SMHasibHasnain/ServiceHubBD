using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Features.Auth;
using ServiceHub.Api.Features.Billing;
using ServiceHub.Api.Features.Bookings;
using ServiceHub.Api.Features.Inspections;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Data ----------
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(config.GetConnectionString("Default"), npgsql => npgsql
        .MigrationsHistoryTable("__ef_migrations_history")
        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
    .UseSnakeCaseNamingConvention()
    // Soft-deleted principals (users, businesses) filter their dependents explicitly; the warning is expected.
    .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));
builder.Services.AddScoped<DataSeeder>();
builder.Services.Configure<SeedOptions>(config.GetSection(SeedOptions.Section));

// ---------- App services ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<InspectionRouter>();
builder.Services.AddScoped<ServiceHub.Api.Features.Businesses.PublicProfileBuilder>();
builder.Services.AddScoped<ServiceHub.Api.Features.Page.UploadCleanup>();
builder.Services.Configure<BillingOptions>(config.GetSection(BillingOptions.Section));
builder.Services.AddScoped<EntitlementService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddHostedService<SubscriptionWorker>();
builder.Services.AddSingleton<ISmsSender, LoggingSmsSender>();
builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
builder.Services.AddSingleton<LocalDiskStorage>();
builder.Services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalDiskStorage>());
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// ---------- Auth ----------
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (System.Text.Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes. Set it with `dotnet user-secrets` or an environment variable.");
builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.Key(),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
        };
        o.Events = new JwtBearerEvents
        {
            // Suspended or deleted accounts lose access immediately, not when the token expires.
            OnTokenValidated = async ctx =>
            {
                var sub = ctx.Principal?.FindFirst("sub")?.Value;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var active = Guid.TryParse(sub, out var id) &&
                             await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.Status == AccountStatus.Active);
                if (!active) ctx.Fail("account_inactive");
            },
        };
    });
builder.Services.AddAuthorization();

// ---------- HTTP ----------
builder.Services.AddControllers(o => o.ModelBinderProviders.Insert(0, new SnakeCaseEnumBinderProvider()))
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));
var authPerMinute = config.GetValue("RateLimits:AuthPerMinute", 20);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // Token refresh happens on every page load / tab, so it gets its own, larger budget.
    o.AddPolicy("session", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute * 10, Window = TimeSpan.FromMinutes(1) }));
    var uploadsPerMinute = config.GetValue("RateLimits:UploadsPerMinute", 30);
    o.AddPolicy("uploads", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = uploadsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    var seedOptions = config.GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(seedOptions);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.WithTitle("ServiceHub BD API"));
}
app.UseCors();
// Uploaded images. nosniff stops a browser from treating an image as HTML or script.
var uploads = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(app.Services.GetRequiredService<LocalDiskStorage>().Root),
    RequestPath = "/" + uploads.PublicPath.Trim('/'),
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    },
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("database unavailable", statusCode: 503));

app.Run();

public partial class Program;
