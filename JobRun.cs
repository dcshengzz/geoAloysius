using System;
using SQLite;

namespace GpsSync;

public class JobRun
{
	[PrimaryKey]
	[AutoIncrement]
	public int Id { get; set; }

	public bool IsPunchedIn { get; set; }

	public DateTimeOffset Timestamp { get; set; }
}
