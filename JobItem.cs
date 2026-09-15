using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Supabase;

namespace GpsSync;

public class JobItem : ReactiveObject
{
	private readonly Client supabase;

	private readonly AppSettings settings;

	private readonly DispatchJobRecord record;

	private readonly ObservableAsPropertyHelper<bool> _canStart;

	private readonly ObservableAsPropertyHelper<bool> _canComplete;

	private readonly ObservableAsPropertyHelper<bool> _showActions;

	private string _0024Status;

	public long Id => record.Id;

	public string Title => record.Title;

	public string Description => record.Description;

	public DateTime CreatedAt => record.CreatedAt;

	public DateTime? CompletedAt => record.CompletedAt;

	[Reactive]
	public string Status
	{
		[CompilerGenerated]
		get
		{
			return _0024Status;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024Status, value, "Status");
		}
	}

	public bool CanStart => _canStart.Value;

	public bool CanComplete => _canComplete.Value;

	public bool ShowActions => _showActions.Value;

	public ICommand MarkInProgress { get; }

	public ICommand MarkCompleted { get; }

	public JobItem(DispatchJobRecord record, Client supabase, AppSettings settings)
	{
		this.record = record;
		this.supabase = supabase;
		this.settings = settings;
		Status = record.Status;
		_canStart = this.WhenAnyValue((JobItem x) => x.Status, (string s) => s == "Pending").ToProperty(this, (JobItem x) => x.CanStart);
		_canComplete = this.WhenAnyValue((JobItem x) => x.Status, (string s) => s == "In Progress").ToProperty(this, (JobItem x) => x.CanComplete);
		_showActions = this.WhenAnyValue((JobItem x) => x.Status, (string s) => s == "Pending" || s == "In Progress").ToProperty(this, (JobItem x) => x.ShowActions);
		MarkInProgress = ReactiveCommand.CreateFromTask(async () =>
		{
			if (!this.settings.IsPunchedIn)
			{
				await Microsoft.Maui.Controls.Application.Current.MainPage.DisplayAlert(
					"Not Punched In", "You need to punch in before starting a job.", "OK");
				return;
			}
			await UpdateStatus("In Progress");
		}, this.WhenAnyValue((JobItem x) => x.CanStart));
		MarkCompleted = ReactiveCommand.CreateFromTask(async () =>
		{
			bool confirmed = await Microsoft.Maui.Controls.Application.Current.MainPage.DisplayAlert(
				"Complete Job", "Mark this job as completed?", "Complete", "Cancel");
			if (confirmed)
				await UpdateStatus("Completed");
		}, this.WhenAnyValue((JobItem x) => x.CanComplete));
	}

	private async Task UpdateStatus(string newStatus)
	{
		record.Status = newStatus;
		if (newStatus == "Completed")
			record.CompletedAt = DateTime.UtcNow;
		await supabase.From<DispatchJobRecord>().Update(record);
		Status = newStatus;
	}
}
