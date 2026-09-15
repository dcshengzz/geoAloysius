using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Shiny.Jobs;
using Shiny.Notifications;
using Notification = Shiny.Notifications.Notification;
using Supabase;
using Supabase.Postgrest;

namespace GpsSync.Delegates;

public class MyJob : Job
{
	private readonly MySqliteConnection conn;

	private readonly AppSettings settings;

	private readonly Supabase.Client supabase;

	private readonly INotificationManager notifications;

	public MyJob(ILogger<MyJob> logger, AppSettings settings, MySqliteConnection conn, Supabase.Client supabase, INotificationManager notifications)
		: base(logger)
	{
		base.MinimumTime = TimeSpan.FromMinutes(10.0);
		this.settings = settings;
		this.conn = conn;
		this.supabase = supabase;
		this.notifications = notifications;
	}

	protected override async Task Run(CancellationToken cancelToken)
	{
		await conn.InsertAsync(new JobRun
		{
			Timestamp = DateTimeOffset.UtcNow,
			IsPunchedIn = settings.IsPunchedIn
		});
		await SyncOfflinePings(cancelToken);
		await CheckForNewJobs(cancelToken);
	}

	private async Task SyncOfflinePings(CancellationToken cancelToken)
	{
		if (supabase.Auth.CurrentSession == null)
		{
			return;
		}
		string userId = supabase.Auth.CurrentUser?.Id;
		if (string.IsNullOrEmpty(userId))
		{
			return;
		}
		foreach (GpsPing ping in await conn.GpsPings.Where((GpsPing p) => !p.Synced).ToListAsync())
		{
			if (cancelToken.IsCancellationRequested)
			{
				break;
			}
			if (await RetryWithBackoff(async delegate
			{
				await supabase.From<GpsPingRecord>().Insert(new GpsPingRecord
				{
					UserId      = userId,
					DisplayName = Preferences.Default.Get("user.display_name", string.Empty),
					Latitude    = ping.Latitude,
					Longitude   = ping.Longitude,
					CreatedAt   = DateTime.TryParseExact(ping.Timestamp, "yyyy-MM-dd HH:mm:ss",
					              System.Globalization.CultureInfo.InvariantCulture,
					              System.Globalization.DateTimeStyles.AssumeLocal, out var ts)
					              ? new DateTimeOffset(ts).ToUniversalTime()
					              : DateTimeOffset.UtcNow
				});
			}, cancelToken))
			{
				ping.Synced = true;
				await conn.UpdateAsync(ping);
			}
		}
	}

	private static async Task<bool> RetryWithBackoff(Func<Task> action, CancellationToken cancelToken, int maxRetries = 3)
	{
		TimeSpan delay = TimeSpan.FromSeconds(2.0);
		for (int i = 0; i < maxRetries; i++)
		{
			if (cancelToken.IsCancellationRequested)
			{
				return false;
			}
			try
			{
				await action();
				return true;
			}
			catch
			{
				if (i < maxRetries - 1)
				{
					await Task.Delay(delay, cancelToken);
				}
				delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2.0);
			}
		}
		return false;
	}

	private async Task CheckForNewJobs(CancellationToken cancelToken)
	{
		try
		{
			string userId = supabase.Auth.CurrentUser?.Id;
			if (string.IsNullOrEmpty(userId))
			{
				return;
			}
			string notifiedIds = Preferences.Default.Get("jobs.notified_ids", string.Empty);
			HashSet<string> notifiedSet = new HashSet<string>(notifiedIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
			List<DispatchJobRecord> newJobs = (await supabase.From<DispatchJobRecord>().Filter("engineer_user_id", Constants.Operator.Equals, userId).Filter("status", Constants.Operator.Equals, "Pending")
				.Get()).Models.Where((DispatchJobRecord j) => !notifiedSet.Contains(j.Id.ToString())).ToList();
			foreach (DispatchJobRecord job in newJobs)
			{
				await notifications.Send(new Notification
				{
					Title = "New Job Assigned",
					Message = job.Title
				});
				notifiedSet.Add(job.Id.ToString());
			}
			if (newJobs.Count > 0)
			{
				Preferences.Default.Set("jobs.notified_ids", string.Join(",", notifiedSet));
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Debug.WriteLine("Job notification check failed: " + ex2.Message);
		}
	}
}
