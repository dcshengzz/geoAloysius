using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace GpsSync;

public class JobItem : ReactiveObject
{
	private readonly IBackendClient backend;

	private readonly AppSettings settings;

	private readonly BackendJob record;

	private readonly ObservableAsPropertyHelper<bool> _canStart;

	private readonly ObservableAsPropertyHelper<bool> _canComplete;

	private readonly ObservableAsPropertyHelper<bool> _showActions;

	public long Id => record.Id;

	public string Title => record.Title;

	public string Description => record.Description;

	public DateTime CreatedAt => record.CreatedAt;

	[Reactive]
	public DateTime? CompletedAt { get; set; }

	[Reactive]
	public string Status { get; set; }

	public bool CanStart => _canStart.Value;

	public bool CanComplete => _canComplete.Value;

	public bool ShowActions => _showActions.Value;

	public ICommand MarkInProgress { get; }

	public ICommand MarkCompleted { get; }

	public JobItem(BackendJob record, IBackendClient backend, AppSettings settings)
	{
		this.record = record;
		this.backend = backend;
		this.settings = settings;
		Status = record.Status;
		CompletedAt = record.CompletedAt;
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
		await backend.UpdateJobStatusAsync(Id, newStatus);
		if (newStatus == "Completed")
			CompletedAt = DateTime.UtcNow;
		Status = newStatus;
	}
}
