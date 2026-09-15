using System.ComponentModel.DataAnnotations;

namespace GpsSync.Api.Models;

/// <summary>
/// Mirrors the old Supabase "profiles" table (see ProfileRecord.cs in the MAUI app).
/// One row per user; UserId is both PK and FK to AppUser.Id.
/// </summary>
public class Profile
{
    [Key]
    public string UserId { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public string? DisplayName { get; set; }

    public string? Email { get; set; }

    public bool IsPunchedIn { get; set; }

    /// <summary>Single-device enforcement token; matched against the device's stored token.</summary>
    public string? ActiveDeviceToken { get; set; }
}
