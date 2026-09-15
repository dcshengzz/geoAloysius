using System;
using SQLite;

namespace GpsSync;

public class NetworkEvent
{
	[PrimaryKey]
	[AutoIncrement]
	public int Id { get; set; }

	public bool HasInternet { get; set; }

	public string? ConnectionTypes { get; set; }

	public DateTimeOffset Timestamp { get; set; }
}
