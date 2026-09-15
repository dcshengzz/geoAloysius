using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;
using Shiny.Locations;
using Shiny.Notifications;

namespace GpsSync;

public class MainViewModel : ViewModel
{
	private readonly AppSettings settings;

	private readonly IGpsManager gpsManager;

	private readonly INotificationManager notifications;

	private readonly IBackendClient backend;

	private readonly MySqliteConnection data;

	private readonly IBatteryOptimizationService batteryOptimization;

	private IDisposable? gpsSubscription;

	private IDisposable? elapsedTimer;

	private IDisposable? badgeTimer;

	private IDisposable? signalTimer;
	private IDisposable? settingsGpsSubscription;
	private EventHandler<Microsoft.Maui.Networking.ConnectivityChangedEventArgs>? _connectivityHandler;
	private DateTimeOffset? _listenerStartedAt;

	// Prevent multiple simultaneous displacement alerts
	private bool _displacementAlertShown;

	[Reactive]
	public bool IsPunchedIn { get; set; }

	[Reactive]
	public bool IsAdmin { get; set; }

	[Reactive]
	public double CurrentLatitude { get; set; }

	[Reactive]
	public double CurrentLongitude { get; set; }

	[Reactive]
	public bool HasLocation { get; set; }

	[Reactive]
	public DateTime? LastSyncTime { get; set; }

	[Reactive]
	public string? ElapsedTime { get; set; }

	[Reactive]
	public bool GpsSignalActive { get; set; }

	// True when GPS hardware is throttled (stationary) but fix is held and internet is up
	[Reactive]
	public bool GpsSignalTracking { get; set; }

	// "active" | "tracking" | "lost" | "stopped" | "acquiring"
	[Reactive]
	public string GpsSignalState { get; set; } = "stopped";

	[Reactive]
	public bool IsAcquiringFix { get; set; }

	[Reactive]
	public bool ShowSignalLost { get; set; }

	[Reactive]
	public bool GpsActiveNoInternet { get; set; }

	[Reactive]
	public bool WarnNoInternet { get; set; }

	[Reactive]
	public bool WarnBatterySaver { get; set; }

	[Reactive]
	public bool WarnLocationPermission { get; set; }

	[Reactive]
	public string LastPingDisplay { get; set; } = string.Empty;

	private bool _locationAlwaysGranted = true;


	[Reactive]
	public string? LastJobRun { get; set; }

	[Reactive]
	public int MyJobsCount { get; set; }

	[Reactive]
	public int JobBoardCount { get; set; }

	[Reactive]
	public new bool IsBusy { get; set; }

	public ICommand Refresh { get; }

	public ICommand PunchIn { get; }

	public ICommand PunchOut { get; }

	public MainViewModel(BaseServices services, AppSettings settings, IGpsManager gpsManager, INotificationManager notifications, IBackendClient backend, MySqliteConnection data, IBatteryOptimizationService batteryOptimization)
		: base(services)
	{
		MainViewModel mainViewModel = this;
		this.settings = settings;
		this.gpsManager = gpsManager;
		this.notifications = notifications;
		this.backend = backend;
		this.data = data;
		this.batteryOptimization = batteryOptimization;
		IsPunchedIn = settings.IsPunchedIn;
		Refresh = ReactiveCommand.CreateFromTask(async () =>
		{
			IsBusy = true;
			try
			{
				// Restart GPS listener if it stopped while punched in
				if (IsPunchedIn && gpsManager.CurrentListener == null)
				{
					_listenerStartedAt = DateTimeOffset.UtcNow;
					await gpsManager.StartListener(GpsRequest.Realtime(background: true));
				}
				// Restore persisted GPS reading time
				string storedTime = Preferences.Default.Get("gps.last_reading_utc", string.Empty);
				if (!string.IsNullOrEmpty(storedTime) && DateTimeOffset.TryParse(storedTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var persistedTime))
					settings.LastGpsReadingTime = persistedTime;
				// Try to get the latest GPS reading directly from the manager first (instant)
				var liveReading = await gpsManager.GetLastReading();
				if (liveReading != null)
				{
					CurrentLatitude = liveReading.Position.Latitude;
					CurrentLongitude = liveReading.Position.Longitude;
					HasLocation = true;
					settings.LastGpsReadingTime = liveReading.Timestamp;
					Preferences.Default.Set("gps.last_reading_utc", liveReading.Timestamp.ToString("O"));
				}
				else
				{
					// Fall back to latest coords from local DB
					var latest = await data.GpsPings.OrderByDescending(p => p.Timestamp).FirstOrDefaultAsync();
					if (latest != null)
					{
						CurrentLatitude = latest.Latitude;
						CurrentLongitude = latest.Longitude;
						HasLocation = true;
					}
				}
				UpdateGpsSignalState();
				await RefreshBadges();
			}
			finally { IsBusy = false; }
		});
		PunchIn = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (await mainViewModel.Dialogs.Confirm("Start GPS tracking and punch in?", "Punch In"))
			{
				AccessState access = await gpsManager.RequestAccess(GpsRequest.Realtime(background: true));
				if (access != AccessState.Available)
				{
					await mainViewModel.Dialogs.Alert("Insufficient GPS Permissions - " + access);
				}
				else
				{
					access = await notifications.RequestAccess();
					if (access != AccessState.Available)
					{
						await mainViewModel.Dialogs.Alert("Insufficient Notification Permissions - " + access);
					}
					else
					{
						mainViewModel.batteryOptimization.RequestExemption();
						await gpsManager.StopListener();
						_listenerStartedAt = DateTimeOffset.UtcNow;
						await gpsManager.StartListener(GpsRequest.Realtime(background: true));
						settings.IsPunchedIn = true;
						settings.PunchInTime = DateTimeOffset.UtcNow;
						mainViewModel.IsPunchedIn = true;
						mainViewModel.StartElapsedTimer();
						await mainViewModel.SyncPunchStatus(isPunchedIn: true);
					}
				}
			}
		}, this.WhenAny((MainViewModel x) => x.IsPunchedIn, (IObservedChange<MainViewModel, bool> x) => !x.GetValue()), (IScheduler?)null);
		PunchOut = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			// Block punch-out if a job is still In Progress
			try
			{
				string? uid = backend.CurrentUserId;
				if (!string.IsNullOrEmpty(uid))
				{
					var inProgress = await backend.GetJobsAsync(engineerUserId: uid, status: "In Progress");
					if (inProgress.Count > 0)
					{
						await mainViewModel.Dialogs.Alert("You have a job in progress. Please complete it before punching out.", "Cannot Punch Out");
						return;
					}
				}
			}
			catch { }
			if (await mainViewModel.Dialogs.Confirm("Stop GPS tracking and punch out?", "Punch Out"))
			{
				settings.IsPunchedIn = false;
				settings.PunchInTime = null;
				mainViewModel.IsPunchedIn = false;
				mainViewModel.ElapsedTime = null;
				await gpsManager.StopListener();
				Preferences.Default.Remove("gps.has_fix");
				mainViewModel.StopElapsedTimer();
				mainViewModel.UpdateGpsSignalState();
				await mainViewModel.SyncPunchStatus(isPunchedIn: false);
			}
		}, this.WhenAny((MainViewModel x) => x.IsPunchedIn, (IObservedChange<MainViewModel, bool> x) => x.GetValue()), (IScheduler?)null);
	}

	public override async void OnAppearing()
	{
		base.OnAppearing();
		_displacementAlertShown = false;
		try
		{
			string? userId = backend.CurrentUserId;
			if (!string.IsNullOrEmpty(userId))
			{
				var profile = await backend.GetProfileAsync(userId);
				settings.IsAdmin = profile?.IsAdmin ?? false;
				Preferences.Default.Set("user.display_name", profile?.DisplayName ?? string.Empty);
			}
		}
		catch
		{
		}
		IsAdmin = settings.IsAdmin;
		await RefreshBadges();
		badgeTimer?.Dispose();
		badgeTimer = Observable.Interval(TimeSpan.FromSeconds(30.0)).ObserveOn(RxApp.MainThreadScheduler).Subscribe(async delegate
		{
			await RefreshBadges();
			await CheckDeviceSession();
			await CheckLocationPermission();
		});
		if (settings.HasLastKnownPosition)
		{
			CurrentLatitude = settings.LastKnownLatitude;
			CurrentLongitude = settings.LastKnownLongitude;
			HasLocation = true;
		}
		gpsSubscription = gpsManager.WhenReading().ObserveOn(RxApp.MainThreadScheduler).Subscribe(delegate(GpsReading reading)
		{
			// Update coordinates instantly
			CurrentLatitude = reading.Position.Latitude;
			CurrentLongitude = reading.Position.Longitude;
			LastSyncTime = DateTime.Now;
			HasLocation = true;
			Preferences.Default.Set("gps.has_fix", true);
			// Stamp the reading time immediately — UpdateGpsSignalState() is the sole authority
			// on GpsSignalActive; never set it directly here to avoid bypassing the internet check
			settings.LastGpsReadingTime = DateTimeOffset.UtcNow;
			Preferences.Default.Set("gps.last_reading_utc", DateTimeOffset.UtcNow.ToString("O"));
			UpdateGpsSignalState();
		});

		// Last Ping updates only when data is confirmed sent to the server (admin map update)
		settings.WhenAnyValue(x => x.LastSyncedToServerTime)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(t => { if (t.HasValue) { LastSyncTime = t.Value; UpdateGpsSignalState(); } });

		// Restore persisted GPS reading time (survives app restarts)
		string storedGpsTime = Preferences.Default.Get("gps.last_reading_utc", string.Empty);
		if (!string.IsNullOrEmpty(storedGpsTime) && DateTimeOffset.TryParse(storedGpsTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var restoredTime))
			settings.LastGpsReadingTime = restoredTime;

		// Restore last server sync time so Last Ping shows immediately on launch
		string storedSyncTime = Preferences.Default.Get("gps.last_synced_utc", string.Empty);
		if (!string.IsNullOrEmpty(storedSyncTime) && DateTime.TryParse(storedSyncTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var restoredSyncTime))
			LastSyncTime = restoredSyncTime;

		// Restore HasLocation from persisted fix flag — survives app resume when GPS is throttled
		if (IsPunchedIn && Preferences.Default.Get("gps.has_fix", false))
			HasLocation = true;

		// Auto-restart GPS listener if punched in but listener stopped
		if (IsPunchedIn && gpsManager.CurrentListener == null)
		{
			_listenerStartedAt = DateTimeOffset.UtcNow;
			try { await gpsManager.StartListener(GpsRequest.Realtime(background: true)); } catch { }
		}
		else if (IsPunchedIn && _listenerStartedAt == null)
		{
			_listenerStartedAt = DateTimeOffset.UtcNow;
		}

		// React instantly when a GPS reading updates LastGpsReadingTime
		settingsGpsSubscription?.Dispose();
		settingsGpsSubscription = settings.WhenAnyValue(x => x.LastGpsReadingTime)
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => UpdateGpsSignalState());

		// Re-evaluate signal every 5 s so UI stays current
		signalTimer?.Dispose();
		signalTimer = Observable.Interval(TimeSpan.FromSeconds(5.0))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => UpdateGpsSignalState());

		// Subscribe to connectivity changes for instant status updates when network toggles
		_connectivityHandler = (_, args) =>
		{
			void Run() => UpdateGpsSignalState(args.NetworkAccess);
			if (MainThread.IsMainThread) Run();
			else MainThread.BeginInvokeOnMainThread(Run);
			// Follow-up: re-read true state after Android may fire a second transitional event
			Task.Delay(300).ContinueWith(_ => {
				if (MainThread.IsMainThread) UpdateGpsSignalState();
				else MainThread.BeginInvokeOnMainThread(() => UpdateGpsSignalState());
			});
		};
		Microsoft.Maui.Networking.Connectivity.Current.ConnectivityChanged += _connectivityHandler;

		UpdateGpsSignalState();
		await CheckLocationPermission();
		if (IsPunchedIn && settings.PunchInTime.HasValue)
		{
			StartElapsedTimer();
		}
		JobRun lastJob = await data.JobRuns.OrderByDescending((JobRun x) => x.Timestamp).FirstOrDefaultAsync();
		if (lastJob != null)
		{
			LastJobRun = lastJob.Timestamp.LocalDateTime.ToString("MMM d, h:mm tt");
		}
	}

	public override void OnDisappearing()
	{
		base.OnDisappearing();
		gpsSubscription?.Dispose();
		gpsSubscription = null;
		StopElapsedTimer();
		badgeTimer?.Dispose();
		badgeTimer = null;
		signalTimer?.Dispose();
		signalTimer = null;
		settingsGpsSubscription?.Dispose();
		settingsGpsSubscription = null;
		if (_connectivityHandler != null)
		{
			Microsoft.Maui.Networking.Connectivity.Current.ConnectivityChanged -= _connectivityHandler;
			_connectivityHandler = null;
		}
	}

	private async Task CheckLocationPermission()
	{
		try
		{
			var status = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<Microsoft.Maui.ApplicationModel.Permissions.LocationAlways>();
			_locationAlwaysGranted = status == Microsoft.Maui.ApplicationModel.PermissionStatus.Granted;
		}
		catch
		{
			_locationAlwaysGranted = true;
		}
	}

	private void UpdateGpsSignalState(Microsoft.Maui.Networking.NetworkAccess? knownAccess = null)
	{
		if (!IsPunchedIn)
		{
			GpsSignalActive = false;
			GpsSignalTracking = false;
			GpsSignalState = "stopped";
			IsAcquiringFix = false;
			ShowSignalLost = false;
			WarnNoInternet = false;
			WarnBatterySaver = false;
			WarnLocationPermission = false;
			LastPingDisplay = string.Empty;
			return;
		}

		var netAccess = knownAccess ?? Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess;
		bool hasInternet = netAccess == Microsoft.Maui.Networking.NetworkAccess.Internet;
		WarnNoInternet = !hasInternet;

		try { WarnBatterySaver = Microsoft.Maui.Devices.Battery.Default.EnergySaverStatus == Microsoft.Maui.Devices.EnergySaverStatus.On; }
		catch { WarnBatterySaver = false; }

		WarnLocationPermission = !_locationAlwaysGranted;

		// GPS reading timestamps from Preferences (updated by WhenReading on every hardware fix)
		DateTimeOffset? lastReading = null;
		string storedReading = Preferences.Default.Get("gps.last_reading_utc", string.Empty);
		if (!string.IsNullOrEmpty(storedReading) &&
		    DateTimeOffset.TryParse(storedReading, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedReadingTime))
			lastReading = parsedReadingTime;
		// 45s: GPS hardware actively firing
		bool readingFresh = lastReading.HasValue && (DateTimeOffset.UtcNow - lastReading.Value) < TimeSpan.FromSeconds(45.0);
		// 5 min: GPS throttled but fix is still valid (stationary)
		bool readingRecent = lastReading.HasValue && (DateTimeOffset.UtcNow - lastReading.Value) < TimeSpan.FromSeconds(600.0);

		// Grace period: show Acquiring in first 60s after punch-in before any GPS fix is received
		if (!HasLocation && !readingFresh && _listenerStartedAt.HasValue &&
		    (DateTimeOffset.UtcNow - _listenerStartedAt.Value) < TimeSpan.FromSeconds(60))
		{
			GpsSignalActive = false;
			GpsSignalTracking = false;
			GpsActiveNoInternet = false;
			GpsSignalState = "acquiring";
			IsAcquiringFix = true;
		}
		else if (readingFresh)
		{
			// GPS hardware actively firing — active regardless of internet
			GpsSignalActive = true;
			GpsSignalTracking = false;
			GpsActiveNoInternet = !hasInternet;
			GpsSignalState = "active";
			IsAcquiringFix = false;
		}
		else if (HasLocation || readingRecent)
		{
			// GPS throttled (stationary) but fix held — tracking regardless of internet
			GpsSignalActive = false;
			GpsSignalTracking = true;
			GpsActiveNoInternet = !hasInternet;
			GpsSignalState = "tracking";
			IsAcquiringFix = false;
		}
		else
		{
			// No fix and reading too stale — genuine signal lost
			GpsSignalActive = false;
			GpsSignalTracking = false;
			GpsActiveNoInternet = false;
			GpsSignalState = "lost";
			IsAcquiringFix = false;
		}
		ShowSignalLost = IsPunchedIn && !GpsSignalActive && !GpsSignalTracking && !IsAcquiringFix;

		// Sync LastSyncTime from Preferences — MyGpsDelegate may be in a different DI scope
		string storedSync = Preferences.Default.Get("gps.last_synced_utc", string.Empty);
		if (!string.IsNullOrEmpty(storedSync) &&
		    DateTime.TryParse(storedSync, null, System.Globalization.DateTimeStyles.RoundtripKind, out var syncedTime) &&
		    (!LastSyncTime.HasValue || syncedTime > LastSyncTime.Value))
			LastSyncTime = syncedTime;

		// Show relative time when active; freeze at exact timestamp when signal is lost
		if (LastSyncTime.HasValue)
		{
			if (!GpsSignalActive && !GpsSignalTracking)
				LastPingDisplay = $"Last synced: {LastSyncTime.Value:HH:mm:ss}";
			else
			{
				var ago = DateTime.Now - LastSyncTime.Value;
				if (ago.TotalSeconds < 30)
					LastPingDisplay = "Last synced: just now";
				else if (ago.TotalMinutes < 1)
					LastPingDisplay = $"Last synced: {(int)ago.TotalSeconds}s ago";
				else if (ago.TotalMinutes < 60)
					LastPingDisplay = $"Last synced: {(int)ago.TotalMinutes} min ago";
				else
					LastPingDisplay = $"Last synced: {LastSyncTime.Value:HH:mm}";
			}
		}
	}

	private async Task SyncPunchStatus(bool isPunchedIn)
	{
		try
		{
			string? userId = backend.CurrentUserId;
			if (!string.IsNullOrEmpty(userId))
			{
				await backend.UpdateProfileAsync(userId, isPunchedIn: isPunchedIn);
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// Checks every 30 s whether the server's active_device_token still matches the token
	/// stored on this device. If another device has logged in and displaced this one, sign
	/// out immediately and inform the user.
	/// </summary>
	private async Task CheckDeviceSession()
	{
		if (_displacementAlertShown) return;
		try
		{
			string? userId = backend.CurrentUserId;
			if (string.IsNullOrEmpty(userId)) return;

			var profile = await backend.GetProfileAsync(userId);
			if (profile == null || profile.IsAdmin) return;

			// No server token means the account has been fully signed out elsewhere — also kick
			string? storedToken = await SecureStorage.Default.GetAsync("device.session_token");
			if (string.IsNullOrEmpty(profile.ActiveDeviceToken) || storedToken != profile.ActiveDeviceToken)
			{
				_displacementAlertShown = true;

				// Stop GPS if still running
				if (IsPunchedIn)
				{
					try { await gpsManager.StopListener(); } catch { }
					settings.IsPunchedIn = false;
					IsPunchedIn = false;
				}

				// Clear all local auth state
				try { await backend.LogoutAsync(); } catch { }
				SecureStorage.Default.Remove("device.session_token");
				Preferences.Default.Remove("keep_logged_in");
				Preferences.Default.Remove("jobs.notified_ids");
				Preferences.Default.Remove("jobs.accepted_notified_ids");
				Preferences.Default.Remove("jobs.completed_notified_ids");
				Preferences.Default.Remove("badges.jobboard_cleared_at");
				Preferences.Default.Remove("badges.myjobs_cleared_at");
				Preferences.Default.Remove("gps.last_reading_utc");
				settings.IsAdmin = false;
				settings.IsPunchedIn = false;
				settings.HasLastKnownPosition = false;
				settings.LastGpsReadingTime = null;
				settings.PunchInTime = null;

				await Dialogs.Alert(
					"Your account has been signed in on another device. You have been signed out from this device.",
					"Signed Out");

				await Navigation.NavigateAsync("/LoginPage?displaced=true");
			}
		}
		catch { }
	}

	private void StartElapsedTimer()
	{
		StopElapsedTimer();
		elapsedTimer = Observable.Interval(TimeSpan.FromSeconds(1.0)).StartWith(default(long)).ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(delegate
			{
				DateTimeOffset? punchInTime = settings.PunchInTime;
				if (punchInTime.HasValue)
				{
					TimeSpan timeSpan = DateTimeOffset.UtcNow - punchInTime.Value;
					ElapsedTime = $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
				}
			});
	}

	private void StopElapsedTimer()
	{
		elapsedTimer?.Dispose();
		elapsedTimer = null;
	}

	private async Task RefreshBadges()
	{
		try
		{
			string? userId = backend.CurrentUserId;
			if (string.IsNullOrEmpty(userId))
			{
				return;
			}
			if (IsAdmin)
			{
				DateTime.TryParse(Preferences.Default.Get("badges.jobboard_cleared_at", string.Empty), null, DateTimeStyles.RoundtripKind, out var clearedAt);
				var active = (await backend.GetJobsAsync()).Where(j => j.Status == "Pending" || j.Status == "In Progress");
				JobBoardCount = ((clearedAt == default(DateTime)) ? active.Count() : active.Count(j => DateTime.SpecifyKind(j.CreatedAt, DateTimeKind.Utc) > clearedAt));
			}
			else
			{
				DateTime.TryParse(Preferences.Default.Get("badges.myjobs_cleared_at", string.Empty), null, DateTimeStyles.RoundtripKind, out var clearedAt2);
				var mine = await backend.GetJobsAsync(engineerUserId: userId, status: "Pending");
				MyJobsCount = ((clearedAt2 == default(DateTime)) ? mine.Count : mine.Count(j => DateTime.SpecifyKind(j.CreatedAt, DateTimeKind.Utc) > clearedAt2));
			}
		}
		catch
		{
		}
	}
}
