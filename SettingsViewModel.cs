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

namespace GpsSync;

public class SettingsViewModel : ViewModel
{
	private readonly AppSettings settings;
	private readonly IGpsManager gpsManager;
	private readonly IBackendClient backend;

	[Reactive] public bool IsNotificationsEnabled { get; set; }
	[Reactive] public bool IsDarkMode { get; set; }
	[Reactive] public bool NotifyPunchInOut { get; set; }
	[Reactive] public bool NotifyJobStatus { get; set; }
	[Reactive] public string DisplayName { get; set; } = string.Empty;
	[Reactive] public bool IsAdmin { get; private set; }


	public ICommand SaveDisplayName { get; }
	public ICommand Logout { get; }

	public SettingsViewModel(BaseServices services, AppSettings settings, IGpsManager gpsManager, IBackendClient backend)
		: base(services)
	{
		SettingsViewModel settingsViewModel = this;
		this.settings = settings;
		this.gpsManager = gpsManager;
		this.backend = backend;

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
					// Server updates the profile's display_name.
					await settingsViewModel.backend.UpdateDisplayNameAsync(settingsViewModel.DisplayName.Trim());
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
			// Clear single-device token on the server BEFORE dropping local auth tokens.
			try
			{
				string? userId = settingsViewModel.backend.CurrentUserId;
				if (!string.IsNullOrEmpty(userId))
					await settingsViewModel.backend.UpdateProfileAsync(userId, clearActiveDeviceToken: true);
			}
			catch { }
			try { await settingsViewModel.backend.LogoutAsync(); } catch { }
			SecureStorage.Default.Remove("device.session_token");
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
			string? userId = backend.CurrentUserId;
			if (!string.IsNullOrEmpty(userId))
			{
				var profile = await backend.GetProfileAsync(userId);
				if (profile != null)
				{
					IsAdmin = profile.IsAdmin;
					if (!string.IsNullOrWhiteSpace(profile.DisplayName))
						DisplayName = profile.DisplayName;
				}
			}
		}
		catch { }
	}
}
