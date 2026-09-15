using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shiny;

namespace GpsSync;

public class NetworkLogViewModel : AbstractLogViewModel<NetworkEvent>
{
	public NetworkLogViewModel(BaseServices services, MySqliteConnection data)
		: base(services, data)
	{
	}

	protected override async Task<List<NetworkEvent>> LoadData(int page, int pageSize, DateTime? from, DateTime? to)
	{
		List<NetworkEvent> all = await base.Data.NetworkEvents.OrderByDescending((NetworkEvent x) => x.Timestamp).ToListAsync();
		if (from.HasValue)
		{
			all = all.Where((NetworkEvent x) => x.Timestamp.LocalDateTime >= from.Value).ToList();
		}
		if (to.HasValue)
		{
			all = all.Where((NetworkEvent x) => x.Timestamp.LocalDateTime <= to.Value.AddDays(1.0)).ToList();
		}
		return all.Skip(page * pageSize).Take(pageSize).ToList();
	}
}
