using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Prism.Navigation;
using Shiny;

namespace GpsSync;

public class SplashViewModel : ViewModel
{
	private static bool _splashShown = false;

	private readonly IBackendClient backend;
	private readonly MySqliteConnection data;

	public SplashViewModel(BaseServices services, IBackendClient backend, MySqliteConnection data)
		: base(services)
	{
		this.backend = backend;
		this.data = data;
	}

	public override async void OnAppearing()
	{
		base.OnAppearing();
		// Force re-login when the app is updated
		string storedVersion = Preferences.Default.Get("app.version", string.Empty);
		string currentVersion = AppInfo.Current.VersionString + "." + AppInfo.Current.BuildString;
		if (!string.IsNullOrEmpty(storedVersion) && storedVersion != currentVersion)
		{
			Preferences.Default.Remove("keep_logged_in");
			SecureStorage.Default.Remove("api.access_token");
			SecureStorage.Default.Remove("api.refresh_token");
			SecureStorage.Default.Remove("api.user_id");
			SecureStorage.Default.Remove("device.session_token");
		}
		Preferences.Default.Set("app.version", currentVersion);
		// Show the 1.5s splash only on the first run of this process (cold start / after app is closed).
		if (!_splashShown)
		{
			_splashShown = true;
			await Task.WhenAll(Task.Delay(1500), data.InitializeAsync());
		}
		else
		{
			await data.InitializeAsync();
		}

		// Password reset is now an in-app 6-digit code flow (ForgotPasswordPage) — no email deep
		// link needed. Clear any stray pending deep-link URL so it does not linger.
		if (!string.IsNullOrEmpty(DeepLinkService.PendingUrl))
			DeepLinkService.PendingUrl = null;

		try
		{
			AuthUser? user = await backend.RestoreSessionAsync();
			if (user != null)
			{
				if (!await PassesDeviceCheck(user))
				{
					await ForceLogout();
					await base.Navigation.NavigateAsync("/LoginPage?displaced=true");
					return;
				}
				await base.Navigation.NavigateAsync("/NavigationPage/MainPage");
				return;
			}
		}
		catch
		{
			await ForceLogout();
		}
		await base.Navigation.NavigateAsync("/LoginPage");
	}

	/// <summary>
	/// Returns true if the current session is allowed to proceed on this device.
	/// Admins always pass. Non-admins are blocked if the server's active device token
	/// does not match the token stored locally. Returns false on any error (fail safe).
	/// </summary>
	private async Task<bool> PassesDeviceCheck(AuthUser user)
	{
		try
		{
			if (user.IsAdmin) return true;

			var profile = await backend.GetProfileAsync(user.Id);
			if (profile == null || profile.IsAdmin) return true;

			// No active token on server means no other device is registered — allow
			if (string.IsNullOrEmpty(profile.ActiveDeviceToken)) return true;

			string? storedToken = await SecureStorage.Default.GetAsync("device.session_token");
			return storedToken == profile.ActiveDeviceToken;
		}
		catch
		{
			// Cannot reach server — fail safe and require fresh login
			return false;
		}
	}

	private async Task ForceLogout()
	{
		try { await backend.LogoutAsync(); } catch { }
		SecureStorage.Default.Remove("device.session_token");
		Preferences.Default.Remove("keep_logged_in");
	}
}
