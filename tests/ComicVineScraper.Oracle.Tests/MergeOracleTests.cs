using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Config;
using ComicVineScraper.Model;
using ComicVineScraper.Plugin;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>
/// The whole write path against Python on real ComicRack books: ComicRackBook.Read, BookFacts.From, MergeRules, and
/// ComicRackBook.Apply on one copy; PluginBookData, ComicBook.update or skip_forever, and PluginBookData.update on another.
/// </summary>
public sealed class MergeOracleTests
{
	private static readonly DateTime Now = new(2024, 1, 31, 13, 45, 10);
	private static readonly object[] PythonNow = [2024, 1, 31, 13, 45, 10];

	private static readonly string[] IssueTexts = ["", " ", "Batman", "  The Joker  ", "Ünï", "a,b", "\n", "Mine"];
	private static readonly string[] IssueNumbers = ["", "1", "12", "½", " 3 ", "5a"];
	private static readonly string[] IssuePublishers = ["", "Marvel", "DC Comics", "Vertigo", "Image", "marvel", "MAX", "Icon"];
	private static readonly string[] IssueImprints = ["", "", "Vertigo", "MAX", "Marvel", "Icon", "DC Comics"];
	private static readonly int[] IssueYears = [-1, 0, 1990, 2010, 2012, 9999, 10000];
	private static readonly int[] IssueMonths = [-1, 0, 2, 4, 5, 12, 13];
	private static readonly int[] IssueDays = [-1, 0, 3, 29, 30, 31, 32];
	private static readonly int[] IssueVolumes = [-1, 0, 1990, 1963];
	private static readonly double[] IssueRatings = [0.0, 2.5, 5.0, 6.0, -1.0, 3.25];
	private static readonly string[] IssueWebs = ["", "https://comicvine.gamespot.com/x/4000-1/", " https://y "];
	private static readonly string[][] IssueLists = [[], ["Batman"], ["a", " ", "b"], ["Smith, John"], ["x;y"], [" padded "], ["Robin", "Batgirl", "Alfred"]];

	private static readonly Dictionary<string, string> ImprintPool = new()
	{
		["vertigo"] = "DC Comics", ["max"] = "Marvel", ["icon"] = "Marvel", ["marvel"] = "Marvel Worldwide",
	};

	private static readonly Dictionary<string, string> AliasPool = new()
	{
		["marvel"] = "Marvel Comics", ["dc comics"] = "DC", ["vertigo"] = "Vertigo Comics", ["image"] = "Image Comics", ["marvel worldwide"] = "MW",
	};

	[Test]
	public async Task Plan_and_Apply_write_what_ComicBook_update_writes()
	{
		var random = new Random(4242);
		List<string> differences = [];
		int compared = 0;
		int releaseFills = 0;
		for (int i = 0; i < 3000; i++)
		{
			ComicBook original = BookCorpus.RandomBook(random);
			(MergeSettings settings, Dictionary<string, object?> pythonSettings) = RandomSettings(random);
			(Issue issue, Dictionary<string, object?> pythonIssue) = RandomIssue(random);

			var python = new ComicBook(original);
			var error = (string)Oracle.Call("cvs_book_oracle", "update", python, pythonSettings, pythonIssue, PythonNow)!;
			var port = new ComicBook(original);
			var adapter = new ComicRackBook(port, null!);
			BookFacts facts = BookFacts.From(0, adapter.Read(), BookCorpus.ParseFileName, ignoreFolders: false);
			adapter.Apply(MergeRules.Plan(facts, issue, settings, Now), thumbnail: null);

			bool unsetReleaseFilled = original.ReleasedTime == DateTime.MinValue && !settings.OverwriteExisting && settings.Update.HasFlag(BookFields.Released);
			if (error.Length > 0)
			{
				// deviations.md #13: Python's DateTime(y, m, d) throws on a store date such as 2010-02-31, part way through the write.
				if (!IsUnrealDate(issue.Released) || !error.Contains("un-representable DateTime"))
				{
					differences.Add($"{BookCorpus.Describe(original)}: python raised {error}");
				}
				continue;
			}
			compared++;
			string expected = Dump(python, withReleased: !unsetReleaseFilled);
			string actual = Dump(port, withReleased: !unsetReleaseFilled);
			if (unsetReleaseFilled)
			{
				// deviations.md #11: an unset ReleasedTime is blank, so the store date is filled in without overwrite.
				DateTime filled = DateGate.Restrict(BookFields.Released, new BookValues { Released = issue.Released }) == BookFields.Released
					? new DateTime(issue.Released.Year!.Value, issue.Released.Month!.Value, issue.Released.Day!.Value)
					: DateTime.MinValue;
				releaseFills += filled == DateTime.MinValue ? 0 : 1;
				if (port.ReleasedTime != filled || python.ReleasedTime != DateTime.MinValue)
				{
					differences.Add($"{BookCorpus.Describe(original)}: released python {python.ReleasedTime:d}, C# {port.ReleasedTime:d}, expected {filled:d}");
				}
			}
			if (actual != expected)
			{
				differences.Add($"{BookCorpus.Describe(original)} {Show(settings)}:\n  python {expected}\n  C#     {actual}");
			}
		}

		await Assert.That(differences.Take(10)).IsEmpty();
		await Assert.That(compared).IsGreaterThan(2700);
		await Assert.That(releaseFills).IsGreaterThan(25);
	}

	[Test]
	public async Task PlanSkipForever_and_Apply_write_what_ComicBook_skip_forever_writes()
	{
		var random = new Random(77);
		List<string> differences = [];
		for (int i = 0; i < 1500; i++)
		{
			ComicBook original = BookCorpus.RandomBook(random);
			(MergeSettings settings, Dictionary<string, object?> pythonSettings) = RandomSettings(random);

			var python = new ComicBook(original);
			var error = (string)Oracle.Call("cvs_book_oracle", "skip_forever", python, pythonSettings, PythonNow)!;
			var port = new ComicBook(original);
			var adapter = new ComicRackBook(port, null!);
			BookFacts facts = BookFacts.From(0, adapter.Read(), BookCorpus.ParseFileName, ignoreFolders: false);
			adapter.Apply(MergeRules.PlanSkipForever(facts, settings, Now), thumbnail: null);

			string expected = error.Length > 0 ? "python raised " + error : Dump(python, withReleased: true);
			string actual = Dump(port, withReleased: true);
			if (actual != expected)
			{
				differences.Add($"{BookCorpus.Describe(original)} {Show(settings)}:\n  python {expected}\n  C#     {actual}");
			}
		}

		await Assert.That(differences.Take(10)).IsEmpty();
	}

	private static (MergeSettings, Dictionary<string, object?>) RandomSettings(Random random)
	{
		BookFields update = FieldTable.Toggleable.Where(_ => random.Next(4) > 0).Aggregate(BookFields.None, (all, row) => all | row.Controls);
		update |= random.Next(2) == 0 ? BookFields.Rating : BookFields.None;
		update |= random.Next(2) == 0 ? BookFields.Tags : BookFields.None;
		update |= random.Next(3) > 0 ? BookFields.Notes : BookFields.None;
		bool overwrite = random.Next(2) == 0;
		bool ignoreBlanks = random.Next(2) == 0;
		bool convert = random.Next(3) > 0;
		bool noteDate = random.Next(2) == 0;
		Dictionary<string, string> imprints = ImprintPool.Where(_ => random.Next(2) == 0).ToDictionary(p => p.Key, p => p.Value);
		Dictionary<string, string> aliases = AliasPool.Where(_ => random.Next(3) == 0).ToDictionary(p => p.Key, p => p.Value);

		// Python reads the raw ignore-blanks setting even without overwrite, so the rules get it raw too.
		var settings = new MergeSettings(update, overwrite, ignoreBlanks, convert, true, true, noteDate, imprints, aliases);
		var python = new Dictionary<string, object?>
		{
			["update_rating_b"] = update.HasFlag(BookFields.Rating),
			["rescrape_tags_b"] = update.HasFlag(BookFields.Tags),
			["rescrape_notes_b"] = update.HasFlag(BookFields.Notes),
			["ow_existing_b"] = overwrite,
			["ignore_blanks_b"] = ignoreBlanks,
			["convert_imprints_b"] = convert,
			["note_scrape_date_b"] = noteDate,
			["user_imprints_sm"] = imprints,
			["publisher_aliases_sm"] = aliases,
			["ignore_folders_b"] = false,
			["download_thumbs_b"] = true,
			["preserve_thumbs_b"] = true,
		};
		foreach (FieldTable.Row row in FieldTable.Toggleable)
		{
			python[PythonName(row.SettingsKey)] = update.HasFlag(row.Controls);
		}
		return (settings, python);
	}

	// updateCoverArtist becomes update_cover_artist_b, the Configuration attribute Python reads.
	private static string PythonName(string settingsKey) => Regex.Replace(settingsKey, "[A-Z]", m => "_" + m.Value.ToLowerInvariant()) + "_b";

	private static (Issue, Dictionary<string, object?>) RandomIssue(Random random)
	{
		var values = new Dictionary<string, object?>
		{
			["issue_key"] = random.Next(1, 1000000).ToString(CultureInfo.InvariantCulture),
			["series_key"] = random.Next(1, 100000).ToString(CultureInfo.InvariantCulture),
			["issue_num_s"] = BookCorpus.Pick(random, IssueNumbers),
			["title_s"] = BookCorpus.Pick(random, IssueTexts),
			["series_name_s"] = BookCorpus.Pick(random, IssueTexts),
			["publisher_s"] = BookCorpus.Pick(random, IssuePublishers),
			["imprint_s"] = BookCorpus.Pick(random, IssueImprints),
			["summary_s"] = BookCorpus.Pick(random, IssueTexts),
			["webpage_s"] = BookCorpus.Pick(random, IssueWebs),
			["pub_year_n"] = BookCorpus.Pick(random, IssueYears),
			["pub_month_n"] = BookCorpus.Pick(random, IssueMonths),
			["pub_day_n"] = BookCorpus.Pick(random, IssueDays),
			["rel_year_n"] = BookCorpus.Pick(random, IssueYears),
			["rel_month_n"] = BookCorpus.Pick(random, IssueMonths),
			["rel_day_n"] = BookCorpus.Pick(random, IssueDays),
			["volume_year_n"] = BookCorpus.Pick(random, IssueVolumes),
			["rating_n"] = BookCorpus.Pick(random, IssueRatings),
		};
		foreach (string list in new[] { "crossovers_sl", "characters_sl", "teams_sl", "locations_sl", "writers_sl", "pencillers_sl", "inkers_sl",
			"colorists_sl", "letterers_sl", "cover_artists_sl", "editors_sl" })
		{
			values[list] = BookCorpus.Pick(random, IssueLists);
		}

		// The port's Issue comes from the values Python's Issue holds once its setters have cleaned them.
		var cleaned = (Dictionary<object, object?>)Oracle.Call("cvs_book_oracle", "issue_values", values)!;
		string Text(string name) => (string)cleaned[name]!;
		int? Part(string name) => (int)cleaned[name]! is int n && n >= 0 ? n : null;
		string[] List(string name) => ((object[])cleaned[name]!).Cast<string>().ToArray();
		double rating = (double)cleaned["rating_n"]!;
		var issue = new Issue(
			new IssueId(int.Parse(Text("issue_key"), CultureInfo.InvariantCulture)),
			new SeriesId(int.Parse(Text("series_key"), CultureInfo.InvariantCulture)),
			Text("series_name_s"), Part("volume_year_n"), Text("issue_num_s"), Text("title_s"), Text("summary_s"), Text("publisher_s"), Text("imprint_s"),
			PartialDate.Of(Part("pub_year_n"), Part("pub_month_n"), Part("pub_day_n")),
			PartialDate.Of(Part("rel_year_n"), Part("rel_month_n"), Part("rel_day_n")),
			List("crossovers_sl"), List("characters_sl"), List("teams_sl"), List("locations_sl"), List("writers_sl"), List("pencillers_sl"),
			List("inkers_sl"), List("colorists_sl"), List("letterers_sl"), List("cover_artists_sl"), List("editors_sl"),
			Text("webpage_s"),
			rating == 0.0 && random.Next(2) == 0 ? null : (float)rating,
			[]);
		return (issue, values);
	}

	private static bool IsUnrealDate(PartialDate date) =>
		date is { Year: > 0 and < 9999 and int year, Month: int month, Day: int day } && day > DateTime.DaysInMonth(year, month);

	private static string Dump(ComicBook book, bool withReleased) => string.Join("¦",
		book.Series, book.Number, book.Volume, book.Format, book.Title, book.AlternateSeries, book.Summary, book.Publisher, book.Imprint,
		book.Characters, book.Teams, book.Locations, book.Writer, book.Penciller, book.Inker, book.Colorist, book.Letterer,
		book.CoverArtist, book.Editor, book.Tags, book.Notes, book.Web,
		book.CommunityRating.ToString("R", CultureInfo.InvariantCulture),
		book.GetCustomValue("comicvine_issue"), book.GetCustomValue("comicvine_volume"),
		withReleased ? book.ReleasedTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "-",
		book.Year, book.Month, book.Day).Replace("\n", "\\n");

	private static string Show(MergeSettings settings) =>
		$"[update={settings.Update} ow={settings.OverwriteExisting} ib={settings.IgnoreBlanks} convert={settings.ConvertImprints} " +
		$"imprints={string.Join(",", settings.UserImprints.Keys)} aliases={string.Join(",", settings.PublisherAliases.Keys)}]";
}
