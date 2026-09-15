using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Shiny;
using Shiny.Hosting;
using Shiny.Net;
using IConnectivity = Shiny.Net.IConnectivity;

namespace GpsSync.Controls;

public partial class ConnectivityView : ContentView
{
	private IDisposable? sub;

	public static readonly BindableProperty OfflineTextProperty = BindableProperty.Create("OfflineText", typeof(string), typeof(ConnectivityView), "There is no internet connection detected");

	public string OfflineText
	{
		get
		{
			return (string)GetValue(OfflineTextProperty);
		}
		set
		{
			SetValue(OfflineTextProperty, value);
		}
	}

	public ConnectivityView()
	{
		InitializeComponent();
		base.Loaded += delegate
		{
			IConnectivity requiredService = Host.Current.Services.GetRequiredService<IConnectivity>();
			SetInternet(requiredService);
			sub = requiredService.WhenChanged().SubOnMainThread(delegate(IConnectivity x)
			{
				SetInternet(x);
			});
		};
		base.Unloaded += delegate
		{
			sub?.Dispose();
		};
	}

	private void SetInternet(IConnectivity conn)
	{
		bool flag = conn.IsInternetAvailable();
		base.IsVisible = !flag;
	}
}
