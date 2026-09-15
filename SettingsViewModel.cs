using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Prism.Navigation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;
using Shiny.Locations;
using Supabase;
using Supabase.Gotrue;
using Supabase.Postgrest;

namespace GpsSync;

public class SettingsViewModel : ViewModel
{
	private readonly AppSettings settings;
	private readonly IGpsManager gpsManager;
	private readonly Supabase.Client supabase;

	[Reactive] public bool IsNotificationsEnabled { get; set; }
	[Reactive] public bool IsDarkMode { get; set; }
	[Reactive] public bool NotifyPunchInOut { get; set; }
	[Reactive] public bool NotifyJobStatus { get; set; }
	[Reactive] public string DisplayName { get; set; } = string.Empty;
	[Reactive] public bool IsAdmin { get; private set; }


	public ICommand SaveDisplayName { get; }
	public ICommand Logout { get; }

	public SettingsViewModel(BaseServices services, AppSettings settings, IGpsManager gpsManager, Supabase.Client supabase)
		: base(services)
	{
		SettingsViewModel settingsViewModel = this;
		this.settings = settings;
		this.gpsManager = gpsManager;
		this.supabase = supabase;

		IsNotificationsEnabled = settings.IsNotificationsEnabled;
		IsDarkMode = Preferences.Default.Get("app.darkmode", defaultValue: false);
		NotifyPunchInOut = Preferences.Default.Get("admin.notify_punch", defaultValue: true);
		NotifyJobStatus = Preferences.Default.Get("admin.notify_job_status", defaultValue: true);

		this.WhenAnyValue(x => x.IsNotificationsEnabled).Skip(1)
			.Subscribe(x => settings.IsNotificationsEnabled = x);

		this.WhenAnyValue(x => x.IsDarkMode).Skip(1)
			.Subscribe(x =>
			{
				Preferences.Default.Set("app.darkmode", x);
				Application.Current.UserAppTheme = x ? AppTheme.Dark : AppTheme.Light;
			});

		this.WhenAnyValue(x => x.NotifyPunchInOut).Skip(1)
			.Subscribe(x => Preferences.Default.Set("admin.notify_punch", x));

		this.WhenAnyValue(x => x.NotifyJobStatus).Skip(1)
			.Subscribe(x => Preferences.Default.Set("admin.notify_job_status", x));

		SaveDisplayName = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (!string.IsNullOrWhiteSpace(settingsViewModel.DisplayName))
			{
				try
				{
					await settingsViewModel.supabase.Auth.Update(new UserAttributes
					{
						Data = new Dictionary<string, object> { ["display_name"] = settingsViewModel.DisplayName.Trim() }
					});
					try
					{
						string userId = settingsViewModel.supabase.Auth.CurrentUser?.Id;
						if (!string.IsNullOrEmpty(userId))
						{
							await settingsViewModel.supabase.From<ProfileRecord>()
								.Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId)
								.Set(x => x.DisplayName, settingsViewModel.DisplayName.Trim())
								.Update();
						}
					}
					catch { }
					await settingsViewModel.Dialogs.Alert("Username saved.", "Done");
				}
				catch
				{
					await settingsViewModel.Dialogs.Alert("Failed to save username. Try again.", "Error");
				}
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);

		Logout = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (settingsViewModel.settings.IsPunchedIn)
			{
				await settingsViewModel.Dialogs.Alert("You are still punched in. Please punch out before signing out.", "Punch Out First");
				return;
			}
			bool confirmed = await settingsViewModel.Dialogs.Confirm("Are you sure you want to sign out?", "Sign Out");
			if (!confirmed) return;
			try { await settingsViewModel.gpsManager.StopListener(); } catch { }
			try { await settingsViewModel.supabase.Auth.SignOut(); } catch { }
			// Clear single-device token
			try
			{
				string userId = settingsViewModel.supabase.Auth.CurrentUser?.Id;
				if (!string.IsNullOrEmpty(userId))
				{
					await settingsViewModel.supabase.From<ProfileRecord>()
						.Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId)
						.Set(x => x.ActiveDeviceToken, (string?)null)
						.Update();
				}
			}
			catch { }
			SecureStorage.Default.Remove("device.session_token");
			SecureStorage.Default.Remove("sb.access_token");
			SecureStorage.Default.Remove("sb.refresh_token");
			// Clear all user-specific notification and badge state so the next account starts clean
			Preferences.Default.Remove("jobs.notified_ids");
			Preferences.Default.Remove("jobs.accepted_notified_ids");
			Preferences.Default.Remove("jobs.completed_notified_ids");
			Preferences.Default.Remove("badges.jobboard_cleared_at");
			Preferences.Default.Remove("badges.myjobs_cleared_at");
			Preferences.Default.Remove("gps.last_reading_utc");
			settingsViewModel.settings.IsAdmin = false;
			settingsViewModel.settings.IsPunchedIn = false;
			settingsViewModel.settings.HasLastKnownPosition = false;
			settingsViewModel.settings.LastGpsReadingTime = null;
			settingsViewModel.settings.PunchInTime = null;
#if ANDROID
			PersistentForegroundService.Stop(Android.App.Application.Context);
#endif
			await settingsViewModel.Navigation.NavigateAsync("/LoginPage");
		}, (IObservable<bool>?)null, (IScheduler?)null);

	}

	public override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			string userId = supabase.Auth.CurrentUser?.Id;
			if (!string.IsNullOrEmpty(userId))
			{
				var profile = await supabase.From<ProfileRecord>()
					.Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId)
					.Single();
				if (profile != null)
				{
					IsAdmin = profile.IsAdmin;
					// Prefer the profile table display_name (admin can update it)
					if (!string.IsNullOrWhiteSpace(profile.DisplayName))
						DisplayName = profile.DisplayName;
				}
			}
		}
		catch { }
		// Fallback to auth metadata if profile has no name set
		if (string.IsNullOrWhiteSpace(DisplayName))
		{
			var metadata = supabase.Auth.CurrentUser?.UserMetadata;
			if (metadata != null && metadata.TryGetValue("display_name", out var value))
				DisplayName = value?.ToString() ?? string.Empty;
		}
	}
}
