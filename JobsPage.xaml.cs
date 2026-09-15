namespace GpsSync;

public partial class JobsPage : ContentPage
{
    public JobsPage()
    {
        this.InitializeComponent();
    }

    protected override bool OnBackButtonPressed()
    {
        if (Application.Current?.MainPage is NavigationPage nav) _ = nav.PopToRootAsync();
        return true;
    }
}
