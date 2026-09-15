using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace GpsSync;

/// <summary>
/// Always-on foreground service that keeps the app process alive in the background.
/// Starts when the app launches; stops only when the user swipes the app from recents
/// or explicitly signs out.
/// </summary>
[Service(ForegroundServiceType = ForegroundService.TypeDataSync, Exported = false)]
public class PersistentForegroundService : Service
{
	public const int NotificationId = 8888;
	public const string ChannelId = "gpssync_persistent";
	public const string ActionStart = "gpssync.START_PERSISTENT";
	public const string ActionStop  = "gpssync.STOP_PERSISTENT";

	public override IBinder? OnBind(Intent? intent) => null;

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		if (intent?.Action == ActionStop)
		{
			StopForeground(StopForegroundFlags.Remove);
			StopSelf();
			return StartCommandResult.NotSticky;
		}

		EnsureChannel();

		var notification = new NotificationCompat.Builder(this, ChannelId)
			.SetContentTitle("HitachiDispatch")
			.SetContentText("Running in background")
			.SetSmallIcon(Resource.Drawable.notification)
			.SetOngoing(true)
			.SetPriority(NotificationCompat.PriorityLow)
			.SetVisibility(NotificationCompat.VisibilitySecret) // hide from lock screen
			.Build();

		StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);

		return StartCommandResult.Sticky;
	}

	/// <summary>Called when the user swipes the app from the recent apps list.</summary>
	public override void OnTaskRemoved(Intent? rootIntent)
	{
		StopForeground(StopForegroundFlags.Remove);
		StopSelf();
		base.OnTaskRemoved(rootIntent);
	}

	private void EnsureChannel()
	{
		var manager = (NotificationManager?)GetSystemService(NotificationService);
		if (manager?.GetNotificationChannel(ChannelId) != null) return;

		var channel = new NotificationChannel(ChannelId, "App Background Service", NotificationImportance.Low)
		{
			Description = "Keeps HitachiDispatch running in the background"
		};
		channel.SetShowBadge(false);
		manager?.CreateNotificationChannel(channel);
	}

	/// <summary>Start the service from any context.</summary>
	public static void Start(Context context)
	{
		var intent = new Intent(context, typeof(PersistentForegroundService));
		intent.SetAction(ActionStart);
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			context.StartForegroundService(intent);
		else
			context.StartService(intent);
	}

	/// <summary>Stop the service (call on sign-out).</summary>
	public static void Stop(Context context)
	{
		var intent = new Intent(context, typeof(PersistentForegroundService));
		intent.SetAction(ActionStop);
		context.StartService(intent);
	}
}
