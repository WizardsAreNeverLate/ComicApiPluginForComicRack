using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicVineScraper.Text;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>FileNameParser against fnameparser.extract and fnameparser.regex.</summary>
public sealed class FileNameParserOracleTests
{
	private static readonly string[] SeriesWords =
	[
		"Batman", "X-Men", "Amazing-Spider-Man", "S.H.I.E.L.D.", "2000AD", "2000 AD", "The Beano", "the_beano", "Iron Man 2.0", "Red Hood & the Outlaws",
		"Zorro Rides Again!", "Ultimate", "Vision", "Meta4", "Buffy Season 9", "Daily Bugle", "Spawn", "100 Bullets", "52", "1602", "Ms. Marvel",
		"Hellboy - The Corpse", "Walking_Dead", "Ninjak", "Ēlf", "Ποπ", "Ящик", "Batman٣", "Captain Britain", "Thor", "Die Hard", "Les Bidochon",
	];

	private static readonly string[] Numbers =
	[
		"1", "001", "012", "1.5", "0.5", ".5", "-1", "-0.5", "5a", "10.0b", "#12", "# 7", "#1950", "21.0000000", "007.1", "0", "000", "1/2", "½", "5½",
		"٣", "٣٤", "۳", "١٢.٥", "12-15", "3 of 5", "03 (of 05)", "4 de 6", "2 von 3", "99999999999999999999", "1e5", "0x10", "1.2.3", "-",
	];

	private static readonly string[] Extras =
	[
		"(2011)", "(1990)", "[2004-6]", "{1999-2001}", "(1890)", "(2100)", "(2099)", "(c2c)", "(digital-Empire)", "(of 05)", "02 of 02 covers", "50 pages",
		"245p", "v2", "Vol. 3", "vol -3.1", "Volume 4a", "V2003", "v1999", "(two covers) (Minutemen-DTs)", "(٢٠١١)", "(۲۰۱۱)", "(2011) (2012)", "- Title Here",
		"(Annual 2011)", "[Zone]", "{2005}", "(2006 - 2009)", "( 2007 )", "((2008))", "Annual", "TPB", "_", "-", "", "",
	];

	private static readonly string[] Extensions = [".cbr", ".cbz", "", ".zip", ".pdf.cbz", "."];

	[Test]
	public async Task Extract_matches_Python_on_the_golden_file_names()
	{
		await AssertExtractMatches(GoldenNames());
	}

	[Test]
	public async Task Extract_matches_Python_on_generated_file_names()
	{
		await AssertExtractMatches(GeneratedNames(20261109, 3000));
	}

	[Test]
	public async Task Extract_matches_Python_on_random_characters()
	{
		const string alphabet = "aZ09 9-_.#()[]{}vV,½٣ofcvr2000AD";
		var random = new Random(4242);
		var names = new List<string>();
		for (int i = 0; i < 1500; i++)
		{
			var name = new StringBuilder();
			int length = random.Next(0, 24);
			for (int j = 0; j < length; j++)
			{
				name.Append(alphabet[random.Next(alphabet.Length)]);
			}
			names.Add(name.ToString());
		}
		await AssertExtractMatches(names);
	}

	[Test]
	public async Task Alt_search_regex_matches_Python()
	{
		string[] patterns =
		[
			@"(?P<series>.*?) #?(?P<num>\d+) \((?P<year>\d{4})\)",
			@"(?P<series>[^\d]+)(?P<num>\d+)?",
			@"(?i)(?P<series>batman)\s*(?P<num>\d*)",
			@"(?P<series>\w+)_(?P<num>\d+)_(?P<year>\d+)",
			@"(?P<num>\d+) - (?P<series>.+)\.cb.",
			@"(?P<series>(\w+ )+)(?P<num>\d+)",
			@"(?P<s>\w+) (?P=s) (?P<series>\w+)",
			@"(?P<series>\w+)\_(?P<num>\d{1,})",
			@"(?<series>.+?) (?<num>\d+)",
			@"(?u)(?P<series>\w+) (?P<year>\d{4})?",
			@"(?P<series> +)(?P<num>\d+)",
			@"[(](?P<series>[^)]+)[)] (?P<num>\d+)",
			@"(?P<series>.*)",
			@"x{,3}(?P<series>\w+)",
		];
		string[] names = [.. GoldenNames().Take(120), "Batman 12 (2001).cbr", "batman_3_1999.cbz", "12 - Spawn.cbr", "Thor Thor Odin.cbr", "   7.cbr", "(Hulk) 4.cbr", "xxxBatman", "BATMAN12"];
		int compared = 0;
		foreach (string pattern in patterns)
		{
			Regex regex = PyRegex.TryCompile(pattern, TimeSpan.FromSeconds(5)) ?? throw new InvalidOperationException("did not compile: " + pattern);
			foreach (string name in names)
			{
				var python = (object[]?)Oracle.Call("fnameparser", "regex", name, pattern);
				ParsedName? expected = python is null ? null : new ParsedName((string)python[0]!, (string)python[1]!, (string)python[2]!);
				await Assert.That(FileNameParser.MatchAltSearchRegex(name, regex)).IsEqualTo(expected).Because(pattern + " on " + name);
				compared++;
			}
		}
		await Assert.That(compared).IsEqualTo(patterns.Length * names.Length);
	}

	[Test]
	[Arguments(@"(?P<series>")]
	[Arguments(@"(?Q<series>.*)")]
	[Arguments(@"(?P<series>.*")]
	[Arguments(@"(?P=nope)")]
	[Arguments(@"[a-")]
	public async Task Patterns_Python_rejects_do_not_compile(string pattern)
	{
		bool pythonRejects = !PythonConfig.Compiles(pattern);

		await Assert.That(pythonRejects).IsTrue();
		await Assert.That(PyRegex.TryCompile(pattern, TimeSpan.FromSeconds(1))).IsNull();
	}

	private static async Task AssertExtractMatches(IEnumerable<string> names)
	{
		int compared = 0;
		foreach (string name in names)
		{
			var python = (object[])Oracle.Call("fnameparser", "extract", name)!;
			var expected = new ParsedName((string)python[0]!, (string)python[1]!, (string)python[2]!);
			await Assert.That(FileNameParser.Extract(name)).IsEqualTo(expected).Because(name);
			compared++;
		}
		await Assert.That(compared).IsGreaterThan(0);
	}

	private static IEnumerable<string> GoldenNames()
	{
		string path = Path.Combine(Oracle.SourceRoot, "py", "tests", "test_fnameparser.data");
		return File.ReadAllLines(path, Encoding.UTF8)
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
			.Select(line => Regex.Match(line, line.StartsWith("'", StringComparison.Ordinal) ? "'(.*?)'" : "\"(.*?)\"").Groups[1].Value);
	}

	private static IEnumerable<string> GeneratedNames(int seed, int count)
	{
		var random = new Random(seed);
		string Pick(string[] items) => items[random.Next(items.Length)];
		string[] separators = [" ", " ", " ", "_", "-", ".", "  ", " - "];
		for (int i = 0; i < count; i++)
		{
			var parts = new List<string>();
			if (random.Next(6) == 0)
			{
				parts.Add(Pick(["05.", "12 -", "3_", "001 "]));
			}
			parts.Add(Pick(SeriesWords));
			if (random.Next(3) == 0)
			{
				parts.Add(Pick(Extras));
			}
			if (random.Next(5) > 0)
			{
				parts.Add(Pick(Numbers));
			}
			for (int extra = random.Next(4); extra > 0; extra--)
			{
				parts.Add(Pick(Extras));
			}
			string separator = Pick(separators);
			string name = string.Join(random.Next(4) == 0 ? separator : " ", parts);
			if (random.Next(8) == 0)
			{
				name = name.Replace(" ", "-");
			}
			yield return (random.Next(10) == 0 ? @"C:\Comics\" : "") + name + Pick(Extensions) + (random.Next(15) == 0 ? "  " : "");
		}
	}

	[Test]
	public async Task Generated_names_vary()
	{
		await Assert.That(GeneratedNames(20261109, 3000).Distinct(StringComparer.Ordinal).Count()).IsGreaterThan(2500);
	}
}
