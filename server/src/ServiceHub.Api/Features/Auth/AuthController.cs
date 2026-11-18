using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Auth;

public record RegisterRequest(
    [Required, StringLength(120, MinimumLength = 2)] string FullName,
    [Required] string Phone,
    [EmailAddress] string? Email,
    [Required, MinLength(8)] string Password);

public record LoginRequest([Required] string Login, [Required] string Password, bool RememberMe = false);

public record RefreshRequest([Required] string RefreshToken);

public record SendOtpRequest([Required] string Phone, OtpPurpose Purpose);

public record VerifyPhoneRequest([Required, StringLength(6, MinimumLength = 6)] string Code);

public record ResetPasswordRequest([Required] string Phone, [Required] string Code, [Required, MinLength(8)] string NewPassword);

public record UserDto(Guid Id, string FullName, string Phone, string? Email, string? AvatarUrl, bool PhoneVerified,
    WorkspaceType LastWorkspaceType, Guid? LastActiveBusinessId);

public record AuthResponse(UserDto User, TokenPair Tokens);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    AppDbContext db,
    TokenService tokens,
    IPasswordHasher<User> hasher,
    ISmsSender sms,
    ICurrentUser me) : ControllerBase
{
    private const int OtpMinutes = 5;
    private const int OtpMaxAttempts = 5;

    public static UserDto ToDto(User u) => new(u.Id, u.FullName, u.Phone, u.Email, u.AvatarUrl,
        u.PhoneVerifiedAt != null, u.LastWorkspaceType, u.LastActiveBusinessId);

    /// <summary>Creates a customer account. Business pages are created later from the same account.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var phone = Phone.NormalizeBd(req.Phone) ?? throw new BadRequestException("সঠিক মোবাইল নম্বর লিখুন।", "invalid_phone");
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLowerInvariant();

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == phone))
            throw new ConflictException("এই মোবাইল নম্বর দিয়ে আগেই অ্যাকাউন্ট খোলা হয়েছে।", "phone_taken");
        if (email != null && await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email))
            throw new ConflictException("এই ইমেইল দিয়ে আগেই অ্যাকাউন্ট খোলা হয়েছে।", "email_taken");

        var user = new User { FullName = req.FullName.Trim(), Phone = phone, Email = email };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        db.Users.Add(user);
        var pair = tokens.Issue(user, rememberMe: false);
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Created("/api/me", new AuthResponse(ToDto(user), pair));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var login = req.Login.Trim();
        var phone = Phone.NormalizeBd(login);
        var email = login.Contains('@') ? login.ToLowerInvariant() : null;
        var user = await db.Users.FirstOrDefaultAsync(u => (phone != null && u.Phone == phone) || (email != null && u.Email == email));

        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
            throw new UnauthorizedException("মোবাইল নম্বর/ইমেইল অথবা পাসওয়ার্ড সঠিক নয়।", "invalid_credentials");
        if (user.Status != AccountStatus.Active)
            throw new ForbiddenException("আপনার অ্যাকাউন্টটি স্থগিত আছে।", "account_suspended");

        user.LastLoginAt = DateTime.UtcNow;
        var pair = tokens.Issue(user, req.RememberMe);
        await db.SaveChangesAsync();
        return new AuthResponse(ToDto(user), pair);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("session")] // runs on every page load; must not share the strict login budget
    public async Task<ActionResult<TokenPair>> Refresh(RefreshRequest req) => await tokens.RefreshAsync(req.RefreshToken);

    [HttpPost("logout")]
    [EnableRateLimiting("session")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        var hash = Tokens.Sha256(req.RefreshToken);
        await db.UserSessions.Where(s => s.RefreshTokenHash == hash && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        return NoContent();
    }

    /// <summary>Sends a 6-digit code by SMS. Always returns 202 so it can't be used to probe accounts.</summary>
    [HttpPost("otp")]
    public async Task<IActionResult> SendOtp(SendOtpRequest req)
    {
        if (req.Purpose == OtpPurpose.EmailVerification)
            throw new BadRequestException("ইমেইল যাচাই এখনো চালু হয়নি।", "not_supported");
        var phone = Phone.NormalizeBd(req.Phone) ?? throw new BadRequestException("সঠিক মোবাইল নম্বর লিখুন।", "invalid_phone");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (user is null) return Accepted();

        var recent = await db.VerificationCodes.CountAsync(c =>
            c.Target == phone && c.CreatedAt > DateTime.UtcNow.AddMinutes(-15));
        if (recent >= 3)
            throw new AppException(StatusCodes.Status429TooManyRequests, "otp_rate_limited",
                "অনেকবার কোড চাওয়া হয়েছে। কিছুক্ষণ পর আবার চেষ্টা করুন।");

        var code = Tokens.NumericCode();
        db.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id, Target = phone, Purpose = req.Purpose,
            CodeHash = Tokens.Sha256($"{phone}:{code}"),
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpMinutes),
        });
        await db.SaveChangesAsync();
        await sms.SendAsync(phone, $"ServiceHub BD যাচাই কোড: {code}। মেয়াদ {OtpMinutes} মিনিট।");
        return Accepted();
    }

    [Authorize]
    [HttpPost("verify-phone")]
    public async Task<ActionResult<UserDto>> VerifyPhone(VerifyPhoneRequest req)
    {
        var user = await db.Users.FindAsync(me.RequireId()) ?? throw new UnauthorizedException();
        await ConsumeCodeAsync(user.Phone, OtpPurpose.PhoneVerification, req.Code);
        user.PhoneVerifiedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToDto(user);
    }

    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        var phone = Phone.NormalizeBd(req.Phone) ?? throw new BadRequestException("সঠিক মোবাইল নম্বর লিখুন।", "invalid_phone");
        await ConsumeCodeAsync(phone, OtpPurpose.PasswordReset, req.Code);
        var user = await db.Users.FirstAsync(u => u.Phone == phone);
        user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
        user.PhoneVerifiedAt ??= DateTime.UtcNow;
        // A password reset signs out every device.
        await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task ConsumeCodeAsync(string phone, OtpPurpose purpose, string code)
    {
        var entry = await db.VerificationCodes
            .Where(c => c.Target == phone && c.Purpose == purpose && c.ConsumedAt == null && c.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
        var invalid = new BadRequestException("কোডটি সঠিক নয় অথবা মেয়াদ শেষ হয়ে গেছে।", "invalid_code");
        if (entry is null || entry.Attempts >= OtpMaxAttempts) throw invalid;
        if (entry.CodeHash != Tokens.Sha256($"{phone}:{code}"))
        {
            entry.Attempts++;
            await db.SaveChangesAsync();
            throw invalid;
        }
        entry.ConsumedAt = DateTime.UtcNow;
    }
}
