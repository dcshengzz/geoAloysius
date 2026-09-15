using System;
using System.Runtime.CompilerServices;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace GpsSync;

public class AppSettings : ReactiveObject
{
	private bool _0024IsPunchedIn;

	private bool _0024IsNotificationsEnabled;

	private bool _0024IsDarkMode;

	private DateTimeOffset? _0024PunchInTime;

	private DateTimeOffset? _0024LastGpsReadingTime;

	private double _0024LastKnownLatitude;

	private double _0024LastKnownLongitude;

	private bool _0024HasLastKnownPosition;

	private bool _0024IsAdmin;

	private DateTime? _0024LastSyncedToServerTime;

	[Reactive]
	public bool IsPunchedIn
	{
		[CompilerGenerated]
		get
		{
			return _0024IsPunchedIn;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024IsPunchedIn, value, "IsPunchedIn");
		}
	}

	[Reactive]
	public bool IsNotificationsEnabled
	{
		[CompilerGenerated]
		get
		{
			return _0024IsNotificationsEnabled;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024IsNotificationsEnabled, value, "IsNotificationsEnabled");
		}
	}

	[Reactive]
	public bool IsDarkMode
	{
		[CompilerGenerated]
		get
		{
			return _0024IsDarkMode;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024IsDarkMode, value, "IsDarkMode");
		}
	}

	[Reactive]
	public DateTimeOffset? PunchInTime
	{
		[CompilerGenerated]
		get
		{
			return _0024PunchInTime;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024PunchInTime, value, "PunchInTime");
		}
	}

	[Reactive]
	public DateTimeOffset? LastGpsReadingTime
	{
		[CompilerGenerated]
		get
		{
			return _0024LastGpsReadingTime;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024LastGpsReadingTime, value, "LastGpsReadingTime");
		}
	}

	[Reactive]
	public double LastKnownLatitude
	{
		[CompilerGenerated]
		get
		{
			return _0024LastKnownLatitude;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024LastKnownLatitude, value, "LastKnownLatitude");
		}
	}

	[Reactive]
	public double LastKnownLongitude
	{
		[CompilerGenerated]
		get
		{
			return _0024LastKnownLongitude;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024LastKnownLongitude, value, "LastKnownLongitude");
		}
	}

	[Reactive]
	public bool HasLastKnownPosition
	{
		[CompilerGenerated]
		get
		{
			return _0024HasLastKnownPosition;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024HasLastKnownPosition, value, "HasLastKnownPosition");
		}
	}

	[Reactive]
	public DateTime? LastSyncedToServerTime
	{
		[CompilerGenerated]
		get
		{
			return _0024LastSyncedToServerTime;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024LastSyncedToServerTime, value, "LastSyncedToServerTime");
		}
	}

	[Reactive]
	public bool IsAdmin
	{
		[CompilerGenerated]
		get
		{
			return _0024IsAdmin;
		}
		[CompilerGenerated]
		set
		{
			this.RaiseAndSetIfChanged(ref _0024IsAdmin, value, "IsAdmin");
		}
	}
}
