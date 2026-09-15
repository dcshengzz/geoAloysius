using System;
using System.Collections.Generic;
using System.Linq;

namespace GpsSync;

public static class DeepLinkService
{
	public static string? PendingUrl { get; set; }

	public static (string? AccessToken, string? RefreshToken, string? Type) ParseUrl(string url)
	{
		string text = (url.Contains('#') ? url.Split('#')[1] : string.Empty);
		Dictionary<string, string> dictionary = (from p in text.Split('&')
			select p.Split('=') into p
			where p.Length == 2
			select p).ToDictionary((string[] p) => p[0], (string[] p) => Uri.UnescapeDataString(p[1]));
		dictionary.TryGetValue("access_token", out var value);
		dictionary.TryGetValue("refresh_token", out var value2);
		dictionary.TryGetValue("type", out var value3);
		return (AccessToken: value, RefreshToken: value2, Type: value3);
	}
}
