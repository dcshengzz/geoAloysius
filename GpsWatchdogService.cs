using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Shiny;
using Shiny.Notifications;
using Notification = Shiny.Notifications.Notification;
using Supabase;

namespace GpsSync;

public class GpsWatchdogService : IShinyStartupTask
{
	private readonly AppSettings settings;

	private readonly INotificationManager notifications;

	private readonly MySqliteConnection conn;

	private readonly Client supabase;

	private bool alertSent;

	private IDisposable? forcePingTimer;

	private IDisposable? staleAlertTimer;

	public GpsWatchdogService(AppSettings settings, INotificationManager notifications, MySqliteConnection conn, Client supabase)
	{
		this.settings = settings;
		this.notifications = notifications;
		this.conn = conn;
		this.supabase = supabase;
	}

	public void Start()
	{
		forcePingTimer = Observable.Interval(TimeSpan.FromSeconds(10.0)).SubscribeAsync(async delegate
		{
			if (settings.IsPunchedIn && settings.HasLastKnownPosition)
			{
				DateTimeOffset? lastReal = settings.LastGpsReadingTime;
				// Only gap-fill if last real GPS was within the last 5 minutes.
				// If GPS has been off longer than that, stop sending stale location
				// so the live map correctly reflects the signal loss.
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
				// Only alert if GPS received a reading during this punch-in session but has since gone stale.
				// Without this check, a stale reading from a previous session would trigger the alert immediately.
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
			if (supabase.Auth.CurrentSession != null)
			{
				try
				{
					GpsPingRecord record = new GpsPingRecord
					{
						UserId      = supabase.Auth.CurrentUser?.Id,
						DisplayName = Preferences.Default.Get("user.display_name", string.Empty),
						Latitude    = latitude,
						Longitude   = longitude,
						CreatedAt   = DateTimeOffset.UtcNow
					};
					await supabase.From<GpsPingRecord>().Insert(record);
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
