namespace GpsSync;

public partial class NetworkLogPage : ContentPage
{
	public NetworkLogPage()
	{
		this.InitializeComponent();
	}

	protected override bool OnBackButtonPressed()
	{
		_ = Navigation.PopToRootAsync();
		return true;
	}
}
