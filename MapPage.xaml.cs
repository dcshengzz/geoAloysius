using System.ComponentModel;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.UI.Maui;

namespace GpsSync;

public partial class MapPage : ContentPage
{
    public MapPage(MapViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var vm = (MapViewModel)BindingContext;
        MapControl.Map = vm.Map;

        vm.RequestJobDetails = async (engineerLabel) =>
        {
            var title = await DisplayPromptAsync(
                "Assign Job",
                $"Assign to engineer …{engineerLabel}",
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

        MapControl.Info += OnMapInfo;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MapControl.Info -= OnMapInfo;
        ((MapViewModel)BindingContext).PropertyChanged -= OnViewModelPropertyChanged;
        ((MapViewModel)BindingContext).RequestJobDetails = null;
        ((MapViewModel)BindingContext).OnDisappearing();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MapViewModel.IsAdmin)) return;
        var vm = (MapViewModel)BindingContext;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ToolbarItems.Clear();
            if (vm.IsAdmin)
                ToolbarItems.Add(new ToolbarItem { Text = "↻", Command = vm.ManualRefresh });
        });
    }

    protected override bool OnBackButtonPressed()
    {
        if (Application.Current?.MainPage is NavigationPage nav) _ = nav.PopToRootAsync();
        return true;
    }

    void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        var vm = (MapViewModel)BindingContext;

        var feature = e.MapInfo?.Feature;

        // Tapped empty space — dismiss any open callout
        if (feature == null)
        {
            MainThread.BeginInvokeOnMainThread(() => vm.IsCalloutVisible = false);
            return;
        }

        if (!vm.IsAdmin) return;

        var userId = feature["user_id"] as string;
        if (string.IsNullOrEmpty(userId)) return;

        // Show info callout; admin can then tap "Assign Job" inside it
        MainThread.BeginInvokeOnMainThread(() => vm.ShowCallout(feature));
    }
}
