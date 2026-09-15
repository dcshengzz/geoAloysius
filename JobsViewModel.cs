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

public class JobsViewModel : ViewModel
{
	private readonly IBackendClient backend;
	private readonly AppSettings settings;

	[Reactive]
	public List<JobItem>? Jobs { get; set; }

	public ICommand Load { get; }
	public ICommand ClearCompleted { get; }

	public JobsViewModel(BaseServices services, IBackendClient backend, AppSettings settings)
		: base(services)
	{
		this.backend = backend;
		this.settings = settings;
		Load = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			string? userId = this.backend.CurrentUserId;
			if (userId != null)
			{
				var jobs = await this.backend.GetJobsAsync(engineerUserId: userId);
				Jobs = jobs.OrderByDescending(r => r.CreatedAt)
					.Select(r => new JobItem(r, this.backend, this.settings)).ToList();
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);
		BindBusyCommand(Load);
		ClearCompleted = ReactiveCommand.Create(() =>
		{
			if (Jobs != null)
				Jobs = Jobs.Where(j => j.Status != "Completed").ToList();
		});
	}

	public override void OnAppearing()
	{
		base.OnAppearing();
		Preferences.Default.Set("badges.myjobs_cleared_at", DateTime.UtcNow.ToString("O"));
		Load.Execute(null);
	}
}
