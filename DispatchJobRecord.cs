using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace GpsSync;

[Table("dispatch_jobs")]
public class DispatchJobRecord : BaseModel
{
	[PrimaryKey("id", false)]
	public long Id { get; set; }

	[Column("engineer_user_id", NullValueHandling.Include, false, false)]
	public string EngineerUserId { get; set; } = string.Empty;

	[Column("admin_user_id", NullValueHandling.Include, false, false)]
	public string AdminUserId { get; set; } = string.Empty;

	[Column("title", NullValueHandling.Include, false, false)]
	public string Title { get; set; } = string.Empty;

	[Column("description", NullValueHandling.Include, false, false)]
	public string Description { get; set; } = string.Empty;

	[Column("status", NullValueHandling.Include, false, false)]
	public string Status { get; set; } = "Pending";

	[Column("created_at", NullValueHandling.Include, false, false)]
	public DateTime CreatedAt { get; set; }

	[Column("completed_at", NullValueHandling.Include, false, false)]
	public DateTime? CompletedAt { get; set; }
}
