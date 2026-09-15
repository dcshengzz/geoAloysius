namespace GpsSync.Api.Models;

/// <summary>
/// Mirrors the old Supabase "gps_pings" table (see GpsPingRecord.cs).
/// Included now so the schema is complete in one migration; controllers arrive in the GPS slice.
/// </summary>
public class GpsPing
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? DisplayName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
