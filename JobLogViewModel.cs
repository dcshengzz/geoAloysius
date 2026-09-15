using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shiny;

namespace GpsSync;

public class JobLogViewModel : AbstractLogViewModel<JobRun>
{
	public JobLogViewModel(BaseServices services, MySqliteConnection data)
		: base(services, data)
	{
	}

	protected override async Task<List<JobRun>> LoadData(int page, int pageSize, DateTime? from, DateTime? to)
	{
		List<JobRun> all = await base.Data.JobRuns.OrderByDescending((JobRun x) => x.Timestamp).ToListAsync();
		if (from.HasValue)
		{
			all = all.Where((JobRun x) => x.Timestamp.LocalDateTime >= from.Value).ToList();
		}
		if (to.HasValue)
		{
			all = all.Where((JobRun x) => x.Timestamp.LocalDateTime <= to.Value.AddDays(1.0)).ToList();
		}
		return all.Skip(page * pageSize).Take(pageSize).ToList();
	}
}
