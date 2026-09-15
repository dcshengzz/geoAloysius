using System;
using System.Reactive.Linq;
using Microsoft.Maui.Storage;
using Shiny;
using Supabase;
using Supabase.Gotrue;

namespace GpsSync;

public class SupabaseTokenRefreshService : IShinyStartupTask
{
	private readonly Supabase.Client supabase;

	private IDisposable? refreshTimer;

	public SupabaseTokenRefreshService(Supabase.Client supabase)
	{
		this.supabase = supabase;
	}

	public void Start()
	{
		refreshTimer = Observable.Interval(TimeSpan.FromMinutes(45.0)).SubscribeAsync(async delegate
		{
			if (supabase.Auth.CurrentSession == null)
			{
				return;
			}
			try
			{
				Session refreshed = await supabase.Auth.RefreshSession();
				if (refreshed?.AccessToken != null)
				{
					await SecureStorage.Default.SetAsync("sb.access_token", refreshed.AccessToken);
					if (refreshed.RefreshToken != null)
					{
						await SecureStorage.Default.SetAsync("sb.refresh_token", refreshed.RefreshToken);
					}
				}
			}
			catch
			{
			}
		});
	}
}
