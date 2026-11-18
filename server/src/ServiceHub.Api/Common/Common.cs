using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ServiceHub.Api.Common;

public static class SnakeCase
{
    private static readonly ConcurrentDictionary<Enum, string> ToCache = new();
    private static readonly ConcurrentDictionary<(Type, string), object> FromCache = new();

    public static string Convert(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    public static string FromEnum<TEnum>(TEnum value) where TEnum : struct, Enum =>
        ToCache.GetOrAdd(value, v => Convert(v.ToString()));

    public static TEnum ToEnum<TEnum>(string value) where TEnum : struct, Enum =>
        (TEnum)FromCache.GetOrAdd((typeof(TEnum), value), key =>
        {
            foreach (var v in Enum.GetValues<TEnum>())
                if (Convert(v.ToString()) == key.Item2) return v;
            throw new InvalidOperationException($"Unknown {typeof(TEnum).Name} value '{key.Item2}'.");
        });
}

// ---------- Errors (rendered as RFC 9457 problem details by ErrorHandler) ----------

public class AppException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public class NotFoundException(string message = "তথ্যটি পাওয়া যায়নি।", string code = "not_found")
    : AppException(StatusCodes.Status404NotFound, code, message);

public class ForbiddenException(string message = "এই অংশে প্রবেশের অনুমতি আপনার নেই।", string code = "forbidden")
    : AppException(StatusCodes.Status403Forbidden, code, message);

public class ConflictException(string message, string code = "conflict")
    : AppException(StatusCodes.Status409Conflict, code, message);

public class BadRequestException(string message, string code = "bad_request")
    : AppException(StatusCodes.Status400BadRequest, code, message);

public class UnauthorizedException(string message = "অনুগ্রহ করে লগইন করুন।", string code = "unauthorized")
    : AppException(StatusCodes.Status401Unauthorized, code, message);

// ---------- Current user ----------

public interface ICurrentUser
{
    Guid? Id { get; }
    Guid RequireId();
    string? IpAddress { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? Id
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? accessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public Guid RequireId() => Id ?? throw new UnauthorizedException();

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

// ---------- Paging ----------

public record PageQuery(int Page = 1, int PageSize = 20)
{
    public int SafePage => Math.Max(1, Page);
    public int SafeSize => Math.Clamp(PageSize, 1, 100);
    public int Skip => (SafePage - 1) * SafeSize;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

// ---------- Bangladesh time ----------

public static class BdTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    /// <summary>Bangladesh week: 0 = Saturday … 6 = Friday.</summary>
    public static short BdDayOfWeek(DateOnly date) => (short)(((int)date.DayOfWeek + 1) % 7);

    public static DateTime ToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), Zone);

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

    public static DateOnly Today() => DateOnly.FromDateTime(ToLocal(DateTime.UtcNow));
}

// ---------- Tokens & slugs ----------

public static class Tokens
{
    public static string NewToken(int bytes = 32) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Sha256(string value) =>
        System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string NumericCode(int digits = 6) =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');

    private static string Base64UrlEncode(byte[] data) =>
        System.Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class Slugs
{
    /// <summary>
    /// Builds a URL slug from the Latin characters of <paramref name="text"/>. Bangla-only names
    /// produce just the random suffix, so slugs are always unique-ish and URL-safe.
    /// </summary>
    public static string Create(string? text, bool withSuffix = true)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? "").ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var core = sb.ToString().Trim('-');
        if (core.Length > 60) core = core[..60].Trim('-');
        if (!withSuffix) return core;
        var suffix = Tokens.NewToken(4).ToLowerInvariant().Replace("_", "").Replace("-", "");
        suffix = suffix.Length > 5 ? suffix[..5] : suffix;
        return core.Length == 0 ? $"b-{suffix}" : $"{core}-{suffix}";
    }
}

public static class Phone
{
    private const string BanglaDigits = "০১২৩৪৫৬৭৮৯";

    /// <summary>Normalizes Bangladeshi mobile numbers (Bangla or Latin digits) to E.164: +8801XXXXXXXXX.</summary>
    public static string? NormalizeBd(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = new StringBuilder();
        foreach (var ch in input)
        {
            var bn = BanglaDigits.IndexOf(ch);
            if (bn >= 0) digits.Append((char)('0' + bn));
            else if (char.IsAsciiDigit(ch)) digits.Append(ch);
        }
        var d = digits.ToString();
        if (d.StartsWith("880")) d = d[2..];
        if (d.Length == 11 && d.StartsWith("01") && d[2] is >= '3' and <= '9') return "+88" + d;
        return null;
    }
}
