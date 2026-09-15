using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Prism.Navigation;
using Shiny;
using Supabase;
using Supabase.Gotrue;

namespace GpsSync;

public class SplashViewModel : ViewModel
{
	private static bool _splashShown = false;

	private readonly Supabase.Client supabase;
	private readonly MySqliteConnection data;

	public SplashViewModel(BaseServices services, Supabase.Client supabase, MySqliteConnection data)
		: base(services)
	{
		this.supabase = supabase;
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
			SecureStorage.Default.Remove("sb.access_token");
			SecureStorage.Default.Remove("sb.refresh_token");
			SecureStorage.Default.Remove("device.session_token");
		}
		Preferences.Default.Set("app.version", currentVersion);
		// Show the 1.5s splash only on the first run of this process (cold start / after app is closed).
		// _splashShown is a static field that resets to false when the process dies, so it reliably
		// distinguishes a fresh launch from returning to the app from the background.
		if (!_splashShown)
		{
			_splashShown = true;
			await Task.WhenAll(Task.Delay(1500), data.InitializeAsync());
		}
		else
		{
			await data.InitializeAsync();
		}
		string pendingUrl = DeepLinkService.PendingUrl;
		if (!string.IsNullOrEmpty(pendingUrl))
		{
			DeepLinkService.PendingUrl = null;
			var (accessToken, refreshToken, type) = DeepLinkService.ParseUrl(pendingUrl);
			if (type == "recovery" && !string.IsNullOrEmpty(accessToken))
			{
				await base.Navigation.NavigateAsync("/ResetPasswordPage?token=" + Uri.EscapeDataString(accessToken));
				return;
			}
			if (type == "signup" || type == "email_change")
			{
				// Email verified - auto-login if tokens are valid (first click only)
				bool verificationSucceeded = false;
				if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
				{
					try
					{
						await supabase.Auth.SetSession(accessToken, refreshToken);
						if (supabase.Auth.CurrentSession?.User != null)
						{
							verificationSucceeded = true;
							if (!await PassesDeviceCheck())
							{
								await ForceLogout();
								await base.Navigation.NavigateAsync("/LoginPage?displaced=true");
								return;
							}
							await base.Navigation.NavigateAsync("/NavigationPage/MainPage");
							return;
						}
						verificationSucceeded = true;
					}
					catch { }
				}
				if (verificationSucceeded)
					await base.Navigation.NavigateAsync("/LoginPage?verified=true");
				else
					await base.Navigation.NavigateAsync("/LoginPage");
				return;
			}
		}
		try
		{
				string access = await SecureStorage.Default.GetAsync("sb.access_token");
				string refresh = await SecureStorage.Default.GetAsync("sb.refresh_token");
				if (!string.IsNullOrEmpty(access) && !string.IsNullOrEmpty(refresh))
				{
					await supabase.Auth.SetSession(access, refresh);
					if (supabase.Auth.CurrentSession?.User != null)
					{
						if (!await PassesDeviceCheck())
						{
							await ForceLogout();
							await base.Navigation.NavigateAsync("/LoginPage?displaced=true");
							return;
						}
						Session s = supabase.Auth.CurrentSession;
						if (s.AccessToken != null)
						{
							await SecureStorage.Default.SetAsync("sb.access_token", s.AccessToken);
						}
						if (s.RefreshToken != null)
						{
							await SecureStorage.Default.SetAsync("sb.refresh_token", s.RefreshToken);
						}
						await base.Navigation.NavigateAsync("/NavigationPage/MainPage");
						return;
					}
				}
			}
			catch
			{
				SecureStorage.Default.Remove("sb.access_token");
				SecureStorage.Default.Remove("sb.refresh_token");
			}
		await base.Navigation.NavigateAsync("/LoginPage");
	}

	/// <summary>
	/// Returns true if the current session is allowed to proceed on this device.
	/// Admins always pass. Non-admins are blocked if the server's active device token
	/// does not match the token stored locally (meaning another device is active).
	/// Returns false on any error so we fail safe.
	/// </summary>
	private async Task<bool> PassesDeviceCheck()
	{
		try
		{
			string? userId = supabase.Auth.CurrentUser?.Id;
			if (string.IsNullOrEmpty(userId)) return false;

			var profile = await supabase.From<ProfileRecord>()
				.Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId)
				.Single();

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
		try { await supabase.Auth.SignOut(); } catch { }
		SecureStorage.Default.Remove("sb.access_token");
		SecureStorage.Default.Remove("sb.refresh_token");
		SecureStorage.Default.Remove("device.session_token");
		Preferences.Default.Remove("keep_logged_in");
	}
}
