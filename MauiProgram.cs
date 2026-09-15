using System.IO;
using CommunityToolkit.Maui;
using GpsSync.Delegates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using Prism;
using Prism.Container.DryIoc;
using Prism.Ioc;
using Prism.Navigation;
using Shiny;
using Shiny.Jobs;
using SkiaSharp.Views.Maui.Controls;
using SkiaSharp.Views.Maui.Handlers;
using Supabase;

namespace GpsSync;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		return MauiApp.CreateBuilder().UseMauiApp<App>().UseMauiCommunityToolkit()
			.ConfigureMauiHandlers(delegate(IMauiHandlersCollection handlers)
			{
				handlers.AddHandler<SKCanvasView, SKCanvasViewHandler>();
			})
			.ConfigureMauiHandlers(_ =>
			{
#if ANDROID
				// Force Samsung One UI (and other OEM skins) to render the full pill track on Switch.
				// Without this, Samsung ignores the MAUI track tint and shows only the bare thumb circle.
				Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("SwitchTrackFix", (handler, _) =>
				{
					if (handler.PlatformView is AndroidX.AppCompat.Widget.SwitchCompat sw)
					{
						var states = new int[][]
						{
							new int[] {  Android.Resource.Attribute.StateChecked },
							new int[] { -Android.Resource.Attribute.StateChecked }
						};
						var colors = new int[]
						{
							Android.Graphics.Color.ParseColor("#3B82F6"), // on  - accent blue
							Android.Graphics.Color.ParseColor("#9CA3AF")  // off - neutral grey
						};
						sw.TrackTintList = new Android.Content.Res.ColorStateList(states, colors);
						sw.TrackTintMode = Android.Graphics.PorterDuff.Mode.SrcIn!;
					}
				});
#endif
			})
			.UseShinyFramework(new DryIocContainerExtension(), delegate(PrismAppBuilder prism)
			{
				prism.OnAppStart(async delegate(IContainerProvider container, INavigationService nav)
				{
					await nav.NavigateAsync("SplashPage");
				});
			}, new GlobalExceptionHandlerConfig(ErrorAlertType.FullError))
			.ConfigureFonts(delegate(IFontCollection fonts)
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.RegisterInfrastructure()
			.RegisterViews()
			.Build();
	}

	private static MauiAppBuilder RegisterInfrastructure(this MauiAppBuilder builder)
	{
		builder.Logging.SetMinimumLevel(LogLevel.Debug);
#if DEBUG
		builder.Logging.AddDebug();
#endif
		IServiceCollection services = builder.Services;
		Client implementationInstance = new Client(AppConfig.SupabaseUrl, AppConfig.SupabaseKey, new SupabaseOptions
		{
			AutoConnectRealtime = false
		});
		services.AddSingleton(implementationInstance);
		services.AddSingleton<MySqliteConnection>();
		ServiceCollectionServiceExtensions.AddSingleton<IBatteryOptimizationService, BatteryOptimizationService>(services);
		services.AddShinyService<NetworkMonitorService>();
		services.AddShinyService<AppSettings>();
		services.AddShinyService<SupabaseTokenRefreshService>();
		services.AddShinyService<GpsWatchdogService>();
		services.AddShinyService<JobNotificationService>();
		services.AddJob(typeof(MyJob), null, InternetAccess.Any, true);
		services.AddGps<MyGpsDelegate>();
		services.AddNotifications();
		return builder;
	}

	private static MauiAppBuilder RegisterViews(this MauiAppBuilder builder)
	{
		IServiceCollection services = builder.Services;
		services.RegisterForNavigation<SplashPage, SplashViewModel>();
		services.RegisterForNavigation<LoginPage, LoginViewModel>();
		services.RegisterForNavigation<MainPage, MainViewModel>();
		services.RegisterForNavigation<JobLogPage, JobLogViewModel>();
		services.RegisterForNavigation<NetworkLogPage, NetworkLogViewModel>();
		services.RegisterForNavigation<GpsLogPage, GpsLogViewModel>();
		services.RegisterForNavigation<MapPage, MapViewModel>();
		services.RegisterForNavigation<JobsPage, JobsViewModel>();
		services.RegisterForNavigation<AdminJobsPage, AdminJobsViewModel>();
		services.RegisterForNavigation<SettingsPage, SettingsViewModel>();
		services.RegisterForNavigation<ResetPasswordPage, ResetPasswordViewModel>();
		services.RegisterForNavigation<ForgotPasswordPage, ForgotPasswordViewModel>();
		services.RegisterForNavigation<RegisterPage, RegisterViewModel>();
		services.RegisterForNavigation<EngineerStatusPage, EngineerStatusViewModel>();
return builder;
	}
}
