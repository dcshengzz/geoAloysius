using System.IO;
using Microsoft.Maui.Storage;
using SQLite;

namespace GpsSync;

public class MySqliteConnection : SQLiteAsyncConnection
{
	public AsyncTableQuery<JobRun> JobRuns => Table<JobRun>();

	public AsyncTableQuery<NetworkEvent> NetworkEvents => Table<NetworkEvent>();

	public AsyncTableQuery<GpsPing> GpsPings => Table<GpsPing>();

	private readonly Task _initTask;

	public MySqliteConnection()
		: base(Path.Combine(FileSystem.AppDataDirectory, "test.db"))
	{
		_initTask = Task.Run(async () =>
		{
			await CreateTableAsync<JobRun>();
			await CreateTableAsync<NetworkEvent>();
			await CreateTableAsync<GpsPing>();
		});
	}

	public Task InitializeAsync() => _initTask;
}
