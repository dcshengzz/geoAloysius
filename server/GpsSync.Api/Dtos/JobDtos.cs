namespace GpsSync.Api.Dtos;

/// <summary>Dispatch job as returned to the client. Mirrors the app's DispatchJobRecord.</summary>
public record JobDto(
    long Id,
    string EngineerUserId,
    string AdminUserId,
    string Title,
    string Description,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record CreateJobRequest(string EngineerUserId, string Title, string? Description);

public record UpdateJobRequest(string Status);
