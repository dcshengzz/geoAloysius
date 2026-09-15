using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Shiny;
using Shiny.Notifications;
using Notification = Shiny.Notifications.Notification;

namespace GpsSync;

public class GpsWatchdogService : IShinyStartupTask
{
	private readonly AppSettings settings;

	private readonly INotificationManager notifications;

	private readonly MySqliteConnection conn;

	private readonly IBackendClient backend;

	private bool alertSent;

	private IDisposable? forcePingTimer;

	private IDisposable? staleAlertTimer;

	public GpsWatchdogService(AppSettings settings, INotificationManager notifications, MySqliteConnection conn, IBackendClient backend)
	{
		this.settings = settings;
		this.notifications = notifications;
		this.conn = conn;
		this.backend = backend;
	}

	public void Start()
	{
		forcePingTimer = Observable.Interval(TimeSpan.FromSeconds(10.0)).SubscribeAsync(async delegate
		{
			if (settings.IsPunchedIn && settings.HasLastKnownPosition)
			{
				DateTimeOffset? lastReal = settings.LastGpsReadingTime;
				// Only gap-fill if last real GPS was within the last 5 minutes.
				bool recentRealReading = lastReal.HasValue && (DateTimeOffset.UtcNow - lastReal.Value) < TimeSpan.FromMinutes(5.0);
				bool needsGapFill = lastReal.HasValue && (DateTimeOffset.UtcNow - lastReal.Value) > TimeSpan.FromSeconds(15.0);
				if (recentRealReading && needsGapFill)
				{
					await ForcePingAsync(settings.LastKnownLatitude, settings.LastKnownLongitude);
				}
			}
		});
		staleAlertTimer = Observable.Interval(TimeSpan.FromMinutes(2.0)).SubscribeAsync(async delegate
		{
			if (!settings.IsPunchedIn)
			{
				alertSent = false;
			}
			else
			{
				DateTimeOffset? last = settings.LastGpsReadingTime;
				DateTimeOffset? punchInTime = settings.PunchInTime;
				bool hadReadingThisSession = last.HasValue && punchInTime.HasValue && last.Value >= punchInTime.Value;
				if (!hadReadingThisSession || DateTimeOffset.UtcNow - last.Value <= TimeSpan.FromMinutes(5.0))
				{
					alertSent = false;
				}
				else if (!alertSent)
				{
					alertSent = true;
					await notifications.Send(new Notification
					{
						Id = 9001,
						Title = "GPS Signal Lost",
						Message = "No GPS reading received in the last 5 minutes while punched in."
					});
				}
			}
		});
	}

	private async Task ForcePingAsync(double latitude, double longitude)
	{
		try
		{
			GpsPing ping = new GpsPing
			{
				Latitude = latitude,
				Longitude = longitude,
				Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
				Synced = false
			};
			await conn.InsertAsync(ping);

			await backend.EnsureSessionLoadedAsync();
			if (backend.IsSignedIn)
			{
				try
				{
					await backend.PostPingAsync(latitude, longitude, Preferences.Default.Get("user.display_name", string.Empty));
					ping.Synced = true;
					await conn.UpdateAsync(ping);
					Preferences.Default.Set("gps.last_reading_utc", DateTimeOffset.UtcNow.ToString("O"));
					Preferences.Default.Set("gps.last_synced_utc", DateTimeOffset.UtcNow.ToString("O"));
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}
}
