namespace GpsSync;

public partial class JobLogPage : ContentPage
{
	public JobLogPage()
	{
		this.InitializeComponent();
	}

	protected override bool OnBackButtonPressed()
	{
		_ = Navigation.PopToRootAsync();
		return true;
	}
}
