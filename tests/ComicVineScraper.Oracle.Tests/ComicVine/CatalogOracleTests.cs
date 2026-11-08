using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Config;
using ComicVineScraper.Engine;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Model;
using TUnit.Assertions.Enums;

namespace ComicVineScraper.Oracle.Tests.ComicVine;

/// <summary>
/// ComicVineCatalog against the original db.py and cvdb.py, both replaying the same responses (Replay). Each test
/// compares the results, the progress reports, and the exact sequence of requests. Python returns sets, so results
/// are compared without order; the C# order (Comic Vine's) is pinned by the unit tests.
/// </summary>
public sealed class CatalogOracleTests
{
	// deviations.md, "Comic Vine ids in text": C# sends an id without leading zeros (Comic Vine answers both alike, see the
	// series-00796-padded fixture) and reads only ASCII digits. Results are still compared for these inputs; requests are not.
	private static readonly Regex IdSentDifferently = new(@"\b(?:4000|49|4050)-(?:0\d|\d*[^\x00-\x7F])");

	private static readonly string[] FixedTerms =
	[
		"Batman", "batman", "The Batman", "BATMAN  ", "  spaced   out  ", "batman\tbeyond", "batman\n",
		"x-men & avengers", "spider-man c2c noads", "spider-man noadss tbp ctc", "amazing spider-man 2nd series",
		"fantastic 4", "Fantastic 4 c2c", "Fantastic Four", "fantastic four", "thirteenth floor", "1st issue", "twelfth night",
		"eightteenth", "the 20th century", "4 5 6", "zero", "one two three",
		"4050-796", "cv 4000-37566", "https://comicvine.gamespot.com/batman/4050-796/", "4000-999999999 or 4050-796",
		"4000-12 4050-34", "49-796", "4050-7", "x4050-796", "4050-796x", "4050-00796", "4000-37566\nline two",
		"x 4050-١٢٣", "4000-١٢٣ 4050-796",
		"ünïcödé", "a.b:c'd-e", "#1 (of 4)", "", "   ", "c2c", "tbp", "&", " & ", "a&b",
		"edgecases", "emptypage", "bugthreetwonine", "bprd vampire", "preacher", "zzqxv qqzzx", "Ⅻ", "١٢",
		"the of vol 2", "ünï batman", "batman of the bat",
	];

	private static readonly string[] Tokens =
	[
		"batman", "Batman", "the", "&", "c2c", "noads", "2nd", "second", "4", "four", "x-men", "4050-796", "4000-37566",
		"edgecases", "emptypage", "fantastic", "bprd", "vampire", "preacher", "Vol.", "#1", "(1990)", "ünï", "-", "'s", ":",
		".", "  ", "\t", "of", "13th", "twelveth",
	];

	private static readonly SearchSettings[] SettingsCorpus =
	[
		new([], [], 0, 9999, 9999, 100),
		new(["the", "of", "vol 2", " spaced", "ünï", "BAT", ""], ["dc comics", "vertigo", "image"], 1960, 2015, 50, 200),
		new([], ["marvel", "editorial novaro"], 0, 9999, 100, 1000),
	];

	[Test]
	public async Task Search_matches_query_series_refs_and_filter_series_refs()
	{
		var random = new Random(20261109);
		string[] generated = Enumerable.Range(0, 120)
			.Select(_ => string.Join(random.Next(4) == 0 ? "" : " ", Enumerable.Range(0, random.Next(1, 5)).Select(_ => Tokens[random.Next(Tokens.Length)])))
			.ToArray();
		var mismatches = new List<string>();
		int compared = 0;
		foreach (SearchSettings settings in SettingsCorpus)
		{
			foreach (string terms in FixedTerms.Concat(generated))
			{
				compared++;
				mismatches.AddRange(await CompareSearch(terms, settings));
			}
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
		await Assert.That(compared).IsEqualTo(3 * (FixedTerms.Length + 120));
	}

	[Test]
	public async Task Issue_lists_match_query_issue_refs()
	{
		var mismatches = new List<string>();
		foreach (int series in new[] { 91273, 51658, 796, 9999, 123456 })
		{
			(object? value, string? pythonError, string[] pythonRequests) = Replay.Unpack(Oracle.Call("cvoracle", "issues", Replay.Serve, series));
			(ComicVineCatalog catalog, var handler) = Replay.Catalog(SettingsCorpus[0]);
			var progress = new Recorder();
			IReadOnlyList<IssueRef> issues = await catalog.GetIssuesAsync(new SeriesId(series), progress, CancellationToken.None);

			var python = (object[])value!;
			Compare(mismatches, $"issues {series}", Set(((object[])python[0]).Select(FormatIssueRef)), Set(issues.Select(FormatIssueRef)));
			Compare(mismatches, $"issues {series} progress", ((object[])python[1]).Select(f => Convert.ToDouble(f, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)),
				progress.Reports.Select(p => ((double)p.Done / p.Total).ToString("R", CultureInfo.InvariantCulture)));
			Compare(mismatches, $"issues {series} requests", pythonRequests, handler.Requested);
			if (pythonError is not null)
			{
				mismatches.Add($"issues {series}: Python raised {pythonError}");
			}
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
	}

	[Test]
	public async Task An_issue_list_whose_first_page_is_empty_crashes_Python_and_is_empty_in_CSharp()
	{
		(_, string? pythonError, _) = Replay.Unpack(Oracle.Call("cvoracle", "issues", Replay.Serve, 9997));
		(ComicVineCatalog catalog, _) = Replay.Catalog(SettingsCorpus[0]);

		IReadOnlyList<IssueRef> issues = await catalog.GetIssuesAsync(new SeriesId(9997), null, CancellationToken.None);

		await Assert.That(pythonError).StartsWith("AttributeError");
		await Assert.That(issues).IsEmpty();
	}

	[Test]
	public async Task Find_issue_matches_query_issue_ref_outside_the_fraction_bugs()
	{
		var random = new Random(411);
		const string Alphabet = "0000.55½27x A";
		string[] numbers =
		[
			"0.5", ".5", "00.50", "½", "0½", "497", "00497", " 007 ", "0", "000", "7", "1", "1 A", "1A", "-1", "1/2", "15", "5.5", "5.25", "x5",
			.. Enumerable.Range(0, 150).Select(_ => new string(Enumerable.Range(0, random.Next(1, 6)).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToArray())),
		];
		var mismatches = new List<string>();
		int skipped = 0;
		foreach (int series in new[] { 9999, 796 })
		{
			foreach (string number in numbers.Distinct())
			{
				if (IsBlankIssueNumber(number) || PythonFractionBug(number))
				{
					skipped++;
					continue;
				}
				(object? value, string? pythonError, string[] pythonRequests) = Replay.Unpack(Oracle.Call("cvoracle", "find_issue", Replay.Serve, series, number));
				(ComicVineCatalog catalog, var handler) = Replay.Catalog(SettingsCorpus[0]);
				IssueRef? found = await catalog.FindIssueAsync(new SeriesId(series), number, CancellationToken.None);

				string label = $"find {series} '{number}'";
				Compare(mismatches, label, [value is null ? "none" : FormatIssueRef(value)], [found is null ? "none" : FormatIssueRef(found)]);
				Compare(mismatches, label + " requests", pythonRequests, handler.Requested);
				if (pythonError is not null)
				{
					mismatches.Add($"{label}: Python raised {pythonError}");
				}
			}
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
		await Assert.That(skipped).IsLessThan(numbers.Length);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task Issue_details_match_query_issue(bool slowData)
	{
		int[][] runs = [[37566], [535328], [9000001], [9000002], [37566, 37566, 535328], [9000001, 9000002], [999999999]];
		var mismatches = new List<string>();
		int surrogateFields = 0;
		int objectNameFields = 0;
		foreach (int[] ids in runs)
		{
			(object? value, string? pythonError, string[] pythonRequests) =
				Replay.Unpack(Oracle.Call("cvoracle", "issue_details", Replay.Serve, ids, slowData));
			(ComicVineCatalog catalog, var handler) = Replay.Catalog(SettingsCorpus[0]);
			var issues = new List<Issue>();
			string? error = null;
			try
			{
				foreach (int id in ids)
				{
					issues.Add(await catalog.GetIssueAsync(new IssueId(id), slowData, CancellationToken.None));
				}
			}
			catch (ComicVineException e)
			{
				error = $"{e.Error} {e.ApiStatus}";
			}

			string label = $"issues [{string.Join(",", ids)}] slow={slowData}";
			Compare(mismatches, label + " error", [pythonError is null ? "none" : "raised"], [error is null ? "none" : "raised"]);
			Compare(mismatches, label + " requests", pythonRequests, handler.Requested);
			if (value is object[] pythonIssues)
			{
				// deviations.md #8: Python drops emoji and other surrogate pairs, so a field where C# keeps one is not compared.
				// deviations.md #15: Python lists an empty <name/> credit as "<name object at 0x...>", so such a field is not compared.
				string[] csharpFields = issues.SelectMany(FormatIssue).ToArray();
				string[] pythonFields = pythonIssues.SelectMany(i => FormatIssue((object[])i)).ToArray();
				bool[] hasPair = csharpFields.Select(HasSurrogatePair).ToArray();
				bool[] hasObjectName = pythonFields.Select(field => ObjectName.IsMatch(field)).ToArray();
				bool[] compared = csharpFields.Select((_, at) => !hasPair[at] && !(at < hasObjectName.Length && hasObjectName[at])).ToArray();
				surrogateFields += hasPair.Count(c => c);
				objectNameFields += hasObjectName.Count(c => c);
				Compare(mismatches, label, pythonFields.Where((_, at) => at >= compared.Length || compared[at]), csharpFields.Where((_, at) => compared[at]));
			}
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
		await Assert.That(surrogateFields).IsEqualTo(2);
		await Assert.That(objectNameFields).IsEqualTo(2);
	}

	[Test]
	public async Task Cvinfo_files_match_check_magic_file()
	{
		string[] contents =
		[
			"https://comicvine.gamespot.com/batman/4050-796/", "4050-796", "  4050-796\r\n", "\uFEFF4050-796", "49-796", "4000-37566",
			"4000-999999999", "4050-123456", "4050-7", "batman", "", "4050-796\n4050-91273", "x 4000-37566 y", "4050-9998",
		];
		var mismatches = new List<string>();
		string root = Path.Combine(Path.GetTempPath(), "cvs-oracle-cvinfo-" + Guid.NewGuid().ToString("N"));
		try
		{
			for (int i = 0; i < contents.Length; i++)
			{
				foreach (string name in new[] { "cvinfo.txt", "cvinfo" })
				{
					string folder = Path.Combine(root, i + name);
					Directory.CreateDirectory(folder);
					File.WriteAllText(Path.Combine(folder, name), contents[i], new UTF8Encoding(false));
					string book = Path.Combine(folder, "Book 1.cbz");
					foreach (string path in new[] { book, folder })
					{
						(object? value, string? pythonError, string[] pythonRequests) = Replay.Unpack(Oracle.Call("cvoracle", "check_magic_file", Replay.Serve, path));
						(ComicVineCatalog catalog, var handler) = Replay.Catalog(SettingsCorpus[0]);
						SeriesRef? series = ComicVineUrl.FindCvInfo(path) is { } target ? await catalog.ResolveSeriesAsync(target, CancellationToken.None) : null;

						string label = $"cvinfo '{contents[i]}' in {name} via {(path == book ? "book" : "folder")}";
						Compare(mismatches, label, [value is null ? "none" : FormatSeries(value)], [series is null ? "none" : FormatSeries(series)]);
						Compare(mismatches, label + " requests", pythonRequests, handler.Requested);
						if (pythonError is not null)
						{
							mismatches.Add($"{label}: Python raised {pythonError}");
						}
					}
				}
			}
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
	}

	/// <summary>deviations.md, "cvinfo with two ids": FindCvInfo returns one target, so the series id is not tried as a fallback.</summary>
	[Test]
	public async Task A_cvinfo_naming_an_unknown_issue_and_a_series_falls_back_to_the_series_in_Python_only()
	{
		string folder = Path.Combine(Path.GetTempPath(), "cvs-oracle-cvinfo-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		try
		{
			File.WriteAllText(Path.Combine(folder, "cvinfo"), "4000-999999999 4050-796");
			(object? value, _, _) = Replay.Unpack(Oracle.Call("cvoracle", "check_magic_file", Replay.Serve, folder));
			(ComicVineCatalog catalog, _) = Replay.Catalog(SettingsCorpus[0]);

			ComicVineTarget? target = ComicVineUrl.FindCvInfo(folder);
			SeriesRef? series = await catalog.ResolveSeriesAsync(target!, CancellationToken.None);

			await Assert.That(FormatSeries(value!)).StartsWith("796|Batman|");
			await Assert.That(target).IsEqualTo(new ComicVineTarget.OfIssue(new IssueId(999999999)));
			await Assert.That(series).IsNull();
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Test]
	public async Task A_cancelled_query_is_cached_as_empty_by_Python_and_not_cached_by_CSharp()
	{
		var cancelThenRepeat = (object[])Oracle.Call("cvoracle", "cancel_then_repeat", Replay.Serve, "batman", 91273)!;
		(ComicVineCatalog catalog, _) = Replay.Catalog(SettingsCorpus[2]);
		using var cancel = new CancellationTokenSource();
		var cancelling = new Recorder(_ => cancel.Cancel());
		await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.SearchSeriesAsync("batman", cancelling, cancel.Token));
		using var cancelIssues = new CancellationTokenSource();
		await Assert.ThrowsAsync<OperationCanceledException>(
			() => catalog.GetIssuesAsync(new SeriesId(91273), new Recorder(_ => cancelIssues.Cancel()), cancelIssues.Token));

		IReadOnlyList<SeriesRef> series = await catalog.SearchSeriesAsync("batman", null, CancellationToken.None);
		IReadOnlyList<IssueRef> issues = await catalog.GetIssuesAsync(new SeriesId(91273), null, CancellationToken.None);

		await Assert.That(((object[])cancelThenRepeat[0]).Cast<int>()).IsEquivalentTo(new[] { 0, 0 });
		await Assert.That(((object[])cancelThenRepeat[1]).Cast<int>()).IsEquivalentTo(new[] { 0, 0 });
		await Assert.That(series.Count).IsEqualTo(200);
		await Assert.That(issues.Count).IsEqualTo(163);
	}

	/// <summary>deviations.md, "Blank issue number": the one input class Find_issue_matches_query_issue_ref_outside_the_fraction_bugs skips.</summary>
	[Test]
	[Arguments("")]
	[Arguments(" ")]
	[Arguments("\t")]
	[Arguments("\r\n ")]
	public async Task A_blank_issue_number_raises_in_Python_and_finds_nothing_without_a_request_in_CSharp(string number)
	{
		(_, string? pythonError, string[] pythonRequests) = Replay.Unpack(Oracle.Call("cvoracle", "find_issue", Replay.Serve, 796, number));
		(ComicVineCatalog catalog, FixtureHandler handler) = Replay.Catalog(SettingsCorpus[0]);

		IssueRef? found = await catalog.FindIssueAsync(new SeriesId(796), number, CancellationToken.None);

		await Assert.That(pythonError).StartsWith("ValueError");
		await Assert.That(pythonRequests).IsEmpty();
		await Assert.That(found).IsNull();
		await Assert.That(handler.Requested).IsEmpty();
		await Assert.That(IsBlankIssueNumber(number)).IsTrue();
	}

	/// <summary>deviations.md, "Blank issue number": Python's _query_issue_id_dom raises ValueError('bad parameters') once the number is stripped.</summary>
	internal static bool IsBlankIssueNumber(string number) => number.Trim().Length == 0;

	/// <summary>deviations.md 3b-2: Python's int(volume.id) raises on the first volume with a non-numeric id and the whole search fails.</summary>
	[Test]
	public async Task A_search_volume_with_a_non_numeric_id_aborts_Python_and_is_dropped_by_CSharp()
	{
		SearchSettings settings = SettingsCorpus[0];
		(_, string? pythonError, _) = Replay.Unpack(Oracle.Call("cvoracle", "search", Replay.Serve, "badvolid",
			settings.IgnoredSearchTerms.ToArray(), settings.MaxSearchResults, settings.IgnoredPublishers.ToArray(),
			settings.IgnoreBeforeYear, settings.IgnoreAfterYear, settings.NeverIgnoreThreshold));
		(ComicVineCatalog catalog, _) = Replay.Catalog(settings);

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("badvolid", null, CancellationToken.None);

		await Assert.That(pythonError).StartsWith("ValueError");
		await Assert.That(found.Select(s => s.Name)).IsEquivalentTo(new[] { "Before Bad Id", "After Bad Id" }, CollectionOrdering.Matching);
	}

	/// <summary>deviations.md 3b-3: Python writes the publisher of a search volume whose publisher name is empty as sstr(object).</summary>
	[Test]
	public async Task A_search_volume_whose_publisher_name_is_empty_has_an_object_name_in_Python_and_none_in_CSharp()
	{
		SearchSettings settings = SettingsCorpus[0];
		(object? value, string? pythonError, _) = Replay.Unpack(Oracle.Call("cvoracle", "search", Replay.Serve, "emptypubname",
			settings.IgnoredSearchTerms.ToArray(), settings.MaxSearchResults, settings.IgnoredPublishers.ToArray(),
			settings.IgnoreBeforeYear, settings.IgnoreAfterYear, settings.NeverIgnoreThreshold));
		(ComicVineCatalog catalog, _) = Replay.Catalog(settings);

		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync("emptypubname", null, CancellationToken.None);

		var pythonSeries = (object[])((object[])((object[])value!)[0]).Single()!;
		await Assert.That(pythonError).IsNull();
		await Assert.That(ObjectName.IsMatch((string)pythonSeries[3])).IsTrue();
		await Assert.That(found.Single().Publisher).IsEqualTo("");
		await Assert.That(found.Single().Name).IsEqualTo((string)pythonSeries[1]);
	}

	/// <summary>deviations.md 3b-4: Python reads dom.results.issue when the total is 1, and a response with no issue element has none.</summary>
	[Test]
	public async Task An_issue_number_search_with_a_total_of_one_and_no_issue_crashes_Python_and_finds_nothing_in_CSharp()
	{
		(_, string? pythonError, _) = Replay.Unpack(Oracle.Call("cvoracle", "find_issue", Replay.Serve, 9996, "3"));
		(ComicVineCatalog catalog, _) = Replay.Catalog(SettingsCorpus[0]);

		IssueRef? found = await catalog.FindIssueAsync(new SeriesId(9996), "3", CancellationToken.None);

		await Assert.That(pythonError).StartsWith("AttributeError");
		await Assert.That(found).IsNull();
	}

	/// <summary>deviations.md #1 and #2: Python's first test is re.match("0*.50*"), and its replace calls never match.</summary>
	internal static bool PythonFractionBug(string number) =>
		(Regex.IsMatch(number, "^0*.50*") && !Regex.IsMatch(number, @"^0*\.50*$"))
		|| (!Regex.IsMatch(number, @"^0*\.50*$") && number is not "½" and not "0½" && IssueNumbers.Alternate(number) != number);

	private static async Task<IEnumerable<string>> CompareSearch(string terms, SearchSettings settings)
	{
		(object? value, string? pythonError, string[] pythonRequests) = Replay.Unpack(Oracle.Call("cvoracle", "search", Replay.Serve, terms,
			settings.IgnoredSearchTerms.ToArray(), settings.MaxSearchResults, settings.IgnoredPublishers.ToArray(),
			settings.IgnoreBeforeYear, settings.IgnoreAfterYear, settings.NeverIgnoreThreshold));
		(ComicVineCatalog catalog, var handler) = Replay.Catalog(settings);
		var progress = new Recorder();
		IReadOnlyList<SeriesRef> found = await catalog.SearchSeriesAsync(terms, progress, CancellationToken.None);

		var mismatches = new List<string>();
		string label = $"search '{terms.Replace("\n", "\\n").Replace("\t", "\\t")}' max={settings.MaxSearchResults} ignored={settings.IgnoredSearchTerms.Count}";
		if (pythonError is not null)
		{
			mismatches.Add($"{label}: Python raised {pythonError}");
			return mismatches;
		}
		var python = (object[])value!;
		Compare(mismatches, label, Set(((object[])python[0]).Select(FormatSeries)), Set(found.Select(FormatSeries)));
		Compare(mismatches, label + " progress", ((object[])python[1]).Select(call => string.Join("/", (object[])call)),
			progress.Reports.Select(p => $"{p.Done}/{p.Total}"));
		if (!IdSentDifferently.IsMatch(terms))
		{
			Compare(mismatches, label + " requests", pythonRequests, handler.Requested);
		}
		return mismatches;
	}

	private static IEnumerable<string> Set(IEnumerable<string> items) => items.OrderBy(item => item, StringComparer.Ordinal);

	private static void Compare(List<string> mismatches, string label, IEnumerable<string> python, IEnumerable<string> csharp)
	{
		string[] p = python.ToArray();
		string[] c = csharp.ToArray();
		if (!p.SequenceEqual(c, StringComparer.Ordinal))
		{
			int at = 0;
			while (at < p.Length && at < c.Length && p[at] == c[at])
			{
				at++;
			}
			mismatches.Add($"{label}: Python {p.Length} items, C# {c.Length}; first difference at {at}: "
				+ $"Python {Escape(at < p.Length ? p[at] : "<end>")} C# {Escape(at < c.Length ? c[at] : "<end>")}");
		}
	}

	private static readonly Regex ObjectName = new(@"<\w+ object at 0x[0-9A-F]+>");

	private static bool HasSurrogatePair(string text) =>
		Enumerable.Range(0, Math.Max(0, text.Length - 1)).Any(i => char.IsSurrogatePair(text[i], text[i + 1]));

	private static string Escape(string text) =>
		"'" + string.Concat(text.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}")) + "'";

	private static string Report(List<string> mismatches)
	{
		foreach (string mismatch in mismatches)
		{
			Console.WriteLine(mismatch);
		}
		return mismatches.Count == 0 ? "" : $"{mismatches.Count} mismatches (all on standard output), first:\n" + mismatches[0];
	}

	private static string FormatSeries(object python)
	{
		var s = (object[])python;
		return $"{s[0]}|{s[1]}|{s[2]}|{s[3]}|{s[4]}|{s[5]}";
	}

	private static string FormatSeries(SeriesRef s) =>
		$"{s.Id}|{s.Name}|{s.StartYear ?? -1}|{s.Publisher}|{s.IssueCount}|{s.ThumbUrl?.OriginalString ?? ""}";

	private static string FormatIssueRef(object python)
	{
		var i = (object[])python;
		return $"{i[0]}|{i[1]}|{i[2]}|{i[3]}";
	}

	private static string FormatIssueRef(IssueRef i) => $"{i.Id}|{i.IssueNumber}|{i.Title}|{i.ThumbUrl?.OriginalString ?? ""}";

	private static IEnumerable<string> FormatIssue(object[] i)
	{
		static string List(object? items) => string.Join(" ; ", ((object[])items!).Select(item => item?.ToString()));
		PartialDate published = PartialDate.Of(Year(i[9]), Part(i[10]), Part(i[11]));
		PartialDate released = PartialDate.Of(Year(i[12]), Part(i[13]), Part(i[14]));
		return
		[
			$"key {i[0]}", $"series {i[1]}", $"series name {i[2]}", $"volume year {i[3]}", $"number {i[4]}", $"title {i[5]}",
			$"summary {i[6]}", $"publisher {i[7]}", $"imprint {i[8]}", $"published {Format(published)}", $"released {Format(released)}",
			$"crossovers {List(i[15])}", $"characters {List(i[16])}", $"teams {List(i[17])}", $"locations {List(i[18])}",
			$"writers {List(i[19])}", $"pencillers {List(i[20])}", $"inkers {List(i[21])}", $"colorists {List(i[22])}",
			$"letterers {List(i[23])}", $"cover artists {List(i[24])}", $"editors {List(i[25])}", $"webpage {i[26]}",
			$"rating {((double)i[27] == 0.0 ? "none" : i[27])}", $"covers {List(i[28])}",
		];
	}

	private static IEnumerable<string> FormatIssue(Issue i) =>
	[
		$"key {i.Id}", $"series {i.SeriesId}", $"series name {i.SeriesName}", $"volume year {i.VolumeYear ?? -1}", $"number {i.IssueNumber}",
		$"title {i.Title}", $"summary {i.Summary}", $"publisher {i.Publisher}", $"imprint {i.Imprint}", $"published {Format(i.Published)}",
		$"released {Format(i.Released)}", $"crossovers {string.Join(" ; ", i.Crossovers)}", $"characters {string.Join(" ; ", i.Characters)}",
		$"teams {string.Join(" ; ", i.Teams)}", $"locations {string.Join(" ; ", i.Locations)}", $"writers {string.Join(" ; ", i.Writers)}",
		$"pencillers {string.Join(" ; ", i.Pencillers)}", $"inkers {string.Join(" ; ", i.Inkers)}", $"colorists {string.Join(" ; ", i.Colorists)}",
		$"letterers {string.Join(" ; ", i.Letterers)}", $"cover artists {string.Join(" ; ", i.CoverArtists)}", $"editors {string.Join(" ; ", i.Editors)}",
		$"webpage {i.Webpage}", $"rating {(i.Rating is null ? "none" : i.Rating)}", $"covers {string.Join(" ; ", i.CoverUrls.Select(u => u.OriginalString))}",
	];

	private static int? Year(object? value) => (int)value! is >= 0 and var year ? year : null;

	private static int? Part(object? value) => (int)value! is > 0 and var part ? part : null;

	private static string Format(PartialDate date) => $"{date.Year}-{date.Month}-{date.Day}";

	private sealed class Recorder(Action<QueryProgress>? onReport = null) : IProgress<QueryProgress>
	{
		private readonly List<QueryProgress> reports = [];

		public IReadOnlyList<QueryProgress> Reports => reports;

		public void Report(QueryProgress value)
		{
			reports.Add(value);
			onReport?.Invoke(value);
		}
	}
}
