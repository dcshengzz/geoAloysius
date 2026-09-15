using SQLite;

namespace GpsSync;

public class GpsPing
{
	[PrimaryKey]
	[AutoIncrement]
	public int Id { get; set; }

	public double Latitude { get; set; }

	public double Longitude { get; set; }

	public string Timestamp { get; set; } = string.Empty;

	public bool Synced { get; set; }
}
