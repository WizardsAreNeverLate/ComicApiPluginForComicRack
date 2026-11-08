using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;

namespace ComicVineScraper.ComicVine;

/// <summary>
/// The per-run port of db.py and cvdb.py: search cleanup, paging, the ignore filters, and the caches. A run starts
/// cold: searches (cleared when they pass 10 entries), the issues of the last series, and every volume's details.
/// Results keep Comic Vine's order. A cancelled query throws and caches nothing.
/// </summary>
public sealed class ComicVineCatalog(ComicVineHttp http, SearchSettings settings) : IComicVine
{
	private const int SearchCacheLimit = 10;

	private readonly object gate = new();
	private readonly Dictionary<string, IReadOnlyList<SeriesRef>> searches = new(StringComparer.Ordinal);
	private readonly Dictionary<SeriesId, SeriesDetails> seriesDetails = [];
	private (SeriesId Series, IReadOnlyList<IssueRef> Issues)? lastIssues;

	public async Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct)
	{
		string stripped = StripIgnoredTerms(terms, settings.IgnoredSearchTerms);
		IReadOnlyList<SeriesRef>? found;
		lock (gate)
		{
			searches.TryGetValue(stripped, out found);
		}
		if (found is null)
		{
			found = await QuerySeriesAsync(stripped, progress, ct).ConfigureAwait(false);
			lock (gate)
			{
				if (searches.Count > SearchCacheLimit)
				{
					searches.Clear();
				}
				searches[stripped] = found;
			}
		}
		return Pending3a.FilterSeries(found, settings);
	}

	public async Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct)
	{
		lock (gate)
		{
			if (lastIssues is { } cached && cached.Series == series)
			{
				return cached.Issues;
			}
		}
		IReadOnlyList<IssueRef> issues = await QueryIssuesAsync(series, progress, ct).ConfigureAwait(false);
		lock (gate)
		{
			lastIssues = (series, issues);
		}
		return issues;
	}

	public async Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct)
	{
		string number = issueNumber;
		if (IssueNumbers.ForQuery(number) is not { } query)
		{
			return null;
		}
		ResultPage<IssueRef> page = ComicVineXml.ParseIssueList(await http.GetAsync(ApiRequest.IssueByNumber(series, query), ct).ConfigureAwait(false));
		for (int attempt = 1; page.Total == 0 && attempt <= 3; attempt++)
		{
			string alternate = IssueNumbers.Alternate(number);
			if (alternate == number)
			{
				break;
			}
			number = alternate;
			ApiRequest request = ApiRequest.IssueByNumber(series, IssueNumbers.ForQuery(number)!);
			page = ComicVineXml.ParseIssueList(await http.GetAsync(request, ct).ConfigureAwait(false));
		}
		return page.Total == 1 ? page.Items.FirstOrDefault() : null;
	}

	/// <param name="includeRating">Python's slow_data: Comic Vine sends no rating, so this reads the alternate covers, and Rating stays null.</param>
	public async Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct)
	{
		ApiRequest request = ApiRequest.IssueDetail(issue);
		XDocument doc = await http.GetAsync(request, ct).ConfigureAwait(false);
		if (ComicVineXml.IssueVolume(doc) is not { } series)
		{
			throw new ComicVineException(ComicVineError.Network, http.Redact(request.ToUrl("")), null, null);
		}
		SeriesDetails details = await GetSeriesDetailsAsync(series, ct).ConfigureAwait(false);
		return ComicVineXml.ParseIssue(doc, issue, series, details, alternateCovers: includeRating);
	}

	/// <summary>Null when Comic Vine does not know the id. Python swallowed every failure here; a bad key or a rate limit now ends the run instead.</summary>
	public async Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct)
	{
		SeriesId? series = target switch
		{
			ComicVineTarget.OfSeries s => s.Id,
			ComicVineTarget.OfIssue i => await SeriesOfIssueAsync(i.Id, ct).ConfigureAwait(false),
			_ => null,
		};
		return series is { } id ? await SeriesByIdAsync(id, ct).ConfigureAwait(false) : null;
	}

	public async Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct) =>
		await http.GetImageAsync(url, ct).ConfigureAwait(false) is { } image ? Pending3a.StripBackCover(image) : null;

	/// <summary>db.query_series_refs: remove whole-word ignored terms. Only terms made of letters and digits count.</summary>
	internal static string StripIgnoredTerms(string terms, IReadOnlyList<string> ignored)
	{
		string[] usable = ignored.Where(term => term.Length > 0 && term.All(char.IsLetterOrDigit)).ToArray();
		return usable.Length == 0 ? terms : Regex.Replace(terms, @"(?i)\b(" + string.Join("|", usable) + @")\b", "");
	}

	/// <summary>cvdb._query_series_refs: a Comic Vine URL first, then the cleaned terms, then the number-word variant.</summary>
	private async Task<IReadOnlyList<SeriesRef>> QuerySeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct)
	{
		string cleaned = Pending3a.CleanSearchTerms(terms, alternate: false);
		if (cleaned.Length == 0)
		{
			return [];
		}
		if (await SeriesFromUrlAsync(terms, ct).ConfigureAwait(false) is { } named)
		{
			return [named];
		}
		IReadOnlyList<SeriesRef> found = await SearchPagesAsync(cleaned, progress, ct).ConfigureAwait(false);
		if (found.Count == 0)
		{
			string alternate = Pending3a.CleanSearchTerms(cleaned, alternate: true);
			if (alternate != cleaned)
			{
				found = await SearchPagesAsync(alternate, progress, ct).ConfigureAwait(false);
			}
		}
		return found;
	}

	/// <summary>An issue id that resolves becomes its series id; otherwise the text's own series id, if any, is tried.</summary>
	private async Task<SeriesRef?> SeriesFromUrlAsync(string text, CancellationToken ct)
	{
		IReadOnlyList<ComicVineTarget> targets = ComicVineUrl.Targets(text);
		SeriesId? series = null;
		if (targets.OfType<ComicVineTarget.OfIssue>().FirstOrDefault() is { } issue)
		{
			series = await SeriesOfIssueAsync(issue.Id, ct).ConfigureAwait(false);
		}
		series ??= targets.OfType<ComicVineTarget.OfSeries>().FirstOrDefault()?.Id;
		return series is { } id ? await SeriesByIdAsync(id, ct).ConfigureAwait(false) : null;
	}

	private async Task<SeriesId?> SeriesOfIssueAsync(IssueId issue, CancellationToken ct) =>
		await GetOrNullAsync(ApiRequest.IssueDetail(issue), ct).ConfigureAwait(false) is { } doc ? ComicVineXml.ParseIssueSeries(doc) : null;

	private async Task<SeriesRef?> SeriesByIdAsync(SeriesId series, CancellationToken ct) =>
		await GetOrNullAsync(ApiRequest.SeriesDetail(series), ct).ConfigureAwait(false) is { } doc ? ComicVineXml.ParseSeries(doc) : null;

	private async Task<XDocument?> GetOrNullAsync(ApiRequest request, CancellationToken ct)
	{
		try
		{
			return await http.GetAsync(request, ct).ConfigureAwait(false);
		}
		catch (ComicVineException e) when (e.IsRecoverable)
		{
			return null;
		}
	}

	/// <summary>
	/// cvdb.__query_series_refs. Progress reports (results read so far, total / 100) once a second page is needed.
	/// Paging stops at MaxSearchResults. An empty first page means no results even when the total says otherwise
	/// (Python bug 329); an empty later page is skipped (Python issues 33 and 396).
	/// </summary>
	private async Task<IReadOnlyList<SeriesRef>> SearchPagesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct)
	{
		var found = new Accumulator<SeriesRef, SeriesId>(series => series.Id, settings.MaxSearchResults);
		if (terms.Trim().Length == 0)
		{
			return found.Items;
		}
		ResultPage<SeriesRef> page = ComicVineXml.ParseSeriesSearch(await http.GetAsync(ApiRequest.SeriesSearch(terms, 1), ct).ConfigureAwait(false));
		int total = page.RawCount == 0 ? 0 : page.Total;
		if (total <= 0)
		{
			return found.Items;
		}
		found.AddRange(page.Items);
		int read = ApiRequest.PageSize;
		if (page.RawCount > 1 && read < total)
		{
			int expectedReports = total / ApiRequest.PageSize;
			progress?.Report(new QueryProgress(read, expectedReports));
			while (read < total && !found.IsFull)
			{
				ct.ThrowIfCancellationRequested();
				page = ComicVineXml.ParseSeriesSearch(
					await http.GetAsync(ApiRequest.SeriesSearch(terms, read / ApiRequest.PageSize + 1), ct).ConfigureAwait(false));
				read += ApiRequest.PageSize;
				progress?.Report(new QueryProgress(read, expectedReports));
				if (page.PageResults is >= 1 && page.RawCount > 0)
				{
					found.AddRange(page.Items);
				}
			}
		}
		return found.Items;
	}

	/// <summary>cvdb._query_issue_refs. Progress reports (issues read so far, total); Done / Total is Python's fraction.</summary>
	private async Task<IReadOnlyList<IssueRef>> QueryIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct)
	{
		var found = new Accumulator<IssueRef, IssueId>(issue => issue.Id, int.MaxValue);
		ResultPage<IssueRef> page = ComicVineXml.ParseIssueList(await http.GetAsync(ApiRequest.IssueList(series, 1), ct).ConfigureAwait(false));
		int total = page.Total;
		if (total <= 0)
		{
			return found.Items;
		}
		found.AddRange(page.Items);
		int read = ApiRequest.PageSize;
		if (page.RawCount > 1 && read < total)
		{
			progress?.Report(new QueryProgress(read, total));
			while (read < total)
			{
				ct.ThrowIfCancellationRequested();
				page = ComicVineXml.ParseIssueList(
					await http.GetAsync(ApiRequest.IssueList(series, read / ApiRequest.PageSize + 1), ct).ConfigureAwait(false));
				read += ApiRequest.PageSize;
				progress?.Report(new QueryProgress(read, total));
				if (page.PageResults is >= 1)
				{
					found.AddRange(page.Items);
				}
			}
		}
		return found.Items;
	}

	private async Task<SeriesDetails> GetSeriesDetailsAsync(SeriesId series, CancellationToken ct)
	{
		lock (gate)
		{
			if (seriesDetails.TryGetValue(series, out SeriesDetails cached))
			{
				return cached;
			}
		}
		SeriesDetails details = ComicVineXml.ParseSeriesDetails(await http.GetAsync(ApiRequest.SeriesDetail(series), ct).ConfigureAwait(false));
		lock (gate)
		{
			seriesDetails[series] = details;
		}
		return details;
	}

	/// <summary>Python collected results in a set: each id once, first occurrence kept, up to a limit.</summary>
	private sealed class Accumulator<T, TKey>(Func<T, TKey> key, int limit)
	{
		private readonly HashSet<TKey> seen = [];
		private readonly List<T> items = [];

		public IReadOnlyList<T> Items => items;
		public bool IsFull => items.Count >= limit;

		public void AddRange(IEnumerable<T> page)
		{
			foreach (T item in page)
			{
				if (!IsFull && seen.Add(key(item)))
				{
					items.Add(item);
				}
			}
		}
	}
}
