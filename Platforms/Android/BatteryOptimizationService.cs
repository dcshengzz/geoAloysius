using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using Application = Android.App.Application;
using Uri = Android.Net.Uri;

namespace GpsSync;

public class BatteryOptimizationService : IBatteryOptimizationService
{
	public void RequestExemption()
	{
		try
		{
			Context context = Application.Context;
			PowerManager powerManager = (PowerManager)context.GetSystemService("power");
			if (!powerManager.IsIgnoringBatteryOptimizations(AppInfo.PackageName))
			{
				Intent intent = new Intent("android.settings.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS");
				intent.SetData(Uri.Parse("package:" + AppInfo.PackageName));
				intent.AddFlags(ActivityFlags.NewTask);
				context.StartActivity(intent);
			}
		}
		catch
		{
		}
	}

}
