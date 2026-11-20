using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ServiceHub.Api.Common;

public class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Folder for uploaded files. Relative paths are resolved against the content root.</summary>
    public string LocalPath { get; set; } = "App_Data/uploads";

    /// <summary>URL prefix the files are served under.</summary>
    public string PublicPath { get; set; } = "/uploads";

    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;
}

/// <summary>
/// Where uploaded files live. The local-disk implementation is for a single server; an object-storage
/// implementation (R2/S3) can replace it without touching callers, since they only see public URLs.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores the bytes under <paramref name="folder"/> with a random name and returns the public URL.</summary>
    Task<string> SaveAsync(Stream content, string folder, string extension, CancellationToken ct = default);

    /// <summary>Deletes a file previously returned by <see cref="SaveAsync"/>. URLs this storage did not create are ignored.</summary>
    Task DeleteAsync(string url, CancellationToken ct = default);

    /// <summary>True when the URL points at a file inside <paramref name="folder"/> of this storage.</summary>
    bool Owns(string url, string folder);
}

public partial class LocalDiskStorage : IFileStorage
{
    private readonly string _root;
    private readonly string _publicPath;

    public LocalDiskStorage(IOptions<StorageOptions> options, IWebHostEnvironment env)
    {
        var o = options.Value;
        _root = Path.GetFullPath(Path.IsPathRooted(o.LocalPath) ? o.LocalPath : Path.Combine(env.ContentRootPath, o.LocalPath));
        _publicPath = "/" + o.PublicPath.Trim('/');
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task<string> SaveAsync(Stream content, string folder, string extension, CancellationToken ct = default)
    {
        if (!SafeSegment().IsMatch(folder) || !SafeSegment().IsMatch(extension))
            throw new ArgumentException("Invalid folder or extension.");
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.CreateVersion7():N}.{extension}";
        await using (var file = File.Create(Path.Combine(dir, name)))
            await content.CopyToAsync(file, ct);
        return $"{_publicPath}/{folder}/{name}";
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        var path = ToPath(url);
        if (path is not null && File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public bool Owns(string url, string folder) => url.StartsWith($"{_publicPath}/{folder}/", StringComparison.Ordinal) && ToPath(url) is not null;

    private string? ToPath(string url)
    {
        if (!url.StartsWith(_publicPath + "/", StringComparison.Ordinal)) return null;
        var parts = url[(_publicPath.Length + 1)..].Split('/');
        if (parts.Length != 2 || !parts.All(p => SafeSegment().IsMatch(p.Replace(".", "")))) return null;
        var full = Path.GetFullPath(Path.Combine(_root, parts[0], parts[1]));
        return full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    [GeneratedRegex("^[a-zA-Z0-9-]{1,64}$")]
    private static partial Regex SafeSegment();
}

/// <summary>Recognises images by their first bytes, not by the file name or the client's content type.</summary>
public static class ImageSniffer
{
    /// <summary>Returns "jpg", "png" or "webp", or null for anything else (including SVG, which can carry scripts).</summary>
    public static string? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "jpg";
        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }
}

/// <summary>
/// Reads the "file" field of a multipart request, checks its size and real type (magic bytes), and stores it.
/// Shared by every upload endpoint (business page, review photos, inspection photos).
/// </summary>
public static class ImageUpload
{
    public static async Task<(string Url, IFormCollection? Form)> SaveAsync(this IFileStorage storage, HttpRequest request,
        string folder, int maxBytes, CancellationToken ct)
    {
        // Read the form by hand so a missing file gets our Bangla error, not the generic model-validation one.
        IFormCollection? form = null;
        try { if (request.HasFormContentType) form = await request.ReadFormAsync(ct); }
        catch (InvalidDataException) { /* malformed or empty multipart body: same as no file */ }
        var file = form?.Files.GetFile("file");
        if (file is null || file.Length == 0)
            throw new BadRequestException("একটি ছবি বাছাই করুন।", "file_required");
        if (file.Length > maxBytes)
            throw new BadRequestException($"ছবির আকার সর্বোচ্চ {Bn(maxBytes / 1024 / 1024)} এমবি হতে পারে।", "file_too_large");

        await using var stream = file.OpenReadStream();
        var head = new byte[12];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        var ext = ImageSniffer.Detect(head.AsSpan(0, read))
            ?? throw new BadRequestException("শুধু JPG, PNG বা WebP ছবি আপলোড করা যায়।", "unsupported_file_type");
        stream.Position = 0;
        return (await storage.SaveAsync(stream, folder, ext, ct), form);
    }

    private static string Bn(long n) => string.Concat(n.ToString().Select(c => char.IsDigit(c) ? "০১২৩৪৫৬৭৮৯"[c - '0'] : c));
}

/// <summary>Rules for image URLs stored on public page content.</summary>
public static class ImageUrls
{
    /// <summary>Our own uploads (<c>/uploads/...</c>) or an https link. Rejects javascript:, data: and the like.</summary>
    public static bool IsAllowed(string url) =>
        (url.StartsWith("/uploads/", StringComparison.Ordinal) && !url.Contains("..")) ||
        (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps);

    public static void Ensure(string? url)
    {
        if (!string.IsNullOrEmpty(url) && !IsAllowed(url))
            throw new BadRequestException("ছবির লিংকটি সঠিক নয়। ছবি আপলোড করুন অথবা https লিংক দিন।", "invalid_image_url");
    }
}

/// <summary>
/// Keeps bookings on the platform: public page text written by businesses may not carry phone numbers.
/// Bangla digits and common separators (spaces, dashes, dots) are recognised.
/// </summary>
public static partial class ContactGuard
{
    public static bool HasPhone(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var latin = string.Concat(text.Select(c => c is >= '০' and <= '৯' ? (char)('0' + (c - '০')) : c));
        return BdMobile().IsMatch(latin);
    }

    public static void Ensure(params string?[] texts)
    {
        if (texts.Any(HasPhone))
            throw new BadRequestException("পাবলিক পেজে ফোন নম্বর দেওয়া যাবে না। গ্রাহকরা প্ল্যাটফর্মের মাধ্যমেই বুকিং ও যোগাযোগ করবেন।", "contact_info_not_allowed");
    }

    [GeneratedRegex(@"(?<!\d)(?:\+?8[\s.\-]*8[\s.\-]*)?0[\s.\-]*1[\s.\-]*[3-9](?:[\s.\-]*\d){8}(?!\d)")]
    private static partial Regex BdMobile();
}
