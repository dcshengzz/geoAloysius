using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Shiny;
using Notification = Shiny.Notifications.Notification;
using Shiny.Notifications;

namespace GpsSync;

public class JobNotificationService : IShinyStartupTask
{
	private readonly AppSettings settings;

	private readonly IBackendClient backend;

	private readonly INotificationManager notifications;

	private IDisposable? timer;
	private readonly Dictionary<string, bool> _lastPunchState = new();

	public JobNotificationService(AppSettings settings, IBackendClient backend, INotificationManager notifications)
	{
		this.settings = settings;
		this.backend = backend;
		this.notifications = notifications;
	}

	public void Start()
	{
		timer = Observable.Interval(TimeSpan.FromSeconds(30.0)).SubscribeAsync(async delegate
		{
			await CheckAsync();
		});
	}

	private async Task CheckAsync()
	{
		try
		{
			string? userId = backend.CurrentUserId;
			if (!string.IsNullOrEmpty(userId))
			{
				if (settings.IsAdmin)
				{
					if (Preferences.Default.Get("admin.notify_punch", defaultValue: true))
						await CheckPunchStateChangesAsync();
					if (Preferences.Default.Get("admin.notify_job_status", defaultValue: true))
						await CheckJobStatusChangesAsync(userId);
				}
				else
				{
					await CheckNewJobsAsync(userId);
				}
			}
		}
		catch
		{
		}
	}

	private async Task CheckNewJobsAsync(string userId)
	{
		string notifiedIds = Preferences.Default.Get("jobs.notified_ids", string.Empty);
		HashSet<string> notifiedSet = new HashSet<string>(notifiedIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
		var newJobs = (await backend.GetJobsAsync(engineerUserId: userId, status: "Pending"))
			.Where(j => !notifiedSet.Contains(j.Id.ToString())).ToList();
		if (newJobs.Count > 0)
		{
			// Batch all new jobs into a single notification (fixed Id replaces any existing one)
			string message = newJobs.Count == 1
				? newJobs[0].Title
				: $"{newJobs.Count} new jobs have been assigned to you";
			await notifications.Send(new Notification
			{
				Id = 1001,
				Title = "New Job Assigned",
				Message = message
			});
			foreach (var job in newJobs)
				notifiedSet.Add(job.Id.ToString());
			Preferences.Default.Set("jobs.notified_ids", string.Join(",", notifiedSet));
			ShowToast(newJobs.Count == 1 ? "A new job has been dispatched to you" : $"{newJobs.Count} new jobs dispatched to you");
		}
	}

	private async Task CheckPunchStateChangesAsync()
	{
		var engineers = (await backend.ListProfilesAsync()).Where(p => !p.IsAdmin).ToList();
		foreach (var profile in engineers)
		{
			bool currentState = profile.IsPunchedIn;
			string label = !string.IsNullOrWhiteSpace(profile.DisplayName)
				? profile.DisplayName.Trim()
				: (!string.IsNullOrWhiteSpace(profile.Email) ? profile.Email.Trim() : profile.UserId);

			if (_lastPunchState.TryGetValue(profile.UserId, out bool previousState))
			{
				if (currentState != previousState)
				{
					string action = currentState ? "punched in" : "punched out";
					await notifications.Send(new Notification
					{
						Id = currentState ? 2001 : 2002,
						Title = currentState ? "Engineer Punched In" : "Engineer Punched Out",
						Message = label + " has " + action
					});
					ShowToast(label + " has " + action);
				}
			}
			_lastPunchState[profile.UserId] = currentState;
		}
	}

	private async Task CheckJobStatusChangesAsync(string adminUserId)
	{
		string acceptedIds = Preferences.Default.Get("jobs.accepted_notified_ids", string.Empty);
		HashSet<string> acceptedSet = new HashSet<string>(acceptedIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
		string declinedIds = Preferences.Default.Get("jobs.declined_notified_ids", string.Empty);
		HashSet<string> declinedSet = new HashSet<string>(declinedIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
		string completedIds = Preferences.Default.Get("jobs.completed_notified_ids", string.Empty);
		HashSet<string> completedSet = new HashSet<string>(completedIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
		var jobs = await backend.GetJobsAsync(adminUserId: adminUserId);
		bool anyAccepted = false;
		bool anyDeclined = false;
		bool anyCompleted = false;
		foreach (var job in jobs)
		{
			string idStr = job.Id.ToString();
			if (job.Status == "In Progress" && !acceptedSet.Contains(idStr))
			{
				await notifications.Send(new Notification
				{
					Id = 3001,
					Title = "Job Accepted",
					Message = job.Title + " is now in progress"
				});
				acceptedSet.Add(idStr);
				anyAccepted = true;
			}
			if (job.Status == "Declined" && !declinedSet.Contains(idStr))
			{
				await notifications.Send(new Notification
				{
					Id = 3002,
					Title = "Job Declined",
					Message = job.Title + " was declined by the engineer"
				});
				declinedSet.Add(idStr);
				anyDeclined = true;
			}
			if (job.Status == "Completed" && !completedSet.Contains(idStr))
			{
				completedSet.Add(idStr);
				anyCompleted = true;
			}
		}
		if (anyAccepted)
		{
			Preferences.Default.Set("jobs.accepted_notified_ids", string.Join(",", acceptedSet));
			ShowToast("An engineer has accepted a job");
		}
		if (anyDeclined)
		{
			Preferences.Default.Set("jobs.declined_notified_ids", string.Join(",", declinedSet));
			ShowToast("An engineer has declined a job");
		}
		if (anyCompleted)
		{
			Preferences.Default.Set("jobs.completed_notified_ids", string.Join(",", completedSet));
		}
	}

	private static void ShowToast(string message)
	{
		MainThread.BeginInvokeOnMainThread(async delegate
		{
			try
			{
				await Toast.Make(message, ToastDuration.Long).Show();
			}
			catch
			{
			}
		});
	}
}
