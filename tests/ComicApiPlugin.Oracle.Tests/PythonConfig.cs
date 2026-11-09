using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;

namespace ComicApiPlugin.Oracle.Tests;

/// <summary>A temp folder standing in for %APPDATA%\Comic Vine Scraper. The oracle never sees the real one.</summary>
public sealed class TempFolder : IDisposable
{
	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cvs-test-" + Guid.NewGuid().ToString("N"));

	public TempFolder() => Directory.CreateDirectory(Path);

	public string Settings => System.IO.Path.Combine(Path, "settings.dat");
	public string Advanced => System.IO.Path.Combine(Path, "advanced.dat");

	public void Dispose() => Directory.Delete(Path, true);
}

/// <summary>Drives utils/configuration.py. Resources.initialize is never called, because it would create the real settings folder.</summary>
public static class PythonConfig
{
	public static object New(TempFolder folder) => Eval(
		"(lambda R, C: (setattr(R, 'SETTINGS_FILE', s), setattr(R, 'ADVANCED_FILE', a), C())[2])(__import__('resources').Resources, __import__('configuration').Configuration)",
		("s", folder.Settings), ("a", folder.Advanced))!;

	public static void Set(object config, string name, object? value) => Oracle.Call("__builtin__", "setattr", config, name, value);

	public static object? Get(object config, string name) => Oracle.Call("__builtin__", "getattr", config, name);

	public static void LoadDefaults(object config) => Eval("c.load_defaults()", ("c", config));

	public static void SaveDefaults(object config) => Eval("c.save_defaults()", ("c", config));

	public static bool Compiles(string pattern)
	{
		try
		{
			Eval("(lambda p: (__import__('re').compile(p), True)[1])(p)", ("p", pattern));
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static string GroupsOf(string pattern, string sample) => (string)Eval(
		"(lambda m: 'none' if m is None else '|'.join('%s=%s' % (k, v) for k, v in sorted(m.groupdict().items())))(__import__('re').match(p, s))",
		("p", pattern), ("s", sample))!;

	/// <summary>"none" where the pattern is unset or Python's re.compile rejects it; otherwise Python's groups on every sample. Read from Python alone.</summary>
	public static string RegexOutcome(object python)
	{
		string pattern = (string)Get(python, "alt_search_regex_s")!;
		return pattern.Length == 0 || !Compiles(pattern) ? "none" : string.Join(";", AdvancedCorpus.RegexSamples.Select(s => GroupsOf(pattern, s)));
	}

	/// <summary>The same outcome for the port's compiled regex, in the format of <see cref="RegexOutcome(object)"/>.</summary>
	public static string RegexOutcome(Regex? csharp) => csharp is null ? "none" : string.Join(";", AdvancedCorpus.RegexSamples.Select(s => CSharpGroups(csharp, s)));

	/// <summary>The groups of re.match(pattern, sample), in GroupsOf's format. Python's match is anchored at the start; Regex.Match is not.</summary>
	public static string CSharpGroups(Regex csharp, string sample)
	{
		if (csharp.Match(sample) is not { Success: true, Index: 0 } match)
		{
			return "none";
		}
		return string.Join("|", csharp.GetGroupNames().Where(n => !int.TryParse(n, out _)).OrderBy(n => n, StringComparer.Ordinal)
			.Select(n => n + "=" + (match.Groups[n].Success ? match.Groups[n].Value : "None")));
	}

	public static string SettingKey(string settingsKey) =>
		"update_" + Regex.Replace(settingsKey.Substring("update".Length), "(?<!^)[A-Z]", m => "_" + m.Value).ToLowerInvariant() + "_b";

	public static void Apply(object python, Configuration config)
	{
		Set(python, "api_key_s", config.ApiKey);
		Set(python, "ow_existing_b", config.OverwriteExisting);
		Set(python, "ignore_blanks_b", config.IgnoreBlanks);
		Set(python, "convert_imprints_b", config.ConvertImprints);
		Set(python, "autochoose_series_b", config.Choose == ChooseMode.AutoChooseSeries);
		Set(python, "confirm_issue_b", config.Choose == ChooseMode.ConfirmIssue);
		Set(python, "download_thumbs_b", config.DownloadThumbs);
		Set(python, "preserve_thumbs_b", config.PreserveThumbs);
		Set(python, "fast_rescrape_b", config.FastRescrape);
		Set(python, "summary_dialog_b", config.SummaryDialog);
		Set(python, "rescrape_notes_b", config.Update.HasFlag(BookFields.Notes));
		Set(python, "rescrape_tags_b", config.Update.HasFlag(BookFields.Tags));
		foreach (FieldTable.Row row in FieldTable.Toggleable)
		{
			Set(python, SettingKey(row.SettingsKey), (config.Update & row.Controls) == row.Controls);
		}
		Set(python, "advanced_settings_s", config.Advanced.RawText);
	}

	public static Configuration Read(object python)
	{
		BookFields update = BookFields.None;
		foreach (FieldTable.Row row in FieldTable.Toggleable.Where(r => (bool)Get(python, SettingKey(r.SettingsKey))!))
		{
			update |= row.Controls;
		}
		update |= (bool)Get(python, "rescrape_notes_b")! ? BookFields.Notes : BookFields.None;
		update |= (bool)Get(python, "rescrape_tags_b")! ? BookFields.Tags : BookFields.None;
		bool auto = (bool)Get(python, "autochoose_series_b")!;
		bool confirm = (bool)Get(python, "confirm_issue_b")!;
		return new Configuration
		{
			Advanced = Advanced(python),
			ApiKey = (string)Get(python, "api_key_s")!,
			Update = update,
			OverwriteExisting = (bool)Get(python, "ow_existing_b")!,
			IgnoreBlanks = (bool)Get(python, "ignore_blanks_b")!,
			ConvertImprints = (bool)Get(python, "convert_imprints_b")!,
			Choose = confirm ? ChooseMode.ConfirmIssue : auto ? ChooseMode.AutoChooseSeries : ChooseMode.Ask,
			DownloadThumbs = (bool)Get(python, "download_thumbs_b")!,
			PreserveThumbs = (bool)Get(python, "preserve_thumbs_b")!,
			FastRescrape = (bool)Get(python, "fast_rescrape_b")!,
			SummaryDialog = (bool)Get(python, "summary_dialog_b")!,
		};
	}

	public static AdvancedSettings Advanced(object python)
	{
		return new AdvancedSettings(
			(string)Get(python, "advanced_settings_s")!, Strings(python, "ignored_publishers_sl"), Strings(python, "ignored_searchterms_sl"),
			Int(python, "ignored_before_year_n"), Int(python, "ignored_after_year_n"), Int(python, "never_ignore_threshold_n"),
			(bool)Get(python, "update_rating_b")!, (bool)Get(python, "show_covers_b")!, (bool)Get(python, "welcome_dialog_b")!,
			null,
			(bool)Get(python, "ignore_folders_b")!, (bool)Get(python, "force_series_art_b")!, (bool)Get(python, "note_scrape_date_b")!,
			Map(python, "publisher_aliases_sm"), Map(python, "user_imprints_sm"), Int(python, "scrape_delay_n"), Int(python, "max_search_results_n"));
	}

	private static IReadOnlyList<string> Strings(object python, string name) => ((object[])Get(python, name)!).Cast<string>().ToArray();

	private static IReadOnlyDictionary<string, string> Map(object python, string name) =>
		((Dictionary<object, object?>)Get(python, name)!).ToDictionary(e => (string)e.Key, e => (string)e.Value!);

	/// <summary>Python ints beyond 32 bits come back as long or BigInteger; the port saturates them.</summary>
	private static int Int(object python, string name)
	{
		var value = BigInteger.Parse(Convert.ToString(Get(python, name), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
		return value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;
	}

	private static object? Eval(string expression, params (string Name, object Value)[] variables) =>
		Oracle.Call("__builtin__", "eval", expression, variables.ToDictionary(v => v.Name, v => v.Value));
}

/// <summary>advanced.dat texts: every key, with values that hit each branch and clamp, in several line layouts.</summary>
public static class AdvancedCorpus
{
	public static readonly string[] Keys =
	[
		"IGNORE_PUBLISHER", "IGNORE_SEARCHTERM", "IGNORE_BEFORE_YEAR", "IGNORE_AFTER_YEAR", "NEVER_IGNORE_THRESHOLD", "SCRAPE_RATING",
		"SHOW_COVERS", "WELCOME_DIALOG", "ALT_SEARCH_REGEX", "IGNORE_FOLDERS", "FORCE_SERIES_ART", "NOTE_SCRAPE_DATE", "PUBLISHER_ALIAS",
		"IMPRINT", "SCRAPE_DELAY", "MAX_SEARCH_RESULTS",
	];

	private static readonly string[] Words = ["DC Comics", "'Marvel'", "\"Image\"", "  spaced  ", "", "Ünïcode", "a'b", "''", "'", "x\"", "ÀÉ"];
	private static readonly string[] Terms = ["tpb", "TPB", "hc", "a b", "x-y", "123", "é", "٣", "Ab1", "'tpb'", "tpb "];
	private static readonly string[] Numbers =
	[
		"1950", "1950.7", "-5", "0", "abc", "1e3", " 7 ", "1e30", "+3", "3600", "3601", "5000", "5001", "9", "10", "11", "1", "2", "3",
		"9999999999", "2147483648", "0.5", "-0.5", "1_0", "'42'", "\"42\"", "4 2", "", ".5", "5.", "٣",
	];
	private static readonly string[] Bools = ["true", "True", "TRUE", "false", "False", "yes", "1", "", " true ", "'true'", "\"false\"", "tru", "tRuE"];

	public static readonly string[] Regexes =
	[
		@"(?P<series>.+?)\s+(?P<num>\d+)", @"(?P<series>.*) (?P<year>\d{4})", @"^(?P<series>[^#]+)#(?P<num>\d+)", @"(?P<series>a)(?P=series)",
		@"(?P<series>", @"[", @"(", @"*abc", @"(?P<series>.+)(?P<series>.+)", @"\d+(?P<num>\d)", @"(?P<series>\w+)", @"abc{2,1}", @"(?i)(?P<series>x)",
		@"(?P<year>\d+)(?:x)?", @"a++", @"(?<series>b)", @"(?P<series>q)\1", @"(?=x)y", @"\Aab", @"[a-", @"(?P<1a>x)", @"(?P<series>.+?) v(?P<num>\d+)",
		@"(?u)(?P<series>\w+) (?P<num>\d+)", @"(?L)(?P<series>.+?)\s+(?P<num>\d+)", @"(?ui)(?P<series>[a-z]+)", @"(?iu)(?P<series>x)", @"(?uL)x", @"(?P<series>\w+)(?u)",
		@"a{,2}", @"(?P<series>a{,2})b", @"(?P<series>\w{,3}) (?P<num>\d+)", @"a{,}", @"{,2}", @"a{,2}+", @"a{ ,2}",
		@"(?P<series>\N+)", @"\Nabc", @"(?P<series>[\y]+)", @"(?P<series>\w+)\_(?P<num>\d)", @"(?u)(?P<series>\w+)\q", @"\c", @"\p",
	];

	public static readonly string[] RegexSamples = ["Batman 12", "Batman 12 2004", "Batman #12", "aa", "x", "abc", "yy 2004", "Spider-Man v2 (2004)", "", "qq", "bb",
		"Café 12", "Ünïcode 7", "漫画 3", "Naa", "aab", "Ünï_7", "a{,2}b", "NabcN", "yyq", "É 5"];

	private static readonly string[] Arrows =
	[
		"dc->DC Comics", "a => b", "Vertigo-->DC", "x==>y", "  a  ->  b ", "'q' -> 'r'", "\"Q\" -> \"R\"", "a->" + new string('x', 51), "a->" + new string('x', 50),
		new string('y', 51) + "->b", new string('y', 50) + "->b", "->b", "a->", "a>b", "a - > b", "A->B->C", "a-=->b", "'a'->'b' x", " ->b", "a-> ", "ÀB->ü",
	];

	public static IEnumerable<string> Texts()
	{
		string[] layouts = ["{0}={1}", "{0} = {1}", "{0}\t=\t{1}", "{0}='{1}'", "{0}=\"{1}\"", "  {0}  =  {1}  ", "{0}={1}\r"];
		foreach (string key in Keys)
		{
			foreach (string value in ValuesFor(key))
			{
				foreach (string layout in layouts)
				{
					yield return string.Format(CultureInfo.InvariantCulture, layout, key, value);
				}
				yield return string.Format(CultureInfo.InvariantCulture, layouts[0], key.ToLowerInvariant(), value);
			}
		}
		yield return "";
		yield return "   \n\n  ";
		yield return "# comment\nnot a setting\nSCRAPE_DELAY\nSCRAPE_DELAY 5\nIGNORE_PUBLISHER";
		yield return "SCRAPE_DELAY=5 # trailing\nMAX_SEARCH_RESULTS=abc";
		yield return "IGNORE_PUBLISHER=a\nIGNORE_PUBLISHER=A\nIGNORE_PUBLISHER=b\nIGNORE_SEARCHTERM=x\nIGNORE_SEARCHTERM=X";

		var random = new Random(20261109);
		for (int i = 0; i < 700; i++)
		{
			var lines = new List<string>();
			int count = random.Next(1, 9);
			for (int l = 0; l < count; l++)
			{
				if (random.Next(8) == 0)
				{
					lines.Add(random.Next(2) == 0 ? "" : "garbage line " + random.Next(100));
					continue;
				}
				string key = Keys[random.Next(Keys.Length)];
				string[] values = ValuesFor(key).ToArray();
				lines.Add(string.Format(CultureInfo.InvariantCulture, layouts[random.Next(layouts.Length)], key, values[random.Next(values.Length)]));
			}
			yield return string.Join(random.Next(3) == 0 ? "\r\n" : "\n", lines);
		}
	}

	private static IEnumerable<string> ValuesFor(string key) => key switch
	{
		"IGNORE_PUBLISHER" => Words,
		"IGNORE_SEARCHTERM" => Terms,
		"IGNORE_BEFORE_YEAR" or "IGNORE_AFTER_YEAR" or "NEVER_IGNORE_THRESHOLD" or "SCRAPE_DELAY" or "MAX_SEARCH_RESULTS" => Numbers,
		"ALT_SEARCH_REGEX" => Regexes,
		"PUBLISHER_ALIAS" or "IMPRINT" => Arrows,
		_ => Bools,
	};
}
