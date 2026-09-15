using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace GpsSync;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		ApplySavedTheme();
	}

	private void ApplySavedTheme()
	{
		bool isDark = Preferences.Default.Get("app.darkmode", defaultValue: false);
		UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
	}
}

