using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;
using cYo.Projects.ComicRack.Engine;

namespace ComicApiPlugin.Oracle.Tests;

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
		int changedLines = 0;
		int skippedLines = 0;
		for (int i = 0; i < 3000; i++)
		{
			ComicBook original = BookCorpus.RandomBook(random);
			(MergeSettings settings, Dictionary<string, object?> pythonSettings) = RandomSettings(random);
			(Issue issue, Dictionary<string, object?> pythonIssue) = RandomIssue(random);

			var python = new ComicBook(original);
			var logged = (object[])Oracle.Call("cvs_book_oracle", "update_logged", python, pythonSettings, pythonIssue, PythonNow)!;
			var error = (string)logged[0];
			var port = new ComicBook(original);
			var adapter = new ComicRackBook(port, null!);
			BookFacts facts = BookFacts.From(0, adapter.Read(), BookCorpus.ParseFileName, ignoreFolders: false);
			BookPatch patch = MergeRules.Plan(facts, issue, settings, Now);
			adapter.Apply(patch, thumbnail: null);

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
			// deviations.md #11: Python reads an unset ReleasedTime as 1-1-1, so its Release Date line (skipped, or marked changed) differs.
			bool releaseUnset = original.ReleasedTime == DateTime.MinValue;
			(string[] pythonLines, string[] portLines) = Comparable(((object[])logged[1]).Cast<string>(),
				["setting values for this comic book ('*' = changed):", .. patch.Decisions.Select(d => d.ToLogLine()), FieldDecision.CoverArt(null).ToLogLine()], releaseUnset);
			if (!pythonLines.SequenceEqual(portLines))
			{
				differences.Add($"{BookCorpus.Describe(original)} {Show(settings)} log:\n  python {string.Join("¦", pythonLines)}\n  C#     {string.Join("¦", portLines)}");
			}
			changedLines += portLines.Count(line => line.StartsWith("--> *", StringComparison.Ordinal));
			skippedLines += portLines.Count(line => line.EndsWith(FieldDecision.SkippedText, StringComparison.Ordinal));
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
		await Assert.That(changedLines).IsGreaterThan(10000);
		await Assert.That(skippedLines).IsGreaterThan(5000);
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
			var logged = (object[])Oracle.Call("cvs_book_oracle", "skip_forever_logged", python, pythonSettings, PythonNow)!;
			var error = (string)logged[0];
			var port = new ComicBook(original);
			var adapter = new ComicRackBook(port, null!);
			BookFacts facts = BookFacts.From(0, adapter.Read(), BookCorpus.ParseFileName, ignoreFolders: false);
			adapter.Apply(MergeRules.PlanSkipForever(facts, settings, Now), thumbnail: null);

			if (error.Length == 0)
			{
				string[] pythonLog = ((object[])logged[1]).Cast<string>().Where(line => line.StartsWith("Added ", StringComparison.Ordinal)).ToArray();
				if (!pythonLog.SequenceEqual(MergeRules.SkipForeverLog(settings)))
				{
					differences.Add($"{Show(settings)} log: python {string.Join("¦", pythonLog)} C# {string.Join("¦", MergeRules.SkipForeverLog(settings))}");
				}
			}

			string expected = error.Length > 0 ? "python raised " + error : Dump(python, withReleased: true);
			string actual = Dump(port, withReleased: true);
			if (actual != expected)
			{
				differences.Add($"{BookCorpus.Describe(original)} {Show(settings)}:\n  python {expected}\n  C#     {actual}");
			}
		}

		await Assert.That(differences.Take(10)).IsEmpty();
	}

	/// <summary>Deviations.md, "Merge log": Python widens ComicRack's float rating to a double, so an unchanged 3.4 is marked changed.</summary>
	[Test]
	public async Task An_unchanged_rating_that_a_float_cannot_hold_exactly_is_marked_changed_by_Python_and_not_by_the_port()
	{
		var random = new Random(9);
		ComicBook original = BookCorpus.RandomBook(random);
		original.CommunityRating = 3.4f;
		(MergeSettings settings, Dictionary<string, object?> pythonSettings) = RandomSettings(random);
		settings = settings with { Update = settings.Update | BookFields.Rating, OverwriteExisting = true };
		pythonSettings["update_rating_b"] = true;
		pythonSettings["ow_existing_b"] = true;
		(Issue issue, Dictionary<string, object?> pythonIssue) = RandomIssue(random);
		pythonIssue["rating_n"] = 3.4;
		issue = issue with { Rating = 3.4f };

		var logged = (object[])Oracle.Call("cvs_book_oracle", "update_logged", new ComicBook(original), pythonSettings, pythonIssue, PythonNow)!;
		var adapter = new ComicRackBook(new ComicBook(original), null!);
		BookPatch patch = MergeRules.Plan(BookFacts.From(0, adapter.Read(), BookCorpus.ParseFileName, ignoreFolders: false), issue, settings, Now);

		await Assert.That(((object[])logged[1]).Cast<string>().Single(line => line.Contains("Rating"))).IsEqualTo("--> *Rating         : 3.4");
		await Assert.That(patch.Decisions.Single(d => d.Label == "Rating").ToLogLine()).IsEqualTo("-->  Rating         : 3.4");
	}

	[Test]
	public async Task The_cover_art_line_is_what_Python_logs_for_an_issue_with_and_without_a_cover()
	{
		var random = new Random(5);
		foreach (string[] covers in new[] { new[] { "https://comicvine.gamespot.com/a/uploads/scale_medium/1.jpg", "https://x/2.jpg" }, [] })
		{
			ComicBook original = BookCorpus.RandomBook(random);
			(_, Dictionary<string, object?> pythonSettings) = RandomSettings(random);
			(_, Dictionary<string, object?> pythonIssue) = RandomIssue(random);
			pythonIssue["image_urls_sl"] = covers;

			var logged = (object[])Oracle.Call("cvs_book_oracle", "update_logged", new ComicBook(original), pythonSettings, pythonIssue, PythonNow)!;
			string pythonLine = ((object[])logged[1]).Cast<string>().Single(line => line.Contains("Cover Art URL"));

			await Assert.That(pythonLine).IsEqualTo(FieldDecision.CoverArt(covers.FirstOrDefault()).ToLogLine());
		}
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

	private static readonly Regex OrphanDate = new(@"\?\?.*\d", RegexOptions.CultureInvariant);

	/// <summary>
	/// Drops the lines the two sides cannot share. Python keeps each date part on its own, so the random issues can hold a day without
	/// a month. PartialDate (and Comic Vine's YYYY-MM-DD text) cannot, and the date gate writes neither. The unset release date is deviations.md #11.
	/// </summary>
	private static (string[] Python, string[] Port) Comparable(IEnumerable<string> python, IEnumerable<string> port, bool releaseUnset)
	{
		// The lines before the header come from building the ComicBook (parsing the file name), not from the merge.
		string[] pythonLines = [.. python.SkipWhile(line => !line.StartsWith("setting values", StringComparison.Ordinal))];
		var drop = new HashSet<string>();
		foreach (string label in new[] { "Release Date", "Publish Date" })
		{
			string? line = pythonLines.FirstOrDefault(l => l.StartsWith("--> ", StringComparison.Ordinal) && l.Substring(5).StartsWith(label, StringComparison.Ordinal));
			if ((label == "Release Date" && releaseUnset) || (line is not null && OrphanDate.IsMatch(line.Substring(line.IndexOf(':') + 1))))
			{
				drop.Add(label);
			}
		}
		bool Keep(string line) => !drop.Any(label => line.Length > 5 && line.StartsWith("--> ", StringComparison.Ordinal) && line.Substring(5).StartsWith(label, StringComparison.Ordinal));
		return ([.. pythonLines.Where(Keep)], [.. port.Where(Keep)]);
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
