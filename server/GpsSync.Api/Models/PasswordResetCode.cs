namespace GpsSync.Api.Models;

/// <summary>
/// Short-lived 6-digit password reset code. We store only a SHA-256 hash of the code.
/// Replaces Supabase GoTrue's recovery-OTP flow.
/// </summary>
public class PasswordResetCode
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool Used { get; set; }
}
