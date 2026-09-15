using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shiny;

namespace GpsSync;

public class GpsLogViewModel : AbstractLogViewModel<GpsPing>
{
	public GpsLogViewModel(BaseServices services, MySqliteConnection conn)
		: base(services, conn)
	{
	}

	protected override async Task<List<GpsPing>> LoadData(int page, int pageSize, DateTime? from, DateTime? to)
	{
		string fromStr = from?.ToString("yyyy-MM-dd") ?? "0000-00-00";
		string toStr = to?.AddDays(1.0).ToString("yyyy-MM-dd") ?? "9999-12-31";
		int offset = page * pageSize;
		return await base.Data.QueryAsync<GpsPing>("SELECT * FROM GpsPing WHERE Timestamp >= ? AND Timestamp <= ? ORDER BY Timestamp DESC LIMIT ? OFFSET ?", new object[4] { fromStr, toStr, pageSize, offset });
	}
}
