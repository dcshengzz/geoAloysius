using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Map = Mapsui.Map;
using Brush = Mapsui.Styles.Brush;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Microsoft.Maui.Graphics;
using NetTopologySuite.Geometries;
using Prism.Navigation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Shiny;
using Shiny.Locations;
using Shiny.Notifications;
using Supabase;
using Supabase.Gotrue;
using Supabase.Postgrest;
using Notification = Shiny.Notifications.Notification;

namespace GpsSync;

public class MapViewModel : ViewModel
{
	private readonly MySqliteConnection data;
	private readonly IGpsManager gpsManager;
	private readonly Supabase.Client supabase;
	private readonly AppSettings settings;
	private readonly INotificationManager notifications;

	private readonly MemoryLayer trailLayer;
	private readonly MemoryLayer allUsersLayer;
	private readonly MemoryLayer ownLayer;

	private IDisposable? gpsSubscription;
	private IDisposable? adminRefreshSubscription;

	private string? pendingJobUserId;
	private MPoint? lastKnownPoint;

	// Engineers that have already triggered a stale notification this session
	private readonly HashSet<string> staleNotifiedIds = new();

	// ── Reactive properties ──────────────────────────────────────────────────

	[Reactive] public bool IsAdmin { get; private set; }
	[Reactive] public bool IsRefreshing { get; set; }
	[Reactive] public int FilterMode { get; set; } // 0=All, 1=Active Only, 2=Available (no active job)
	[Reactive] public string EngineerSummary { get; set; } = string.Empty;

	[Reactive] public bool IsCalloutVisible { get; set; }
	[Reactive] public string CalloutName { get; set; } = string.Empty;
	[Reactive] public string CalloutStatus { get; set; } = string.Empty;
	[Reactive] public Microsoft.Maui.Graphics.Color CalloutStatusColor { get; set; } = Colors.Gray;
	[Reactive] public string CalloutLastSeen { get; set; } = string.Empty;
	[Reactive] public string CalloutLastPing { get; set; } = string.Empty;

	public Map Map { get; }

	public ICommand CenterOnMe { get; }
	public ICommand DismissCallout { get; }
	public ICommand AssignJobFromCallout { get; }
	public ICommand SetFilterMode { get; }
	public ICommand ManualRefresh { get; }

	public Func<string, Task<(string Title, string Description)?>>? RequestJobDetails { get; set; }

	// ── Constructor ──────────────────────────────────────────────────────────

	public MapViewModel(BaseServices services, MySqliteConnection data, IGpsManager gpsManager,
		Supabase.Client supabase, AppSettings settings, INotificationManager notifications)
		: base(services)
	{
		this.data = data;
		this.gpsManager = gpsManager;
		this.supabase = supabase;
		this.settings = settings;
		this.notifications = notifications;

		trailLayer = new MemoryLayer("Trails") { Style = null };
		allUsersLayer = new MemoryLayer("All Users") { IsMapInfoLayer = true, Style = null };
		ownLayer = new MemoryLayer("My Location")
		{
			Style = new SymbolStyle
			{
				Fill = new Brush(Mapsui.Styles.Color.Red),
				Outline = new Pen(Mapsui.Styles.Color.White, 2.0),
				SymbolScale = 0.6
			}
		};

		Map = new Map();
		Map.Layers.Add(OpenStreetMap.CreateTileLayer());
		Map.Layers.Add(trailLayer);
		Map.Layers.Add(allUsersLayer);
		Map.Layers.Add(ownLayer);

		var (sgX, sgY) = SphericalMercator.FromLonLat(103.8198, 1.3521);
		Map.Home = n => n.CenterOnAndZoomTo(new MPoint(sgX, sgY), n.Resolutions[11], -1L);

		CenterOnMe = ReactiveCommand.Create(() =>
		{
			if (lastKnownPoint != null)
				Map.Navigator.CenterOnAndZoomTo(lastKnownPoint, Map.Navigator.Resolutions[16], -1L);
		});

		DismissCallout = ReactiveCommand.Create(() => IsCalloutVisible = false);

		AssignJobFromCallout = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			if (pendingJobUserId != null)
			{
				IsCalloutVisible = false;
				await AssignJobToUser(pendingJobUserId);
			}
		}, (IObservable<bool>?)null, (IScheduler?)null);

		SetFilterMode = ReactiveCommand.Create<object>(p =>
		{
			if (p is string s && int.TryParse(s, out int mode))
				FilterMode = mode;
		});

		ManualRefresh = ReactiveCommand.CreateFromTask((Func<Task>)async delegate
		{
			await RefreshAllUserLocations();
		}, (IObservable<bool>?)null, (IScheduler?)null);

		// Re-render when filter changes
		this.WhenAnyValue(x => x.FilterMode)
			.Skip(1)
			.Subscribe(_ => { RefreshAllUserLocations().ConfigureAwait(false); });
	}

	// ── Callout ──────────────────────────────────────────────────────────────

	public void ShowCallout(IFeature feature)
	{
		string userId = (feature["user_id"] as string) ?? string.Empty;
		string label = (feature["label"] as string) ?? GetLabel(null, null, userId);
		string status = (feature["status"] as string) ?? "offline";
		DateTime createdAt = feature["created_at"] is DateTime dt ? dt : DateTime.MinValue;

		pendingJobUserId = userId;
		CalloutName = label;

		CalloutStatus = status switch
		{
			"online" => "Punched In · Active",
			"weak"   => "Punched In · Weak Signal",
			"stale"  => "Punched In · GPS Signal Lost",
			"busy"   => "Punched In · Job In Progress",
			_        => "Punched Out"
		};

		CalloutStatusColor = status switch
		{
			"online" => Colors.Green,
			"weak"   => Microsoft.Maui.Graphics.Color.FromArgb("#f39c12"),
			"stale"  => Microsoft.Maui.Graphics.Color.FromArgb("#e74c3c"),
			"busy"   => Microsoft.Maui.Graphics.Color.FromArgb("#3498db"),
			_        => Colors.Gray
		};

		CalloutLastPing = createdAt == DateTime.MinValue
			? "No ping data"
			: "Last ping: " + RelativeTime(createdAt);

		CalloutLastSeen = createdAt == DateTime.MinValue ? string.Empty : createdAt.ToLocalTime().ToString("h:mm tt");

		IsCalloutVisible = true;
	}

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override async void OnAppearing()
	{
		base.OnAppearing();
		if (!settings.IsAdmin)
			settings.IsAdmin = await CheckIsAdmin();
		IsAdmin = settings.IsAdmin;

		if (IsAdmin)
		{
			// Clear stale layer data immediately so the map never shows old pins
			trailLayer.Features = Array.Empty<IFeature>();
			trailLayer.DataHasChanged();
			allUsersLayer.Features = Array.Empty<IFeature>();
			allUsersLayer.DataHasChanged();

			await RefreshAllUserLocations();
			adminRefreshSubscription?.Dispose();
			adminRefreshSubscription = Observable
				.Interval(TimeSpan.FromSeconds(5.0))
				.ObserveOn(SynchronizationContext.Current)
				.Subscribe(async delegate { await RefreshAllUserLocations(); });
		}
		else
		{
			// Clear admin-specific layers so stale pins from a prior admin session don't linger
			trailLayer.Features = Array.Empty<IFeature>();
			trailLayer.DataHasChanged();
			allUsersLayer.Features = Array.Empty<IFeature>();
			allUsersLayer.DataHasChanged();

			GpsPing latest = await data.GpsPings
				.OrderByDescending(p => p.Timestamp)
				.FirstOrDefaultAsync();
			if (latest != null)
			{
				var (x, y) = SphericalMercator.FromLonLat(latest.Longitude, latest.Latitude);
				lastKnownPoint = new MPoint(x, y);
				UpdateOwnPin(latest.Latitude, latest.Longitude, centerMap: false);
			}

			gpsSubscription = gpsManager.WhenReading()
				.ObserveOn(SynchronizationContext.Current)
				.Subscribe(r => UpdateOwnPin(r.Position.Latitude, r.Position.Longitude, centerMap: true));
		}
	}

	public override void OnDisappearing()
	{
		base.OnDisappearing();
		gpsSubscription?.Dispose();
		gpsSubscription = null;
		adminRefreshSubscription?.Dispose();
		adminRefreshSubscription = null;
	}

	// ── Map refresh ───────────────────────────────────────────────────────────

	private async Task RefreshAllUserLocations()
	{
		IsRefreshing = true;
		try
		{
			// Fetch all profiles first
			var profilesDict = new Dictionary<string, ProfileRecord>();
			try
			{
				foreach (var p in (await supabase.From<ProfileRecord>().Select("*").Get()).Models)
					profilesDict[p.UserId] = p;
			}
			catch { return; } // network error - keep whatever is currently displayed

			// Guard: if profiles came back empty (e.g. RLS or network issue), don't wipe the map
			if (profilesDict.Count == 0) return;

			// Fetch last 10 pings per non-admin user in parallel (avoids global limit cutting engineers out)
			var nonAdminIds = profilesDict.Values.Where(p => !p.IsAdmin).Select(p => p.UserId).ToList();
			var pingTasks = nonAdminIds.Select(async uid =>
			{
				try
				{
					var pings = (await supabase.From<GpsPingRecord>()
						.Select("*")
						.Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, uid)
						.Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
						.Limit(10)
						.Get()).Models;
					return (uid, pings.OrderBy(p => p.CreatedAt).ToList());
				}
				catch
				{
					return (uid, new List<GpsPingRecord>());
				}
			});
			var pingResults = await Task.WhenAll(pingTasks);
			var pingsPerUser = pingResults.ToDictionary(r => r.uid, r => r.Item2);

							// Fetch engineers with an active (non-completed) job
		var activeJobUserIds = new HashSet<string>();
		try
		{
			var activeJobs = (await supabase.From<DispatchJobRecord>()
				.Select("engineer_user_id,status")
				.Get()).Models
				.Where(j => j.Status == "In Progress")
				.Select(j => j.EngineerUserId);
			foreach (var uid in activeJobs) activeJobUserIds.Add(uid);
		}
		catch { }

		var trailFeatures = new List<IFeature>();
		var dotFeatures = new List<IFeature>();
		int activeCount = 0, busyCount = 0, totalEngineers = 0;

		// Iterate profiles so engineers with no pings yet are still counted
		foreach (var profile in profilesDict.Values)
		{
			if (profile.IsAdmin) continue;

			string userId = profile.UserId;
			totalEngineers++;

			pingsPerUser.TryGetValue(userId, out var userPings);
			var latestPing = userPings?.LastOrDefault();

			// Determine status
			string status;
			if (!profile.IsPunchedIn)
			{
				status = "offline";
			}
			else if (latestPing == null)
			{
				status = activeJobUserIds.Contains(userId) ? "busy" : "online";
			}
			else
			{
				DateTimeOffset createdAt = latestPing.CreatedAt ?? DateTimeOffset.UtcNow;
				TimeSpan age = DateTimeOffset.UtcNow - createdAt;
				string gpsStatus = age.TotalMinutes < 1.0 ? "online" : (age.TotalMinutes < 5.0 ? "weak" : "stale");
				status = (gpsStatus != "stale" && activeJobUserIds.Contains(userId)) ? "busy" : gpsStatus;
			}

			if (status == "busy") busyCount++;
			else if (status != "offline") activeCount++;

			// 0=All, 1=Online (online/weak), 2=Available, 3=On Job, 4=Offline (offline/stale)
			if (FilterMode == 1 && status != "online" && status != "weak") continue;
			if (FilterMode == 2 && (status == "offline" || status == "busy" || status == "stale")) continue;
			if (FilterMode == 3 && status != "busy") continue;
			if (FilterMode == 4 && status != "offline" && status != "stale") continue;
			// No location yet — counted in summary but no map dot
			if (latestPing == null) continue;

			DateTimeOffset pingCreatedAt = latestPing.CreatedAt ?? DateTimeOffset.UtcNow;
			TimeSpan pingAge = DateTimeOffset.UtcNow - pingCreatedAt;

			// Trail line ΓÇö only when actively online/moving, not when offline or stale
			if (userPings != null && userPings.Count >= 2 && (status == "online" || status == "busy"))
			{
				// Trim to current continuous session: stop at any gap > 5 minutes going backwards
				var sessionPings = new List<GpsPingRecord>();
				for (int i = userPings.Count - 1; i >= 0; i--)
				{
					if (sessionPings.Count == 0)
					{
						sessionPings.Add(userPings[i]);
					}
					else
					{
						var newer = userPings[i + 1].CreatedAt ?? DateTimeOffset.UtcNow;
						var older = userPings[i].CreatedAt ?? DateTimeOffset.UtcNow;
						var gap = (newer - older).TotalMinutes;
						if (gap > 5.0) break;
						sessionPings.Add(userPings[i]);
					}
				}
				sessionPings.Reverse();

				if (sessionPings.Count >= 2)
				{
					var coords = sessionPings.Select(p =>
					{
						var (tx, ty) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
						return new Coordinate(tx, ty);
					}).ToArray();

					var trail = new GeometryFeature { Geometry = new LineString(coords) };
					trail.Styles.Add(new VectorStyle
					{
						Line = new Pen(Mapsui.Styles.Color.FromString("#90CAF9"), 2.0)
					});
					trailFeatures.Add(trail);
				}
			}

			// Dot
			var (x, y) = SphericalMercator.FromLonLat(latestPing.Longitude, latestPing.Latitude);
			var feature = new PointFeature(new MPoint(x, y))
			{
				["user_id"] = userId,
				["label"] = GetLabel(profile.DisplayName, profile.Email, userId),
				["status"] = status,
				["created_at"] = pingCreatedAt
			};

			Mapsui.Styles.Color dotColor = status switch
			{
				"online" => Mapsui.Styles.Color.FromString("#27ae60"),
				"weak"   => Mapsui.Styles.Color.FromString("#f39c12"),
				"stale"  => Mapsui.Styles.Color.FromString("#e74c3c"),
				"busy"   => Mapsui.Styles.Color.FromString("#3498db"),
				_        => Mapsui.Styles.Color.Gray
			};

			double dotScale = status == "offline" ? 0.45 : 0.6;

			// Main dot
			feature.Styles.Add(new SymbolStyle
			{
				Fill = new Brush(dotColor),
				Outline = new Pen(Mapsui.Styles.Color.White, 3.0),
				SymbolScale = dotScale
			});

			// Name label
			string label = GetLabel(profile.DisplayName, profile.Email, userId);
			feature.Styles.Add(new LabelStyle
			{
				Text = label,
				ForeColor = Mapsui.Styles.Color.Black,
				BackColor = new Brush(new Mapsui.Styles.Color(255, 255, 255, 220)),
				HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
				VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Bottom,
				Offset = new Offset(0.0, -26.0),
				Font = new Mapsui.Styles.Font { Size = 11.0, Bold = false },
				Halo = new Pen(Mapsui.Styles.Color.White, 2.0),
				LineHeight = 1.3f
			});

			dotFeatures.Add(feature);

			// Stale notification — engineer punched in but GPS gone dark
			if (status == "stale" && !staleNotifiedIds.Contains(userId))
			{
				staleNotifiedIds.Add(userId);
				try
				{
					await notifications.Send(new Notification
					{
						Id = 4001,
						Title = "Engineer Offline",
						Message = $"{label} has been punched in but hasn't sent a GPS ping for over 30 minutes."
					});
				}
				catch { }
			}
		}

trailLayer.Features = trailFeatures;
			trailLayer.DataHasChanged();
			allUsersLayer.Features = dotFeatures;
			allUsersLayer.DataHasChanged();

			EngineerSummary = totalEngineers == 0
				? "No engineers"
				: busyCount > 0
					? $"{activeCount} active · {busyCount} on job · {totalEngineers - activeCount - busyCount} offline"
					: $"{activeCount} active · {totalEngineers - activeCount} offline";
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Admin map refresh failed: " + ex.Message);
		}
		finally { IsRefreshing = false; }
	}

	// ── Engineer utilities ────────────────────────────────────────────────────

	public async Task AssignJobToUser(string userId)
	{
		if (RequestJobDetails == null) return;
		string shortId = userId.Substring(0, Math.Min(8, userId.Length));
		var result = await RequestJobDetails(shortId);
		if (result.HasValue)
		{
			User? currentUser = supabase.Auth.CurrentUser;
			if (currentUser != null)
			{
				await supabase.From<DispatchJobRecord>().Insert(new DispatchJobRecord
				{
					EngineerUserId = userId,
					AdminUserId = currentUser.Id ?? string.Empty,
					Title = result.Value.Title,
					Description = result.Value.Description,
					Status = "Pending",
					CreatedAt = DateTime.UtcNow
				});
				await Dialogs.Alert("Job assigned successfully.", "Done");
			}
		}
	}

	private void UpdateOwnPin(double lat, double lon, bool centerMap)
	{
		var (x, y) = SphericalMercator.FromLonLat(lon, lat);
		lastKnownPoint = new MPoint(x, y);
		ownLayer.Features = new[] { new PointFeature(lastKnownPoint) };
		ownLayer.DataHasChanged();
		if (centerMap)
			Map.Navigator.CenterOnAndZoomTo(lastKnownPoint, Map.Navigator.Resolutions[16], -1L);
	}

	private async Task<bool> CheckIsAdmin()
	{
		try
		{
			if (supabase.Auth.CurrentUser == null) return false;
			return (await supabase.From<ProfileRecord>()
				.Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, supabase.Auth.CurrentUser.Id)
				.Single())?.IsAdmin ?? false;
		}
		catch { return false; }
	}

	private static string GetLabel(string? displayName, string? email, string userId)
	{
		if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
		if (!string.IsNullOrWhiteSpace(email)) return email.Trim();
		return userId.Length < 8 ? userId : "User …" + userId.Substring(userId.Length - 6);
	}

	private static string RelativeTime(DateTime dt)
	{
		if (dt == DateTime.MinValue) return "Unknown";
		TimeSpan ts = DateTime.UtcNow - dt.ToUniversalTime();
		if (ts.TotalSeconds < 60) return "just now";
		if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes} min ago";
		if (ts.TotalHours < 24) return $"{(int)ts.TotalHours}h ago";
		return $"{(int)ts.TotalDays}d ago";
	}
}
