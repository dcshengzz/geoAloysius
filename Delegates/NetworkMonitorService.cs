using System;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Shiny;
using Shiny.Net;
using Shiny.Notifications;
using IConnectivity = Shiny.Net.IConnectivity;

namespace GpsSync.Delegates;

public class NetworkMonitorService : IShinyStartupTask
{
	private readonly MySqliteConnection data;

	private readonly IConnectivity conn;

	private readonly INotificationManager notifications;

	private readonly ILogger logger;

	private readonly AppSettings settings;

	public NetworkMonitorService(IConnectivity conn, INotificationManager notifications, MySqliteConnection data, ILogger<NetworkMonitorService> logger, AppSettings settings)
	{
		this.conn = conn;
		this.notifications = notifications;
		this.logger = logger;
		this.settings = settings;
		this.data = data;
	}

	public void Start()
	{
		conn.WhenInternetStatusChanged().Skip(1).DistinctUntilChanged()
			.SubscribeAsync(async delegate(bool connected)
			{
				logger.LogInformation("Connected: " + connected);
				if (settings.IsPunchedIn)
				{
					await data.InsertAsync(new NetworkEvent
					{
						HasInternet = conn.IsInternetAvailable(),
						ConnectionTypes = conn.ConnectionTypes.ToString(),
						Timestamp = DateTimeOffset.UtcNow
					});
					if (settings.IsNotificationsEnabled)
					{
						await notifications.Send(new Shiny.Notifications.Notification
					{
						Id = 5001,
						Title = connected ? "Online" : "Offline",
						Message = connected ? "GPS sync connection restored" : "No internet, location not syncing to server"
					});
					}
				}
			}, delegate(Exception ex)
			{
				logger.LogError(ex, "Error with online restore");
			});
	}
}
