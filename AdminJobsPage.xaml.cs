namespace GpsSync;

public partial class AdminJobsPage : ContentPage
{
    public AdminJobsPage()
    {
        this.InitializeComponent();
    }

    protected override bool OnBackButtonPressed()
    {
        if (Application.Current?.MainPage is NavigationPage nav) _ = nav.PopToRootAsync();
        return true;
    }
}
