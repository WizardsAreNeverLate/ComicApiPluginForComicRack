#if CVS_RECORDED_RESPONSES
using System;
using System.Collections.Generic;
using System.IO;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Service;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The in-app smoke test's switch. Compiled only with -p:CvsRecordedResponses=true, so a normal build cannot turn it on.
/// CVS_NET_RECORDED_RESPONSES names a folder. The plugin starts a fake Comic Vine on a loopback port, which answers
/// from the folder's "fixtures" folder and appends each request no fixture answered to "unrecorded.txt". It starts the
/// packaged service (RunFolderService) with both providers' base URLs set to that fake, so no request leaves the
/// machine. The plugin keeps its settings in the folder's "settings" folder.
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
		string unrecorded = Path.Combine(folder, "unrecorded.txt");
		var gate = new object();
		// Never disposed: it serves the one plugin instance until ComicRack exits.
		FakeComicVineServer comicVine = FakeComicVineServer.FromFixtures(FixtureFiles.ReadFolder(Path.Combine(folder, "fixtures")), ownImages: true, missed: key =>
		{
			lock (gate)
			{
				File.AppendAllText(unrecorded, key + Environment.NewLine);
			}
		});
		ServiceLaunch service = RunFolderService.In(folder) with
		{
			Environment = new Dictionary<string, string>
			{
				[ServiceProtocol.ComicVineBaseUrlVariable] = comicVine.BaseUrl,
				[ServiceProtocol.VerseDbBaseUrlVariable] = $"http://127.0.0.1:{comicVine.Port}",
			},
		};
		return new PluginPorts(service, Path.Combine(folder, "settings"), SystemClock.Instance);
	}
}
#endif
