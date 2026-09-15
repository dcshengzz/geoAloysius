namespace GpsSync.Api.Models;

/// <summary>
/// Server-side refresh token. We store only a SHA-256 hash of the token value.
/// Replaces Supabase GoTrue's refresh-token handling.
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc == null && DateTime.UtcNow < ExpiresAtUtc;
}
