using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;

namespace GpsSync;

public abstract class AbstractLogViewModel<T> : ViewModel where T : class, new()
{
	private const int PageSize = 20;

	private int currentPage;

	protected MySqliteConnection Data { get; }

	public ICommand Load { get; }

	public ICommand LoadMore { get; }

	public ICommand Clear { get; }

	public ICommand ApplyFilter { get; }

	public ICommand ClearFilter { get; }

	public ICommand ToggleFilter { get; }

	[Reactive]
	public List<T>? Logs { get; protected set; }

	[Reactive]
	public bool HasMore { get; private set; }

	[Reactive]
	public bool IsFilterVisible { get; set; }

	[Reactive]
	public DateTime? FilterFrom { get; set; }

	[Reactive]
	public DateTime? FilterTo { get; set; }

	[Reactive]
	public DateTime FromDate { get; set; }

	[Reactive]
	public DateTime ToDate { get; set; }

	protected AbstractLogViewModel(BaseServices services, MySqliteConnection data)
		: base(services)
	{
		Data = data;
		FromDate = DateTime.Today.AddDays(-7.0);
		ToDate = DateTime.Today;
		Load = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			currentPage = 0;
			List<T> items = await LoadData(0, 20, FilterFrom, FilterTo);
			HasMore = items.Count == 20;
			Logs = items;
		}, (IObservable<bool>?)null, (IScheduler?)null);
		BindBusyCommand(Load);
		LoadMore = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			currentPage++;
			List<T> items = await LoadData(currentPage, 20, FilterFrom, FilterTo);
			HasMore = items.Count == 20;
			Logs = (Logs ?? new List<T>()).Concat(items).ToList();
		}, this.WhenAnyValue((AbstractLogViewModel<T> x) => x.HasMore), (IScheduler?)null);
		Clear = ReactiveCommand.CreateFromTask(ClearData);
		ApplyFilter = ReactiveCommand.Create(delegate
		{
			FilterFrom = FromDate.Date;
			FilterTo = ToDate.Date;
			Load.Execute(null);
		});
		ClearFilter = ReactiveCommand.Create(delegate
		{
			FilterFrom = null;
			FilterTo = null;
			IsFilterVisible = false;
			Load.Execute(null);
		});
		ToggleFilter = ReactiveCommand.Create(() => IsFilterVisible = !IsFilterVisible);
	}

	public override void OnAppearing()
	{
		base.OnAppearing();
		Load.Execute(null);
	}

	protected abstract Task<List<T>> LoadData(int page, int pageSize, DateTime? from, DateTime? to);

	protected virtual async Task ClearData()
	{
		if (await base.Dialogs.Confirm("Delete all data?", "Confirm"))
		{
			await Data.DeleteAllAsync<T>();
			Load.Execute(null);
		}
	}
}
