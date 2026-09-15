using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GpsSync.Api.Data;
using GpsSync.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace GpsSync.Api.Services;

public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
}

public interface ITokenService
{
    string CreateAccessToken(AppUser user, bool isAdmin, out int expiresInSeconds);
    Task<string> IssueRefreshTokenAsync(string userId, CancellationToken ct = default);
    Task<RefreshToken?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default);
    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeByRawAsync(string rawToken, CancellationToken ct = default);
}

public class TokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly AppDbContext _db;

    public TokenService(JwtOptions jwt, AppDbContext db)
    {
        _jwt = jwt;
        _db = db;
    }

    public string CreateAccessToken(AppUser user, bool isAdmin, out int expiresInSeconds)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);
        expiresInSeconds = (int)(expires - now).TotalSeconds;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("is_admin", isAdmin ? "true" : "false"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "admin"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<string> IssueRefreshTokenAsync(string userId, CancellationToken ct = default)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays)
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var hash = Hash(rawToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        return token is { IsActive: true } ? token : null;
    }

    public async Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeByRawAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = Hash(rawToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (token is { RevokedAtUtc: null })
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
