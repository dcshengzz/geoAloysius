using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Graphics;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;
using Prism.Navigation;

namespace GpsSync;

public class EngineerStatusViewModel : ViewModel
{
	private readonly IBackendClient backend;

	private IDisposable? autoRefresh;
	private readonly Dictionary<string, string> _renameCache = new();

	public Func<string, Task<(string Title, string Description)?>>? RequestJobDetails { get; set; }

	[Reactive]
	public ObservableCollection<EngineerStatusItem> Engineers { get; set; } = new ObservableCollection<EngineerStatusItem>();

	[Reactive]
	public new bool IsBusy { get; set; }

	[Reactive]
	public string Summary { get; set; } = string.Empty;

	public ICommand Refresh { get; }

	public EngineerStatusViewModel(BaseServices services, IBackendClient backend)
		: base(services)
	{
		this.backend = backend;
		Refresh = ReactiveCommand.CreateFromTask(LoadEngineers);
	}

	public override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadEngineers();
		autoRefresh?.Dispose();
		autoRefresh = Observable.Interval(TimeSpan.FromSeconds(10.0)).ObserveOn(SynchronizationContext.Current).Subscribe(async delegate
		{
			await LoadEngineers();
		});
	}

	public override void OnDisappearing()
	{
		base.OnDisappearing();
		autoRefresh?.Dispose();
		autoRefresh = null;
	}

	private async Task LoadEngineers()
	{
		IsBusy = true;
		try
		{
			List<BackendProfile> engineers = (await backend.ListProfilesAsync()).Where(p => !p.IsAdmin).ToList();
			Dictionary<string, BackendPing> latestPings = (await backend.GetLatestPingsAsync())
				.GroupBy(p => p.UserId)
				.ToDictionary(g => g.Key, g => g.First());
			List<EngineerStatusItem> items = (from x in engineers.Select(delegate(BackendProfile profile)
				{
					latestPings.TryGetValue(profile.UserId, out var value);
					string label = _renameCache.TryGetValue(profile.UserId, out var cachedName)
						? cachedName
						: GetLabel(profile.DisplayName, profile.Email, profile.UserId);
					string initials = _renameCache.TryGetValue(profile.UserId, out var cachedInitials)
						? GetInitials(cachedInitials, null, profile.UserId)
						: GetInitials(profile.DisplayName, profile.Email, profile.UserId);
					Color statusColor = (profile.IsPunchedIn ? Color.FromArgb("#27ae60") : Colors.Gray);
					EngineerStatusItem item = new EngineerStatusItem
					{
						UserId = profile.UserId,
						DisplayName = label,
						Initials = initials,
						StatusText = (profile.IsPunchedIn ? "Punched In" : "Punched Out"),
						StatusColor = statusColor,
						LastSeen = ((value != null && value.CreatedAt.HasValue) ? RelativeTime(value.CreatedAt.Value.UtcDateTime) : "No location data")
					};
					item.AssignCommand = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
					{
						if (RequestJobDetails != null)
						{
							(string Title, string Description)? result = await RequestJobDetails(item.DisplayName);
							if (result.HasValue)
							{
								try
								{
									await backend.CreateJobAsync(item.UserId, result.Value.Title, result.Value.Description);
									await base.Dialogs.Alert("Job assigned successfully.", "Done");
								}
								catch
								{
									await base.Dialogs.Alert("Failed to assign job. Please try again.", "Error");
								}
							}
						}
					}, (IObservable<bool>?)null, (IScheduler?)null);
					item.RenameCommand = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
				{
					string? newName = await Microsoft.Maui.Controls.Application.Current?.MainPage?.DisplayPromptAsync(
						"Rename Engineer",
						"Enter new display name for " + item.DisplayName + ":",
						"Save",
						"Cancel",
						initialValue: item.DisplayName,
						maxLength: 60);
					if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == item.DisplayName)
						return;
					string trimmed = newName.Trim();
					try
					{
						await backend.UpdateProfileAsync(item.UserId, displayName: trimmed);
						_renameCache[item.UserId] = trimmed;
						// Instantly update the item and rebind the collection
						item.DisplayName = trimmed;
						item.Initials = GetInitials(trimmed, null, item.UserId);
						Engineers = new System.Collections.ObjectModel.ObservableCollection<EngineerStatusItem>(Engineers);
					}
					catch
					{
						await base.Dialogs.Alert("Failed to update display name. Please try again.", "Error");
					}
				}, (IObservable<bool>?)null, (IScheduler?)null);
				item.RemoveCommand = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
				{
					if (!await base.Dialogs.Confirm("Remove " + item.DisplayName + "? This will permanently delete their account, profile, and all location history.", "Remove Engineer"))
						return;
					autoRefresh?.Dispose();
					autoRefresh = null;
					try
					{
						bool ok = true;
						try { await backend.DeleteUserAsync(item.UserId); } catch { ok = false; }
						if (!ok)
						{
							await base.Dialogs.Alert("Could not delete engineer. Please check your connection and try again.", "Delete Failed");
						}
						else
						{
							var updated = new System.Collections.ObjectModel.ObservableCollection<EngineerStatusItem>(Engineers.Where(e => e.UserId != item.UserId));
							Engineers = updated;
							int pi = updated.Count(e => e.StatusText == "Punched In");
							Summary = $"{pi} punched in  ·  {updated.Count - pi} punched out";
						}
					}
					finally
					{
						autoRefresh = Observable.Interval(TimeSpan.FromSeconds(10.0)).ObserveOn(SynchronizationContext.Current).Subscribe(async delegate
						{
							await LoadEngineers();
						});
					}
				}, (IObservable<bool>?)null, (IScheduler?)null);
					return item;
				})
				orderby (!(x.StatusText == "Punched In")) ? 1 : 0, x.DisplayName
				select x).ToList();
			Engineers = new ObservableCollection<EngineerStatusItem>(items);
			int punchedIn = items.Count((EngineerStatusItem x) => x.StatusText == "Punched In");
			int punchedOut = items.Count - punchedIn;
			Summary = $"{punchedIn} punched in  ·  {punchedOut} punched out";
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Debug.WriteLine("EngineerStatus load failed: " + ex2.Message);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private static string GetLabel(string? displayName, string? email, string userId)
	{
		if (!string.IsNullOrWhiteSpace(displayName))
		{
			return displayName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(email))
		{
			return email.Trim();
		}
		string result;
		if (userId.Length < 8)
		{
			result = userId;
		}
		else
		{
			int length = userId.Length;
			int num = length - 6;
			result = "User …" + userId.Substring(num, length - num);
		}
		return result;
	}

	private static string GetInitials(string? displayName, string? email, string userId)
	{
		string text = ((!string.IsNullOrWhiteSpace(displayName)) ? displayName.Trim() : ((!string.IsNullOrWhiteSpace(email)) ? email.Trim() : userId));
		string[] array = text.Split(new char[3] { ' ', '@', '.' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			return "?";
		}
		if (array.Length >= 2)
		{
			return $"{array[0][0]}{array[1][0]}".ToUpper();
		}
		return array[0].Substring(0, Math.Min(2, array[0].Length)).ToUpper();
	}

	private static string RelativeTime(DateTime dt)
	{
		TimeSpan timeSpan = DateTime.UtcNow - dt.ToUniversalTime();
		if (timeSpan.TotalSeconds < 60.0)
		{
			return "Just now";
		}
		if (!(timeSpan.TotalMinutes < 60.0))
		{
			if (!(timeSpan.TotalHours < 24.0))
			{
				return $"{(int)timeSpan.TotalDays}d ago";
			}
			return $"{(int)timeSpan.TotalHours}h ago";
		}
		return $"{(int)timeSpan.TotalMinutes} min ago";
	}
}
