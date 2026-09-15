using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace GpsSync;

[Table("profiles")]
public class ProfileRecord : BaseModel
{
	[PrimaryKey("user_id", false)]
	public string UserId { get; set; } = string.Empty;

	[Column("is_admin", NullValueHandling.Include, false, false)]
	public bool IsAdmin { get; set; }

	[Column("display_name", NullValueHandling.Include, false, false)]
	public string? DisplayName { get; set; }

	[Column("email", NullValueHandling.Include, false, false)]
	public string? Email { get; set; }

	[Column("is_punched_in", NullValueHandling.Include, false, false)]
	public bool IsPunchedIn { get; set; }

	[Column("active_device_token", NullValueHandling.Include, false, false)]
	public string? ActiveDeviceToken { get; set; }

}
