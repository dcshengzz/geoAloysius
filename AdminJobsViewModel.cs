using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;

namespace GpsSync;

public class AdminJobsViewModel : ViewModel
{
	private readonly IBackendClient backend;

	[Reactive]
	public List<AdminJobItem>? PendingJobs { get; set; }

	[Reactive]
	public List<AdminJobItem>? InProgressJobs { get; set; }

	[Reactive]
	public List<AdminJobItem>? CompletedJobs { get; set; }

	[Reactive]
	public bool HasPending { get; set; }

	[Reactive]
	public bool HasInProgress { get; set; }

	[Reactive]
	public bool HasCompleted { get; set; }

	public ICommand Load { get; }

	public ICommand ClearCompleted { get; }

	public AdminJobsViewModel(BaseServices services, IBackendClient backend)
		: base(services)
	{
		this.backend = backend;
		Load = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			// Fetch profiles for name lookup
			var profiles = new Dictionary<string, string>();
			try
			{
				foreach (var p in await this.backend.ListProfilesAsync())
					profiles[p.UserId] = !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName.Trim()
						: !string.IsNullOrWhiteSpace(p.Email) ? p.Email.Trim()
						: p.UserId;
			}
			catch { }

			var all = await this.backend.GetJobsAsync();

			AdminJobItem ToItem(BackendJob j) => new AdminJobItem
			{
				Id = j.Id,
				Title = j.Title,
				Description = j.Description,
				Status = j.Status,
				CreatedAt = j.CreatedAt,
				CompletedAt = j.CompletedAt,
				EngineerName = profiles.TryGetValue(j.EngineerUserId, out var name) ? name : j.EngineerUserId
			};

			PendingJobs = all.Where(j => j.Status == "Pending").Select(ToItem).ToList();
			InProgressJobs = all.Where(j => j.Status == "In Progress").Select(ToItem).ToList();
			CompletedJobs = all.Where(j => j.Status == "Completed").Take(30).Select(ToItem).ToList();
			HasPending = PendingJobs.Count > 0;
			HasInProgress = InProgressJobs.Count > 0;
			HasCompleted = CompletedJobs.Count > 0;
		}, (IObservable<bool>?)null, (IScheduler?)null);
		BindBusyCommand(Load);
		ClearCompleted = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (await base.Dialogs.Confirm("Remove all completed jobs from the board?", "Clear Completed"))
			{
				await this.backend.ClearCompletedJobsAsync();
				Load.Execute(null);
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);
	}

	public override void OnAppearing()
	{
		base.OnAppearing();
		Preferences.Default.Set("badges.jobboard_cleared_at", DateTime.UtcNow.ToString("O"));
		Load.Execute(null);
	}
}
