using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace GpsSync;

[Table("gps_pings")]
public class GpsPingRecord : BaseModel
{
	[PrimaryKey("id", false)]
	public long Id { get; set; }

	[Column("user_id", NullValueHandling.Include, false, false)]
	public string UserId { get; set; } = string.Empty;

	[Column("latitude", NullValueHandling.Include, false, false)]
	public double Latitude { get; set; }

	[Column("longitude", NullValueHandling.Include, false, false)]
	public double Longitude { get; set; }

	[Column("display_name", NullValueHandling.Ignore, false, false)]
	public string? DisplayName { get; set; }

	[Column("created_at", NullValueHandling.Ignore, false, false)]
	public DateTimeOffset? CreatedAt { get; set; }
}
