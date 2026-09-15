namespace GpsSync.Api.Dtos;

/// <summary>Profile as returned to the client. Mirrors the app's ProfileRecord.</summary>
public record ProfileDto(
    string UserId,
    bool IsAdmin,
    string? DisplayName,
    string? Email,
    bool IsPunchedIn,
    string? ActiveDeviceToken);

/// <summary>
/// Partial update. Only non-null fields are applied, so the client can PATCH
/// just active_device_token, display_name, or is_punched_in individually.
/// </summary>
public record UpdateProfileRequest(
    string? DisplayName,
    bool? IsPunchedIn,
    string? ActiveDeviceToken,
    bool? ClearActiveDeviceToken);
