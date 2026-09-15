namespace GpsSync.Api.Models;

/// <summary>
/// Mirrors the old Supabase "dispatch_jobs" table (see DispatchJobRecord.cs).
/// Included now so the schema is complete in one migration; controllers arrive in the jobs slice.
/// </summary>
public class DispatchJob
{
    public long Id { get; set; }
    public string EngineerUserId { get; set; } = string.Empty;
    public string AdminUserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
