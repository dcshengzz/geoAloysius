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
using Supabase;
using Supabase.Postgrest;

namespace GpsSync;

public class JobsViewModel : ViewModel
{
	private readonly Supabase.Client supabase;
	private readonly AppSettings settings;

	[Reactive]
	public List<JobItem>? Jobs { get; set; }

	public ICommand Load { get; }
	public ICommand ClearCompleted { get; }

	public JobsViewModel(BaseServices services, Supabase.Client supabase, AppSettings settings)
		: base(services)
	{
		this.supabase = supabase;
		this.settings = settings;
		Load = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			string userId = this.supabase.Auth.CurrentUser?.Id;
			if (userId != null)
			{
				Jobs = (await this.supabase.From<DispatchJobRecord>().Filter("engineer_user_id", Constants.Operator.Equals, userId).Order("created_at", Constants.Ordering.Descending)
					.Get()).Models.OrderByDescending(r => r.CreatedAt).Select((DispatchJobRecord r) => new JobItem(r, this.supabase, this.settings)).ToList();
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
