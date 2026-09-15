namespace GpsSync.Api.Dtos;

/// <summary>GPS ping as returned to the client. Mirrors the app's GpsPingRecord.</summary>
public record GpsPingDto(
    long Id,
    string UserId,
    double Latitude,
    double Longitude,
    string? DisplayName,
    DateTimeOffset? CreatedAt);

public record CreatePingRequest(double Latitude, double Longitude, string? DisplayName);
