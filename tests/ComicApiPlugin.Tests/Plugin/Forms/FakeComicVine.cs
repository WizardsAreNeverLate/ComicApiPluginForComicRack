using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Tests.Plugin.Forms;

/// <summary>
/// IComicVine for the cover panels. Each image URL names its colour ("https://img/Red.png"), so a test reads the colour
/// the user sees. A URL in Held waits until Release, so a test controls the order loads finish in.
/// </summary>
internal sealed class FakeComicVine : IComicVine
{
	private readonly ConcurrentDictionary<Uri, TaskCompletionSource<bool>> held = new();

	public ConcurrentQueue<Uri> ImageRequests { get; } = new();
	public ConcurrentQueue<Uri> CancelledImages { get; } = new();
	public ConcurrentQueue<(SeriesId, string)> IssueLookups { get; } = new();
	public Dictionary<(SeriesId, string), IssueRef> IssuesByNumber { get; } = [];
	public Dictionary<IssueId, IReadOnlyList<Uri>> CoversByIssue { get; } = [];

	public bool LookupsFail { get; set; }

	public static Uri Url(string colour) => new("https://img/" + colour + ".png");

	public static Color ColourOf(Uri url) => Color.FromName(url.Segments.Last().Replace(".png", ""));

	public void Hold(Uri url) => held[url] = new TaskCompletionSource<bool>();

	public void Release(Uri url) => held[url].TrySetResult(true);

	public async Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct)
	{
		ImageRequests.Enqueue(url);
		if (held.TryGetValue(url, out TaskCompletionSource<bool>? gate))
		{
			using (ct.Register(() =>
			{
				CancelledImages.Enqueue(url);
				gate.TrySetCanceled();
			}))
			{
				await gate.Task;
			}
		}
		var image = new Bitmap(20, 30);
		using Graphics g = Graphics.FromImage(image);
		g.Clear(ColourOf(url));
		return image;
	}

	public Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct)
	{
		IssueLookups.Enqueue((series, issueNumber));
		if (LookupsFail)
		{
			throw new ComicVineException(ComicVineError.Network, "https://comicvine.gamespot.com/api/issues/", null, null);
		}
		return Task.FromResult(IssuesByNumber.TryGetValue((series, issueNumber), out IssueRef? found) ? found : null);
	}

	public Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct) =>
		Task.FromResult(new Issue(issue, new SeriesId(1), "", null, "", "", "", "", "", PartialDate.None, PartialDate.None,
			[], [], [], [], [], [], [], [], [], [], [], "", null, CoversByIssue.TryGetValue(issue, out IReadOnlyList<Uri>? covers) ? covers : []));

	public Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct) => throw new NotSupportedException();

	public Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct) => throw new NotSupportedException();

	public Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct) => throw new NotSupportedException();
}
