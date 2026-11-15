#if CVS_RECORDED_RESPONSES
using System;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The differential test's switch, in the same switch build as RecordedResponses. CVS_NET_LIVE_FOLDER names the folder that
/// holds the plugin's settings. Comic Vine requests go over the real network, through the packaged service
/// (RunFolderService). Each request is one line in the service's log with its host and path (never the query, which
/// carries the API key), so the run can count them. Without the variable, the plugin would read and write the user's own
/// %APPDATA%\Comic Vine Scraper.
/// </summary>
internal static class LiveFolder
{
	public const string Variable = "CVS_NET_LIVE_FOLDER";

	public static PluginPorts? FromEnvironment()
	{
		string? folder = Environment.GetEnvironmentVariable(Variable);
		return string.IsNullOrEmpty(folder)
			? null
			: new PluginPorts(RunFolderService.In(folder), folder, SystemClock.Instance);
	}
}
#endif
