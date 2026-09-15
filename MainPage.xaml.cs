using CommunityToolkit.Maui.Alerts;

namespace GpsSync;

public partial class MainPage : ContentPage
{
	private DateTime _lastBackPress = DateTime.MinValue;

	public MainPage()
	{
		InitializeComponent();
	}

	protected override bool OnBackButtonPressed()
	{
		if ((DateTime.Now - _lastBackPress).TotalSeconds < 2)
		{
			Application.Current?.Quit();
			return true;
		}

		_lastBackPress = DateTime.Now;
		Toast.Make("Press back again to exit").Show();
		return true;
	}
}