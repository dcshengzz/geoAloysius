namespace GpsSync;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        this.InitializeComponent();
    }

    protected override bool OnBackButtonPressed()
    {
        if (Application.Current?.MainPage is NavigationPage nav) _ = nav.PopToRootAsync();
        return true;
    }
}
