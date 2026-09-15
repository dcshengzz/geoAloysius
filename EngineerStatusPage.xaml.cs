namespace GpsSync;

public partial class EngineerStatusPage : ContentPage
{
    public EngineerStatusPage(EngineerStatusViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var vm = (EngineerStatusViewModel)BindingContext;

        vm.RequestJobDetails = async (engineerName) =>
        {
            var title = await DisplayPromptAsync(
                "Assign Job",
                $"Assign to {engineerName}:",
                "Assign", "Cancel",
                placeholder: "Job title");
            if (string.IsNullOrWhiteSpace(title)) return null;

            var desc = await DisplayPromptAsync(
                "Assign Job",
                "Description (optional):",
                "Save", "Skip",
                initialValue: "");

            return (title, desc ?? string.Empty);
        };

        vm.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ((EngineerStatusViewModel)BindingContext).RequestJobDetails = null;
        ((EngineerStatusViewModel)BindingContext).OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (Application.Current?.MainPage is NavigationPage nav) _ = nav.PopToRootAsync();
        return true;
    }
}
