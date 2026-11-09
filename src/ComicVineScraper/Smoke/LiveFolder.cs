#if CVS_RECORDED_RESPONSES
using System;
using System.Net.Http;

namespace ComicVineScraper.Plugin;

/// <summary>
/// The differential test's switch, in the same switch build as RecordedResponses. CVS_NET_LIVE_FOLDER names the folder that
/// holds the plugin's settings; Comic Vine requests go over the real network. Without it, the plugin would read and write
/// the user's own %APPDATA%\Comic Vine Scraper.
/// </summary>
internal static class LiveFolder
{
	public const string Variable = "CVS_NET_LIVE_FOLDER";

	public static PluginPorts? FromEnvironment()
	{
		string? folder = Environment.GetEnvironmentVariable(Variable);
		return string.IsNullOrEmpty(folder)
			? null
			: new PluginPorts(() => new HttpClientHandler(), folder, SystemClock.Instance);
	}
}
#endif
