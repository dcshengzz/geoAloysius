namespace GpsSync;

/// <summary>
/// App configuration. Holds the SQL Server backend API base URL.
///
/// The URL switches automatically by build configuration:
///   • Debug   (F5 from Visual Studio) -> the LOCAL dev API.
///   • Release (the APK you hand to engineers) -> the CLOUD API over HTTPS.
/// So you never hand-edit the URL between "developing" and "shipping".
/// </summary>
public static class AppConfig
{
#if DEBUG
    // ---- LOCAL DEV ----
    // Android emulator reaches the API running on the SAME PC via 10.0.2.2 (not localhost).
    // A physical dev phone on the same Wi-Fi uses the PC's LAN IP instead (e.g. http://192.168.1.50:5080).
    public const string ApiBaseUrl = "http://10.0.2.2:5080";
#else
    // ---- CLOUD / PRODUCTION ----
    // The API served over HTTPS by the Docker Compose stack (Caddy) on your server.
    // MUST match SITE_ADDRESS from server/.env, with https:// and no trailing slash.
    //   e.g. "https://api.yourcompany.com"  or  "https://34-87-120-9.sslip.io"
    // >>> EDIT THIS to your deployed address before building the release APK. <<<
    public const string ApiBaseUrl = "https://api.yourcompany.com";
#endif
}
