using System;

namespace GpsSync;

public class AdminJobItem
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string EngineerName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime CreatedAtLocal => CreatedAt;
    public DateTime? CompletedAtLocal => CompletedAt;
}
