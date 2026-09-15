namespace GpsSync.Api.Dtos;

public record RegisterRequest(string Email, string Password);

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record LogoutRequest(string? RefreshToken);

public record UpdateUserRequest(string? DisplayName);

public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);

/// <summary>User info returned to the client (parallels Supabase's User).</summary>
public record UserDto(string Id, string? Email, string? DisplayName, bool IsAdmin);

/// <summary>Returned by login/refresh (parallels Supabase's Session).</summary>
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    UserDto User);
