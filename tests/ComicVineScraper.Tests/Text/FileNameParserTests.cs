using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicVineScraper.Diagnostics;
using ComicVineScraper.Text;

namespace ComicVineScraper.Tests.Text;

public sealed class FileNameParserTests
{
	[Test]
	public async Task Parse_prefers_the_alt_search_regex_and_falls_back_to_extract()
	{
		Regex regex = PyRegex.TryCompile(@"(?P<series>\w+)_(?P<num>\d+)_(?P<year>\d{4})", TimeSpan.FromSeconds(1))!;

		await Assert.That(FileNameParser.Parse("batman_3_1999.cbz", regex)).IsEqualTo(new ParsedName("batman", "3", "1999"));
		await Assert.That(FileNameParser.Parse("Batman 004 (2001).cbz", regex)).IsEqualTo(new ParsedName("Batman", "4", "2001"));
		await Assert.That(FileNameParser.Parse("Batman 004 (2001).cbz", null)).IsEqualTo(new ParsedName("Batman", "4", "2001"));
	}

	[Test]
	public async Task The_alt_search_regex_must_match_at_the_start_and_find_a_series()
	{
		Regex regex = PyRegex.TryCompile(@"(?P<series>[a-z]+)(?P<num>\d+)?", TimeSpan.FromSeconds(1))!;

		await Assert.That(FileNameParser.MatchAltSearchRegex("12 batman", regex)).IsNull();
		await Assert.That(FileNameParser.MatchAltSearchRegex("batman", regex)).IsEqualTo(new ParsedName("batman", "", ""));
	}

	[Test]
	public async Task An_alt_search_regex_that_times_out_counts_as_no_match()
	{
		Regex regex = PyRegex.TryCompile(@"(?P<series>(a+)+)b", TimeSpan.FromMilliseconds(50))!;

		await Assert.That(FileNameParser.MatchAltSearchRegex(new string('a', 40) + "c", regex)).IsNull();
	}

	[Test]
	public async Task A_regex_that_timed_out_is_not_run_again_and_is_logged_once()
	{
		Regex regex = PyRegex.TryCompile(@"(?P<series>(a+)+)b", TimeSpan.FromMilliseconds(300))!;
		string slow = new string('a', 40) + "c";
		var log = new DebugLog();

		var first = System.Diagnostics.Stopwatch.StartNew();
		FileNameParser.MatchAltSearchRegex(slow, regex, log);
		first.Stop();
		var second = System.Diagnostics.Stopwatch.StartNew();
		ParsedName? again = FileNameParser.MatchAltSearchRegex(slow, regex, log);
		second.Stop();

		await Assert.That(first.ElapsedMilliseconds).IsGreaterThanOrEqualTo(250);
		await Assert.That(second.ElapsedMilliseconds).IsLessThan(100);
		await Assert.That(again).IsNull();
		await Assert.That(log.Snapshot().Count).IsEqualTo(1);
	}
	[Test]
	[Arguments("Batman 4 (۲۰۰۴).cbz", "Batman", "4", "2004")]
	[Arguments("Batman 4 (۱۸۰۰).cbz", "Batman", "4", "")]
	[Arguments("Batman 4 (٢٠٠٤).cbz", "Batman", "4", "٢٠٠٤")]
	public async Task A_year_in_non_ASCII_digits_keeps_the_series_and_number(string fileName, string series, string number, string year)
	{
		await Assert.That(FileNameParser.Extract(fileName)).IsEqualTo(new ParsedName(series, number, year));
	}

	[Test]
	[Arguments("Batman 3 DI 5.cbr", "Batman", "3")]
	[Arguments("BATMAN VOLUME 2 3.cbr", "BATMAN", "3")]
	[Arguments("Spawn 7 OF 9 COVERS.cbr", "Spawn", "")]
	public async Task Case_insensitive_rules_do_not_depend_on_the_user_locale(string fileName, string series, string number)
	{
		CultureInfo culture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
		ParsedName parsed = FileNameParser.Extract(fileName);
		CultureInfo.CurrentCulture = culture;

		await Assert.That(parsed).IsEqualTo(new ParsedName(series, number, ""));
	}
}
