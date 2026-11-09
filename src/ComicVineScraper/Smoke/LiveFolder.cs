#if CVS_RECORDED_RESPONSES
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ComicVineScraper.Plugin;

/// <summary>
/// The differential test's switch, in the same switch build as RecordedResponses. CVS_NET_LIVE_FOLDER names the folder that
/// holds the plugin's settings; Comic Vine requests go over the real network, and each request's host and path (never the
/// query, which carries the API key) is appended to "requests.txt" in that folder so the run can count them. Without the
/// variable, the plugin would read and write the user's own %APPDATA%\Comic Vine Scraper.
/// </summary>
internal static class LiveFolder
{
	public const string Variable = "CVS_NET_LIVE_FOLDER";

	public static PluginPorts? FromEnvironment()
	{
		string? folder = Environment.GetEnvironmentVariable(Variable);
		return string.IsNullOrEmpty(folder)
			? null
			: new PluginPorts(() => new CountingHandler(Path.Combine(folder, "requests.txt")), folder, SystemClock.Instance);
	}

	private sealed class CountingHandler(string file) : DelegatingHandler(new HttpClientHandler())
	{
		private static readonly object Gate = new();

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Uri? uri = request.RequestUri;
			if (uri is not null)
			{
				lock (Gate)
				{
					File.AppendAllText(file, uri.Host + uri.AbsolutePath + Environment.NewLine);
				}
			}
			return base.SendAsync(request, cancellationToken);
		}
	}
}
#endif
