#if CVS_RECORDED_RESPONSES
using System;
using System.IO;
using ComicApiPlugin.Fixtures;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The in-app smoke test's switch. Compiled only with -p:CvsRecordedResponses=true, so a normal build cannot turn it on.
/// CVS_NET_RECORDED_RESPONSES names a folder: the plugin answers every Comic Vine request from its "fixtures" folder,
/// keeps its settings in its "settings" folder, and appends each request no fixture answered to "unrecorded.txt".
/// </summary>
internal static class RecordedResponses
{
	public const string Variable = "CVS_NET_RECORDED_RESPONSES";

	public static PluginPorts? FromEnvironment()
	{
		string? folder = Environment.GetEnvironmentVariable(Variable);
		if (string.IsNullOrEmpty(folder))
		{
			return null;
		}
		var responses = FixtureFiles.ReadFolder(Path.Combine(folder, "fixtures"));
		string unrecorded = Path.Combine(folder, "unrecorded.txt");
		return new PluginPorts(
			() => new FixtureHandler(responses, key =>
			{
				File.AppendAllText(unrecorded, key + Environment.NewLine);
				return FixtureHandler.NothingFound(key);
			}),
			Path.Combine(folder, "settings"),
			SystemClock.Instance);
	}
}
#endif
