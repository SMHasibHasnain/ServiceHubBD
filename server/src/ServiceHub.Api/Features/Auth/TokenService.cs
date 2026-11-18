using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain.Entities;

namespace ServiceHub.Api.Features.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "servicehub-bd";
    public string Audience { get; set; } = "servicehub-bd-clients";
    /// <summary>HMAC-SHA256 signing key, at least 32 bytes. Set via user-secrets / environment in production.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 1;
    public int RememberMeRefreshTokenDays { get; set; } = 30;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public record TokenPair(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

public class TokenService(IOptions<JwtOptions> options, AppDbContext db, ICurrentUser me, IHttpContextAccessor http)
{
    private readonly JwtOptions _o = options.Value;

    /// <summary>Issues an access token and opens a new refresh-token session.</summary>
    public TokenPair Issue(User user, bool rememberMe)
    {
        var refresh = Tokens.NewToken();
        var refreshExpires = DateTime.UtcNow.AddDays(rememberMe ? _o.RememberMeRefreshTokenDays : _o.RefreshTokenDays);
        db.UserSessions.Add(new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = Tokens.Sha256(refresh),
            RememberMe = rememberMe,
            ExpiresAt = refreshExpires,
            IpAddress = me.IpAddress,
            UserAgent = http.HttpContext?.Request.Headers.UserAgent.ToString(),
        });
        var (access, accessExpires) = CreateAccessToken(user);
        return new TokenPair(access, accessExpires, refresh, refreshExpires);
    }

    /// <summary>Rotates a refresh token: the old session is revoked and a new one is issued.</summary>
    public async Task<TokenPair> RefreshAsync(string refreshToken)
    {
        var hash = Tokens.Sha256(refreshToken);
        var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.UserSessions, s => s.RefreshTokenHash == hash);
        if (session is null || session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedException("সেশনের মেয়াদ শেষ। আবার লগইন করুন।", "invalid_refresh_token");

        var user = await db.Users.FindAsync(session.UserId);
        if (user is null || user.Status != Domain.AccountStatus.Active)
            throw new UnauthorizedException("অ্যাকাউন্টটি সক্রিয় নয়।", "account_inactive");

        session.RevokedAt = DateTime.UtcNow;
        session.LastUsedAt = DateTime.UtcNow;
        var pair = Issue(user, session.RememberMe);
        await db.SaveChangesAsync();
        return pair;
    }

    private (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_o.AccessTokenMinutes);
        var token = new JwtSecurityToken(
            issuer: _o.Issuer,
            audience: _o.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.FullName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(_o.Key(), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
