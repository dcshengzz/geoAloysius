using System;
using System.Linq;
using System.Threading.Tasks;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Shiny;
using Shiny.Locations;

namespace GpsSync.Delegates;

public class MyGpsDelegate : GpsDelegate, IAndroidForegroundServiceDelegate
{
	private readonly ILogger logger;
	private readonly MySqliteConnection conn;
	private readonly IBackendClient backend;
	private readonly AppSettings settings;

	public MyGpsDelegate(ILogger<MyGpsDelegate> logger, MySqliteConnection conn, IBackendClient backend, AppSettings settings)
		: base(logger)
	{
		this.logger = logger;
		this.conn = conn;
		this.backend = backend;
		this.settings = settings;
		base.MinimumTime = TimeSpan.FromSeconds(5.0);   // was 10 s
		base.MinimumDistance = Distance.FromMeters(0.0);
		this.logger.LogInformation("MyGpsDelegate initialized – interval 5 s");
	}

	protected override async Task OnGpsReading(GpsReading reading)
	{
		try
		{
			logger.LogInformation($"GPS {reading.Position.Latitude:F6}/{reading.Position.Longitude:F6}");

			var ping = new GpsPing
			{
				Latitude  = reading.Position.Latitude,
				Longitude = reading.Position.Longitude,
				Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
				Synced    = false
			};
			await conn.InsertAsync(ping);

			// Update in-memory settings so the main page / watchdog see a fresh reading
			var now = DateTimeOffset.UtcNow;
			settings.LastGpsReadingTime     = now;
			settings.LastKnownLatitude      = reading.Position.Latitude;
			settings.LastKnownLongitude     = reading.Position.Longitude;
			settings.HasLastKnownPosition   = true;
			Preferences.Default.Set("gps.last_reading_utc", now.ToString("O"));

			await backend.EnsureSessionLoadedAsync();
			if (settings.IsPunchedIn && backend.IsSignedIn)
			{
				// Push current ping
				bool synced = false;
				try
				{
					await backend.PostPingAsync(reading.Position.Latitude, reading.Position.Longitude,
						Preferences.Default.Get("user.display_name", string.Empty));
					synced = true;
					ping.Synced = true;
					await conn.UpdateAsync(ping);
					settings.LastSyncedToServerTime = DateTime.Now;
					Preferences.Default.Set("gps.last_synced_utc", DateTime.Now.ToString("O"));
				}
				catch
				{
					logger.LogWarning("Failed to sync GPS ping to backend; queued for offline sync.");
				}

				// If we just pushed successfully, also drain up to 10 queued unsynced pings
				if (synced && settings.IsPunchedIn)
					await SyncOfflineQueue();
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error processing GPS reading");
		}
	}

	/// <summary>Retries unsynced local pings accumulated while offline.</summary>
	private async Task SyncOfflineQueue()
	{
		try
		{
			var pending = await conn.QueryAsync<GpsPing>(
				"SELECT * FROM GpsPing WHERE Synced = 0 ORDER BY Id ASC LIMIT 10");

			foreach (var p in pending)
			{
				try
				{
					await backend.PostPingAsync(p.Latitude, p.Longitude,
						Preferences.Default.Get("user.display_name", string.Empty));
					p.Synced = true;
					await conn.UpdateAsync(p);
				}
				catch
				{
					break; // Still offline – stop trying
				}
			}
		}
		catch { }
	}

	public void Configure(NotificationCompat.Builder builder)
	{
		builder.SetContentTitle("GPS Tracking Active");
		builder.SetContentText("Location is being tracked in the background");
		builder.SetOngoing(ongoing: true);
		builder.SetPriority(1);
		builder.SetForegroundServiceBehavior(1);
	}
}
