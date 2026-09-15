using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;

namespace GpsSync;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = (ConfigChanges.Density | ConfigChanges.Orientation | ConfigChanges.ScreenLayout | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode))]
[IntentFilter(new string[] { "SHINY_LOCAL_NOTIFICATION_CLICK" })]
[IntentFilter(new string[] { "android.intent.action.VIEW" }, Categories = new string[] { "android.intent.category.DEFAULT", "android.intent.category.BROWSABLE" }, DataScheme = "gpssync")]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		ClearCorruptedSecureStorage();
		base.OnCreate(savedInstanceState);
		HandleIntent(((Activity)(object)this).Intent);
		// Keep the app process alive in the background so Samsung and other OEMs
		// cannot kill it while the user has not explicitly closed the app.
		PersistentForegroundService.Start(this);
	}

	// When Samsung (or other OEMs) auto-remove an app, Android deletes the Keystore
	// encryption keys but leaves EncryptedSharedPreferences data on disk. On the next
	// install the new key can't decrypt the old keyset, causing AEADBadTagException
	// inside an Android Runnable that bypasses C# try-catch and crashes the app.
	// Fix: before MAUI touches SecureStorage, scan every SharedPreferences file that
	// looks like an EncryptedSharedPreferences file and clear any that fail to open.
	private void ClearCorruptedSecureStorage()
	{
		// EncryptedSharedPreferences always stores its Tink keyset under this key
		const string KeyKeysetMarker = "__androidx_security_crypto_encrypted_prefs_key_keyset__";
		try
		{
			var masterKey = new AndroidX.Security.Crypto.MasterKey.Builder(this)
				.SetKeyScheme(AndroidX.Security.Crypto.MasterKey.KeyScheme.Aes256Gcm)
				.Build();

			var prefsDir = new Java.IO.File(ApplicationInfo!.DataDir + "/shared_prefs");
			if (!prefsDir.Exists() || !prefsDir.IsDirectory) return;

			foreach (var file in prefsDir.ListFiles() ?? [])
			{
				var prefsName = file.Name?.Replace(".xml", string.Empty);
				if (string.IsNullOrEmpty(prefsName)) continue;

				// Skip files that are not EncryptedSharedPreferences
				var rawPrefs = GetSharedPreferences(prefsName, FileCreationMode.Private);
				if (rawPrefs?.Contains(KeyKeysetMarker) != true) continue;

				// Test whether this encrypted file can still be opened with the current key
				try
				{
					AndroidX.Security.Crypto.EncryptedSharedPreferences.Create(
						this, prefsName, masterKey,
						AndroidX.Security.Crypto.EncryptedSharedPreferences.PrefKeyEncryptionScheme.Aes256Siv,
						AndroidX.Security.Crypto.EncryptedSharedPreferences.PrefValueEncryptionScheme.Aes256Gcm);
				}
				catch
				{
					// Stale keyset — wipe the file so the next Create() starts fresh
					rawPrefs.Edit()?.Clear()?.Commit();
				}
			}
		}
		catch { }
	}

	public override void OnBackPressed()
	{
		var navPage = Microsoft.Maui.Controls.Application.Current?.MainPage as Microsoft.Maui.Controls.NavigationPage;
		if (navPage?.Navigation?.NavigationStack?.Count > 1)
		{
			_ = navPage.PopToRootAsync();
			return;
		}

		// Check punch state via the shared AppSettings singleton
		var appSettings = IPlatformApplication.Current?.Services?.GetService<AppSettings>();
		bool isPunchedIn = appSettings?.IsPunchedIn ?? false;

		Android.App.AlertDialog dialog;
		if (isPunchedIn)
		{
			// Punched in — minimize only; do not allow closing while tracking is active
			dialog = new Android.App.AlertDialog.Builder(this)
				.SetTitle("Minimize App")
				.SetMessage("The app will run in the background")
				.SetPositiveButton("Minimize", (s, e) => MoveTaskToBack(true))
				.SetNegativeButton("Back", (s, e) => { })
				.SetCancelable(false)
				.Create();
		}
		else
		{
			// Punched out — offer to fully exit the app
			dialog = new Android.App.AlertDialog.Builder(this)
				.SetTitle("Exit App")
				.SetMessage("Are you sure you want to exit?")
				.SetPositiveButton("Exit", (s, e) =>
				{
					// Stop the persistent background service so nothing keeps the process alive
					PersistentForegroundService.Stop(this);
					// Remove the task from recents and finish the activity
					FinishAndRemoveTask();
					// Kill the process entirely so the next launch is a true cold start
					Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
				})
				.SetNegativeButton("Back", (s, e) => { })
				.SetCancelable(false)
				.Create();
		}

		dialog.Show();
		dialog.GetButton(-1)?.SetTextColor(Android.Graphics.Color.White); // Positive
		dialog.GetButton(-2)?.SetTextColor(Android.Graphics.Color.White); // Negative
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);
		HandleIntent(intent);
	}

	private static void HandleIntent(Intent? intent)
	{
		if (intent?.Data != null)
		{
			string text = intent.Data.ToString();
			if (!string.IsNullOrEmpty(text))
			{
				DeepLinkService.PendingUrl = text;
			}
		}
	}
}
