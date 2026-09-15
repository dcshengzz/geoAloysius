using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace GpsSync;

public record AuthUser(string Id, string? Email, string? DisplayName, bool IsAdmin);
public record AuthResult(string AccessToken, string RefreshToken, int ExpiresInSeconds, AuthUser User);
public record BackendProfile(string UserId, bool IsAdmin, string? DisplayName, string? Email, bool IsPunchedIn, string? ActiveDeviceToken);
public record BackendJob(long Id, string EngineerUserId, string AdminUserId, string Title, string Description, string Status, DateTime CreatedAt, DateTime? CompletedAt);
public record BackendPing(long Id, string UserId, double Latitude, double Longitude, string? DisplayName, DateTimeOffset? CreatedAt);

/// <summary>
/// Talks to the new SQL Server-backed ASP.NET Core API (see /server). Replaces the parts of the
/// Supabase SDK the app used for auth + profiles. Handles JWT storage and transparent refresh.
/// </summary>
public interface IBackendClient
{
    string? CurrentUserId { get; }
    bool IsSignedIn { get; }

    Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task RegisterAsync(string email, string password, CancellationToken ct = default);
    Task ForgotPasswordAsync(string email, CancellationToken ct = default);
    Task ResetPasswordAsync(string email, string code, string newPassword, CancellationToken ct = default);
    /// <summary>Restores a session from stored tokens. Returns the current user, or null if none/invalid.</summary>
    Task<AuthUser?> RestoreSessionAsync(CancellationToken ct = default);
    /// <summary>Loads tokens + CurrentUserId from secure storage into memory (no network). Safe to call repeatedly.</summary>
    Task EnsureSessionLoadedAsync();
    Task LogoutAsync(CancellationToken ct = default);

    Task<BackendProfile?> GetProfileAsync(string userId, CancellationToken ct = default);
    Task<BackendProfile?> UpdateProfileAsync(
        string userId,
        string? displayName = null,
        bool? isPunchedIn = null,
        string? activeDeviceToken = null,
        bool clearActiveDeviceToken = false,
        CancellationToken ct = default);
    Task UpdateDisplayNameAsync(string displayName, CancellationToken ct = default);

    // ---- Jobs (dispatch_jobs) ----
    Task<IReadOnlyList<BackendProfile>> ListProfilesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BackendJob>> GetJobsAsync(string? engineerUserId = null, string? adminUserId = null, string? status = null, CancellationToken ct = default);
    Task<BackendJob> CreateJobAsync(string engineerUserId, string title, string? description, CancellationToken ct = default);
    Task<BackendJob?> UpdateJobStatusAsync(long id, string status, CancellationToken ct = default);
    Task<int> ClearCompletedJobsAsync(CancellationToken ct = default);

    // ---- GPS (gps_pings) + admin user management ----
    Task PostPingAsync(double latitude, double longitude, string? displayName, CancellationToken ct = default);
    Task<IReadOnlyList<BackendPing>> GetPingsAsync(string userId, int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<BackendPing>> GetLatestPingsAsync(CancellationToken ct = default);
    Task DeletePingsAsync(string userId, CancellationToken ct = default);
    Task DeleteUserAsync(string userId, CancellationToken ct = default);
}

public class BackendClient : IBackendClient
{
    private const string AccessKey = "api.access_token";
    private const string RefreshKey = "api.refresh_token";
    private const string UserIdKey = "api.user_id";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private bool _loaded;
    private string? _access;
    private string? _refresh;

    public string? CurrentUserId { get; private set; }
    public bool IsSignedIn => !string.IsNullOrEmpty(_access);

    public BackendClient(HttpClient http)
    {
        _http = http;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(AppConfig.ApiBaseUrl);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        HttpResponseMessage resp;
        try { resp = await _http.PostAsJsonAsync("auth/login", new { email, password }, Json, ct); }
        catch (Exception ex) { throw ToNetworkException(ex); }

        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);

        var result = await resp.Content.ReadFromJsonAsync<AuthResult>(Json, ct)
            ?? throw new Exception("Empty response from server.");
        await StoreSessionAsync(result);
        return result;
    }

    public async Task RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        HttpResponseMessage resp;
        try { resp = await _http.PostAsJsonAsync("auth/register", new { email, password }, Json, ct); }
        catch (Exception ex) { throw ToNetworkException(ex); }

        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        HttpResponseMessage resp;
        try { resp = await _http.PostAsJsonAsync("auth/forgot-password", new { email }, Json, ct); }
        catch (Exception ex) { throw ToNetworkException(ex); }
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword, CancellationToken ct = default)
    {
        HttpResponseMessage resp;
        try { resp = await _http.PostAsJsonAsync("auth/reset-password", new { email, code, newPassword }, Json, ct); }
        catch (Exception ex) { throw ToNetworkException(ex); }
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
    }

    public async Task<AuthUser?> RestoreSessionAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync();
        if (string.IsNullOrEmpty(_access) && string.IsNullOrEmpty(_refresh))
            return null;

        HttpResponseMessage resp;
        try
        {
            resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, "auth/me"), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Network problem while restoring — treat as not signed in (caller falls back to login).
            return null;
        }

        if (!resp.IsSuccessStatusCode)
        {
            await ClearSessionAsync();
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<AuthUser>(Json, ct);
    }

    public Task EnsureSessionLoadedAsync() => EnsureLoadedAsync();

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync();
        try
        {
            if (!string.IsNullOrEmpty(_refresh))
            {
                var refresh = _refresh;
                await SendAuthedAsync(() =>
                {
                    var r = new HttpRequestMessage(HttpMethod.Post, "auth/logout");
                    r.Content = JsonContent.Create(new { refreshToken = refresh }, options: Json);
                    return r;
                }, ct);
            }
        }
        catch { /* best-effort */ }
        finally { await ClearSessionAsync(); }
    }

    public async Task<BackendProfile?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"profiles/{userId}"), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<BackendProfile>(Json, ct);
    }

    public async Task<BackendProfile?> UpdateProfileAsync(
        string userId,
        string? displayName = null,
        bool? isPunchedIn = null,
        string? activeDeviceToken = null,
        bool clearActiveDeviceToken = false,
        CancellationToken ct = default)
    {
        var body = new
        {
            displayName,
            isPunchedIn,
            activeDeviceToken,
            clearActiveDeviceToken = clearActiveDeviceToken ? true : (bool?)null
        };
        var resp = await SendAuthedAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Patch, $"profiles/{userId}");
            r.Content = JsonContent.Create(body, options: Json);
            return r;
        }, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<BackendProfile>(Json, ct);
    }

    public async Task UpdateDisplayNameAsync(string displayName, CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Patch, "auth/user");
            r.Content = JsonContent.Create(new { displayName }, options: Json);
            return r;
        }, ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
    }

    // ---- Jobs ----

    public async Task<IReadOnlyList<BackendProfile>> ListProfilesAsync(CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, "profiles"), ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<List<BackendProfile>>(Json, ct) ?? new List<BackendProfile>();
    }

    public async Task<IReadOnlyList<BackendJob>> GetJobsAsync(string? engineerUserId = null, string? adminUserId = null, string? status = null, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(engineerUserId)) qs.Add("engineerUserId=" + Uri.EscapeDataString(engineerUserId));
        if (!string.IsNullOrEmpty(adminUserId)) qs.Add("adminUserId=" + Uri.EscapeDataString(adminUserId));
        if (!string.IsNullOrEmpty(status)) qs.Add("status=" + Uri.EscapeDataString(status));
        var url = "jobs" + (qs.Count > 0 ? "?" + string.Join("&", qs) : string.Empty);

        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<List<BackendJob>>(Json, ct) ?? new List<BackendJob>();
    }

    public async Task<BackendJob> CreateJobAsync(string engineerUserId, string title, string? description, CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "jobs");
            r.Content = JsonContent.Create(new { engineerUserId, title, description }, options: Json);
            return r;
        }, ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<BackendJob>(Json, ct)
            ?? throw new Exception("Empty response from server.");
    }

    public async Task<BackendJob?> UpdateJobStatusAsync(long id, string status, CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Patch, $"jobs/{id}");
            r.Content = JsonContent.Create(new { status }, options: Json);
            return r;
        }, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<BackendJob>(Json, ct);
    }

    public async Task<int> ClearCompletedJobsAsync(CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Delete, "jobs/completed"), ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        var doc = await resp.Content.ReadFromJsonAsync<DeletedResult>(Json, ct);
        return doc?.Deleted ?? 0;
    }

    private record DeletedResult(int Deleted);

    // ---- GPS ----

    public async Task PostPingAsync(double latitude, double longitude, string? displayName, CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() =>
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "gps");
            r.Content = JsonContent.Create(new { latitude, longitude, displayName }, options: Json);
            return r;
        }, ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
    }

    public async Task<IReadOnlyList<BackendPing>> GetPingsAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        var url = $"gps?userId={Uri.EscapeDataString(userId)}&limit={limit}";
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<List<BackendPing>>(Json, ct) ?? new List<BackendPing>();
    }

    public async Task<IReadOnlyList<BackendPing>> GetLatestPingsAsync(CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, "gps/latest"), ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
        return await resp.Content.ReadFromJsonAsync<List<BackendPing>>(Json, ct) ?? new List<BackendPing>();
    }

    public async Task DeletePingsAsync(string userId, CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"gps?userId={Uri.EscapeDataString(userId)}"), ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
    }

    public async Task DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var resp = await SendAuthedAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"users/{Uri.EscapeDataString(userId)}"), ct);
        if (!resp.IsSuccessStatusCode) throw await ToApiException(resp);
    }

    // ---- internals ----

    private async Task<HttpResponseMessage> SendAuthedAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        await EnsureLoadedAsync();

        var req = make();
        if (!string.IsNullOrEmpty(_access))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _access);

        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct); }
        catch (Exception ex) { throw ToNetworkException(ex); }

        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAsync(ct))
        {
            var retry = make();
            retry.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _access);
            try { resp = await _http.SendAsync(retry, ct); }
            catch (Exception ex) { throw ToNetworkException(ex); }
        }
        return resp;
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_refresh)) return false;
        HttpResponseMessage resp;
        try { resp = await _http.PostAsJsonAsync("auth/refresh", new { refreshToken = _refresh }, Json, ct); }
        catch { return false; }

        if (!resp.IsSuccessStatusCode)
        {
            await ClearSessionAsync();
            return false;
        }
        var result = await resp.Content.ReadFromJsonAsync<AuthResult>(Json, ct);
        if (result is null) return false;
        await StoreSessionAsync(result);
        return true;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            _access = await SecureStorage.Default.GetAsync(AccessKey);
            _refresh = await SecureStorage.Default.GetAsync(RefreshKey);
            CurrentUserId = await SecureStorage.Default.GetAsync(UserIdKey);
        }
        catch { /* SecureStorage can throw on some devices; treat as no session */ }
    }

    private async Task StoreSessionAsync(AuthResult result)
    {
        _access = result.AccessToken;
        _refresh = result.RefreshToken;
        CurrentUserId = result.User.Id;
        _loaded = true;
        try
        {
            await SecureStorage.Default.SetAsync(AccessKey, result.AccessToken);
            await SecureStorage.Default.SetAsync(RefreshKey, result.RefreshToken);
            await SecureStorage.Default.SetAsync(UserIdKey, result.User.Id);
        }
        catch { /* ignore persistence failures */ }
    }

    private Task ClearSessionAsync()
    {
        _access = null;
        _refresh = null;
        CurrentUserId = null;
        try
        {
            SecureStorage.Default.Remove(AccessKey);
            SecureStorage.Default.Remove(RefreshKey);
            SecureStorage.Default.Remove(UserIdKey);
        }
        catch { }
        return Task.CompletedTask;
    }

    private static Exception ToNetworkException(Exception ex)
        // Message contains "unable to connect" so the view models' ToFriendlyError maps it to a
        // friendly "Connection failed. Check your internet and try again."
        => new Exception("Unable to connect to the server. Please check your connection and try again.", ex);

    private static async Task<Exception> ToApiException(HttpResponseMessage resp)
    {
        string? message = null;
        try
        {
            var doc = await resp.Content.ReadFromJsonAsync<ApiError>(Json);
            message = doc?.Message;
        }
        catch { /* non-JSON body */ }
        return new Exception(string.IsNullOrWhiteSpace(message)
            ? $"Request failed ({(int)resp.StatusCode})."
            : message);
    }

    private record ApiError(string? Message);
}
