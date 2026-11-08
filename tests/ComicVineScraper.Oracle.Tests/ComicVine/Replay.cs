using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Fixtures;

namespace ComicVineScraper.Oracle.Tests.ComicVine;

/// <summary>
/// One set of responses for both sides: the recorded fixtures, plus a fallback so any query can run. An unknown
/// search or issue list finds nothing, and an unknown issue or volume is "Object Not Found" (status 101), as live.
/// </summary>
internal sealed class Replay
{
	private const string EmptyList = "<?xml version=\"1.0\" encoding=\"utf-8\"?><response><error><![CDATA[OK]]></error><limit>100</limit>"
		+ "<offset>0</offset><number_of_page_results>0</number_of_page_results><number_of_total_results>0</number_of_total_results>"
		+ "<status_code>1</status_code><results/><version>1.0</version></response>";

	private const string NotFound = "<?xml version=\"1.0\" encoding=\"utf-8\"?><response><error><![CDATA[Object Not Found]]></error>"
		+ "<limit>0</limit><offset>0</offset><number_of_page_results>0</number_of_page_results><number_of_total_results>0</number_of_total_results>"
		+ "<status_code>101</status_code><results/></response>";

	private static readonly Lazy<IReadOnlyList<RecordedResponse>> Recorded = new(() => FixtureFiles.ReadFolder(FixtureFiles.OutputFolder));
	private static readonly Lazy<Dictionary<string, RecordedResponse>> ByKey = new(() => Recorded.Value.ToDictionary(r => FixtureFiles.Key(r.Url)));

	/// <summary>The serve(url) function the Python helper calls.</summary>
	public static readonly Func<string, RecordedResponse?> Serve = url =>
		ByKey.Value.TryGetValue(FixtureFiles.Key(url), out RecordedResponse? found) ? found : Fallback(FixtureFiles.Key(url));

	public static (ComicVineCatalog Catalog, FixtureHandler Handler) Catalog(SearchSettings settings)
	{
		var handler = new FixtureHandler(Recorded.Value, Fallback);
		var clock = new ManualClock();
		return (new ComicVineCatalog(new ComicVineHttp(handler, "oracle", "ua", clock, () => clock.Elapsed), settings), handler);
	}

	/// <summary>The Python side's result: [value, error or null, requested URLs as fixture keys].</summary>
	public static (object? Value, string? Error, string[] Requested) Unpack(object? result)
	{
		var parts = (object?[])result!;
		return (parts[0], (string?)parts[1], ((object[])parts[2]!).Select(url => FixtureFiles.Key((string)url)).ToArray());
	}

	private static RecordedResponse? Fallback(string key) =>
		key.Contains("/api/search/") || key.Contains("/api/issues/") ? new RecordedResponse(key, HttpStatusCode.OK, EmptyList)
		: key.Contains("/api/issue/4000-") || key.Contains("/api/volume/4050-") ? new RecordedResponse(key, HttpStatusCode.OK, NotFound)
		: null;
}
