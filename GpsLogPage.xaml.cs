namespace GpsSync;

public partial class GpsLogPage : ContentPage
{
	public GpsLogPage()
	{
		this.InitializeComponent();
	}

	protected override bool OnBackButtonPressed()
	{
		_ = Navigation.PopToRootAsync();
		return true;
	}
}
