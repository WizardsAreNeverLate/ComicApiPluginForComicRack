using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Config;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>Configuration and AdvancedSettings against utils/configuration.py, with Resources pointed at a temp folder.</summary>
[NotInParallel("python-resources")]
public sealed class ConfigurationOracleTests
{
	private static readonly DateTime Now = new(2024, 1, 31, 13, 45, 10);

	[Test]
	public async Task Advanced_parsing_matches_the_Python_configuration_on_a_generated_corpus()
	{
		using var folder = new TempFolder();
		object python = PythonConfig.New(folder);
		var mismatches = new List<string>();
		foreach (string text in AdvancedCorpus.Texts())
		{
			PythonConfig.Set(python, "advanced_settings_s", text);
			string expected = Canonical(PythonConfig.Advanced(python)) + " regex=" + PythonConfig.RegexOutcome(python);
			AdvancedSettings parsed = AdvancedSettings.Parse(text);
			string actual = Canonical(parsed) + " regex=" + PythonConfig.RegexOutcome(parsed.AltSearchRegex);
			if (expected != actual)
			{
				mismatches.Add($"{text.Replace("\n", "\n")}\n  python {expected}\n  C#     {actual}");
			}
		}

		await Assert.That(mismatches.Take(8)).IsEmpty();
	}

	[Test]
	public async Task The_advanced_corpus_is_large_and_covers_every_key()
	{
		string[] texts = AdvancedCorpus.Texts().ToArray();

		await Assert.That(texts.Length).IsGreaterThanOrEqualTo(1500);
		foreach (string key in AdvancedCorpus.Keys)
		{
			await Assert.That(texts.Count(t => t.Contains(key))).IsGreaterThan(20);
		}
	}

	[Test]
	public async Task Alternate_search_regexes_compile_and_match_as_in_Python()
	{
		var mismatches = new List<string>();
		foreach (string pattern in AdvancedCorpus.Regexes)
		{
			bool python = PythonConfig.Compiles(pattern);
			var csharp = PythonRegex.TryCompile(pattern);
			if (python != (csharp is not null))
			{
				mismatches.Add($"compile {pattern}: python {python}, C# {csharp is not null}");
				continue;
			}
			if (!python)
			{
				continue;
			}
			foreach (string sample in AdvancedCorpus.RegexSamples)
			{
				string expected = PythonConfig.GroupsOf(pattern, sample);
				string actual = csharp is null ? "none" : PythonConfig.CSharpGroups(csharp, sample);
				if (csharp is not null && expected != actual)
				{
					mismatches.Add($"match {pattern} on {sample}: python {expected}, C# {actual}");
				}
			}
		}

		await Assert.That(mismatches.Take(8)).IsEmpty();
	}

	[Test]
	public async Task Python_loads_what_CSharp_saves()
	{
		using var folder = new TempFolder();
		var random = new Random(20261109);
		var mismatches = new List<string>();
		foreach (Configuration config in SettingsCorpus(random, 300))
		{
			File.WriteAllBytes(folder.Settings, DatFile.ToBytes(config.ToSettingsMap(), Now));
			File.WriteAllBytes(folder.Advanced, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(config.Advanced.RawText)]);
			object python = PythonConfig.New(folder);
			PythonConfig.LoadDefaults(python);

			string expected = Canonical(config) + " regex=" + PythonConfig.RegexOutcome(config.Advanced.AltSearchRegex);
			string actual = Canonical(PythonConfig.Read(python)) + " regex=" + PythonConfig.RegexOutcome(python);
			if (expected != actual)
			{
				mismatches.Add($"python {actual}\n  C#     {expected}");
			}
		}

		await Assert.That(mismatches.Take(5)).IsEmpty();
	}

	[Test]
	public async Task CSharp_loads_what_Python_saves()
	{
		using var folder = new TempFolder();
		var random = new Random(20261110);
		var mismatches = new List<string>();
		foreach (Configuration config in SettingsCorpus(random, 300))
		{
			object python = PythonConfig.New(folder);
			PythonConfig.Apply(python, config);
			File.Delete(folder.Settings);
			File.Delete(folder.Advanced);
			PythonConfig.SaveDefaults(python);

			Configuration loaded = Configuration.FromMaps(DatFile.FromBytes(File.ReadAllBytes(folder.Settings)), StripBom(File.ReadAllBytes(folder.Advanced)));

			if (Canonical(loaded) != Canonical(config))
			{
				mismatches.Add($"loaded {Canonical(loaded)}\n  wanted {Canonical(config)}");
			}
		}

		await Assert.That(mismatches.Take(5)).IsEmpty();
	}

	[Test]
	public async Task Both_choose_flags_in_the_file_load_as_confirm_issue_as_the_Python_engine_acts()
	{
		var map = new Dictionary<string, DatValue> { ["autochooseSeries"] = new DatValue.Bool(true), ["confirmIssue"] = new DatValue.Bool(true) };

		await Assert.That(Configuration.FromMaps(map, "").Choose).IsEqualTo(ChooseMode.ConfirmIssue);
	}

	/// <summary>Deviation: int(float("inf")) raises in Python and aborts loading every setting. The port ignores such a line.</summary>
	[Test]
	public async Task Non_finite_numbers_make_Python_throw_and_the_port_ignores_the_line()
	{
		using var folder = new TempFolder();
		object python = PythonConfig.New(folder);
		foreach (string value in new[] { "inf", "-inf", "nan", "1e400" })
		{
			string text = "SCRAPE_DELAY=" + value + "\nIGNORE_BEFORE_YEAR=1950";

			await Assert.That(() => PythonConfig.Set(python, "advanced_settings_s", text)).ThrowsException();
			AdvancedSettings parsed = AdvancedSettings.Parse(text);
			await Assert.That(parsed.ScrapeDelaySeconds).IsEqualTo(1);
			await Assert.That(parsed.IgnoreBeforeYear).IsEqualTo(1950);
		}
	}

	private static string StripBom(byte[] bytes) =>
		bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3) : Encoding.UTF8.GetString(bytes);

	private static IEnumerable<Configuration> SettingsCorpus(Random random, int count)
	{
		string[] advancedTexts = AdvancedCorpus.Texts().ToArray();
		BookFields[] togglable = [.. FieldTable.Toggleable.Select(r => r.Controls), BookFields.Notes, BookFields.Tags];
		for (int i = 0; i < count; i++)
		{
			BookFields update = BookFields.None;
			foreach (BookFields bits in togglable)
			{
				if (random.Next(2) == 0)
				{
					update |= bits;
				}
			}
			yield return new Configuration
			{
				Advanced = AdvancedSettings.Parse(advancedTexts[random.Next(advancedTexts.Length)]),
				ApiKey = random.Next(4) == 0 ? "" : string.Concat(Enumerable.Range(0, 40).Select(_ => "0123456789abcdef"[random.Next(16)])),
				Update = update,
				OverwriteExisting = random.Next(2) == 0,
				IgnoreBlanks = random.Next(2) == 0,
				ConvertImprints = random.Next(2) == 0,
				Choose = (ChooseMode)random.Next(3),
				DownloadThumbs = random.Next(2) == 0,
				PreserveThumbs = random.Next(2) == 0,
				FastRescrape = random.Next(2) == 0,
				SummaryDialog = random.Next(2) == 0,
			};
		}
	}

	private static string Canonical(Configuration c)
	{
		var parts = new List<string>
		{
			"key=" + c.ApiKey, "ow=" + c.OverwriteExisting, "ib=" + c.IgnoreBlanks, "ci=" + c.ConvertImprints,
			"auto=" + (c.Choose == ChooseMode.AutoChooseSeries), "confirm=" + (c.Choose == ChooseMode.ConfirmIssue),
			"dt=" + c.DownloadThumbs, "pt=" + c.PreserveThumbs, "fr=" + c.FastRescrape, "sd=" + c.SummaryDialog,
			"notes=" + c.Update.HasFlag(BookFields.Notes), "tags=" + c.Update.HasFlag(BookFields.Tags),
		};
		parts.AddRange(FieldTable.Toggleable.Select(r => r.SettingsKey + "=" + ((c.Update & r.Controls) == r.Controls)));
		return string.Join(",", parts) + " " + Canonical(c.Advanced);
	}

	private static string Canonical(AdvancedSettings a) => string.Join(";", new[]
	{
		"raw=" + a.RawText, "pub=" + string.Join("|", a.IgnoredPublishers.OrderBy(x => x, StringComparer.Ordinal)),
		"terms=" + string.Join("|", a.IgnoredSearchTerms.OrderBy(x => x, StringComparer.Ordinal)),
		"before=" + a.IgnoreBeforeYear, "after=" + a.IgnoreAfterYear, "thr=" + a.NeverIgnoreThreshold,
		"rating=" + a.ScrapeRating, "covers=" + a.ShowCovers, "welcome=" + a.WelcomeDialog,
		"folders=" + a.IgnoreFolders, "art=" + a.ForceSeriesArt, "date=" + a.NoteScrapeDate,
		"alias=" + string.Join("|", a.PublisherAliases.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ">" + x.Value)),
		"imprint=" + string.Join("|", a.UserImprints.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ">" + x.Value)),
		"delay=" + a.ScrapeDelaySeconds, "max=" + a.MaxSearchResults,
	});
}
