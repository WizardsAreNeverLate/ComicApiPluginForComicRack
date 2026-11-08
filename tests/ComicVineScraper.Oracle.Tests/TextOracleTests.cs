using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ComicVineScraper.Text;

namespace ComicVineScraper.Oracle.Tests;

/// <summary>NaturalOrder, NumberWords, SearchTerms, and SummaryText against utils.py, cvdb.py, and db.py.</summary>
public sealed class TextOracleTests
{
	private static readonly string[] IssueNumbers =
	[
		"", " ", "0", "00", "0.", ".0", "-0", "1", "01", "1.0", "1.00", "1.01", "1.1", "1.10", "1.2", "2", "5", "05", "10", "11", "23", "55", "100", "1e5",
		"-1", "-5", "-6", "-0.1", "-.11", "- 5", "-5a", ".3", "0.31", "½", "0½", "5½", "5 ½", " 5 ¼ ", "-½", "-5 ¾", "- 5 ¼ ", "¼a", "5½a", "5½b", "⅞", "⅛",
		"1a", "1A", "1b", "1c", "10a", "2a", "5 A", "5A", "5 a", "a", "A", "b", "B", "aa", "aa2", "aa2.3", "a1", "a1.1", "a10", "a10b2", "1-2", "1.2.3", "1..2",
		"nan", "NaN", "inf", "-inf", "infinity", "Infinity", "x nan", "nan 5", "5 nan", "inf 2", "3 inf", "٣", "٣a", "x٣y", "۳", "١٢", "Annual", "annual 2",
		"Annual 1", "special", "Special", "é", "e", "z", "Z", "a_b", "a`", "[x", "^1", "a~", "ζ", "ǅ", "İ", "\U0001F600", "1 2", "1  2", "12 b", "ts", "TPB", "3d", "3D", "3.d", "#3", "3#",
	];

	private static readonly System.Text.RegularExpressions.Regex FractionTail = new(@"^\s*(-?)\s*(\d*)\s*[⅛⅙⅕¼⅓⅜⅖½⅗⅝⅔¾⅘⅚⅞]\s*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

	[Test]
	public async Task NaturalOrder_orders_text_after_a_fraction_where_Python_ignores_it()
	{
		await Assert.That((bool)OracleAdapters.Call("natural_keys_equal", "5½a", "5½b")!).IsTrue();
		await Assert.That(NaturalOrder.KeysEqual("5½a", "5½b")).IsFalse();
		await Assert.That((bool)OracleAdapters.Call("natural_keys_equal", "0.5000", "½")!).IsTrue();
		await Assert.That(NaturalOrder.KeysEqual("0.5000", "½")).IsTrue();
	}

	[Test]
	public async Task NaturalOrder_compares_like_natural_compare_on_every_pair()
	{
		var hasNaN = IssueNumbers.ToDictionary(s => s, s => (bool)OracleAdapters.Call("natural_key_has_nan", s)!, StringComparer.Ordinal);
		var hasFractionTail = IssueNumbers.ToDictionary(s => s, s => FractionTail.Match(s) is { Success: true } m && m.Index == 0 && s.Trim().Length > m.Length, StringComparer.Ordinal);
		int compared = 0;
		foreach (string a in IssueNumbers)
		{
			foreach (string b in IssueNumbers)
			{
				if (hasNaN[a] && hasNaN[b])
				{
					continue; // deviations.md #16
				}
				if (hasFractionTail[a] || hasFractionTail[b])
				{
					continue; // deviations.md #19
				}
				int python = (int)Oracle.Call("utils", "natural_compare", a, b)!;
				await Assert.That(NaturalOrder.Instance.Compare(a, b)).IsEqualTo(python).Because($"'{a}' vs '{b}'");
				bool pythonEqual = (bool)OracleAdapters.Call("natural_keys_equal", a, b)!;
				await Assert.That(NaturalOrder.KeysEqual(a, b)).IsEqualTo(pythonEqual).Because($"'{a}' == '{b}'");
				compared++;
			}
		}
		await Assert.That(compared).IsGreaterThan(IssueNumbers.Length * IssueNumbers.Length * 9 / 10);
	}

	[Test]
	public async Task NaturalOrder_differs_from_Python_only_for_NaN_against_NaN()
	{
		await Assert.That((int)Oracle.Call("utils", "natural_compare", "nan", "nan")!).IsEqualTo(-1);
		await Assert.That((bool)OracleAdapters.Call("natural_keys_equal", "nan", "nan")!).IsFalse();
		await Assert.That(NaturalOrder.Instance.Compare("nan", "NaN")).IsEqualTo(0);
		await Assert.That(NaturalOrder.KeysEqual("nan", "nan")).IsTrue();
	}

	private static readonly string[] Phrases =
	[
		"", "batman 2", "2nd", "the 2nd coming", "1st issue", "1rst", "first", "second", "twelfth night", "eightteenth", "twelveth", "x-men 3",
		"spider-man 2099", "20th century", "0th law", "zero", "twenty", "21", "1/2", "4.5", "one-shot", "one two three", "1 2 3 20 21", "onetwo",
		"seven samurai", "seventeen", "eleventh hour", "8th wonder", "the 10th", "3rd rock", "fourth world", "2nd2", "(2)", "#1", "١", "un 1",
	];

	[Test]
	public async Task NumberWords_matches_convert_number_words()
	{
		foreach (string phrase in Phrases)
		{
			await Assert.That(NumberWords.Expand(phrase)).IsEqualTo((string)Oracle.Call("utils", "convert_number_words", phrase, true)!).Because("expand " + phrase);
			await Assert.That(NumberWords.Contract(phrase)).IsEqualTo((string)Oracle.Call("utils", "convert_number_words", phrase, false)!).Because("contract " + phrase);
		}
	}

	private static IEnumerable<string> SearchCorpus()
	{
		string[] fixedTerms =
		[
			"Batman & Robin", "Batman&Robin", "Batman  &  Robin", "X-Men c2c", "C2C X-Men", "Spawn (noads)", "Spawn noadss", "tbp Thor", "Thor tbps",
			"ctc", "Hulk: Gray", "Hulk's Back", "S.H.I.E.L.D.", "2000 AD", "Wolverine #3", "Daredevil (2011) [digital]", "Ms. Marvel!", "100% Bullets",
			"Iron Man 2.0", "Captain America & the Falcon 2", "The Fifth Beatle", "Seven Soldiers", "Ultimate X-Men 2nd", "Æon Flux", "Élan ½",
			"Ποπ 3", "Ящик два", "Batman\tBeyond", "  padded  ", "", "&", " & ", "one", "1", "c2c tbp", "\U0001F600 emoji 2", "İstanbul 2",
			"Love and Rockets - Vol 2", "Y: The Last Man", "Hellblazer/Constantine", "Fables #75 (of 75)", "Red Sonja+", "Ronin~", "Zot!",
		];
		const string alphabet = "abc XYZ 0129&:'.-_#!?()c2ctbpnoads½éΠ";
		var random = new Random(77);
		var generated = new List<string>();
		for (int i = 0; i < 600; i++)
		{
			var text = new StringBuilder();
			for (int j = random.Next(0, 20); j > 0; j--)
			{
				text.Append(alphabet[random.Next(alphabet.Length)]);
			}
			generated.Add(text.ToString());
		}
		return [.. fixedTerms, .. Phrases, .. generated];
	}

	[Test]
	public async Task SearchTerms_Clean_matches_cleanup_search_terms()
	{
		int compared = 0;
		foreach (string terms in SearchCorpus())
		{
			string python = (string)Oracle.Call("cvdb", "__cleanup_search_terms", terms, false)!;
			await Assert.That(SearchTerms.Clean(terms, alternate: false)).IsEqualTo(python).Because(terms);
			string pythonAlternate = (string)Oracle.Call("cvdb", "__cleanup_search_terms", python, true)!;
			await Assert.That(SearchTerms.Clean(python, alternate: true)).IsEqualTo(pythonAlternate).Because("alternate " + python);
			compared++;
		}
		await Assert.That(compared).IsGreaterThan(600);
	}

	[Test]
	public async Task SearchTerms_StripIgnored_matches_query_series_refs()
	{
		string[][] ignoredLists =
		[
			[], ["the"], ["THE", "and"], ["digital", "c2c"], ["x-men"], ["a b"], [" the "], ["", "of"], ["ποπ"], ["2"], ["the", "1"], ["\U0001F600"], ["Æon"],
		];
		int compared = 0;
		foreach (string terms in SearchCorpus().Take(160))
		{
			foreach (string[] ignored in ignoredLists)
			{
				string python = (string)OracleAdapters.Call("strip_ignored_terms", terms, ignored)!;
				await Assert.That(SearchTerms.StripIgnored(terms, ignored)).IsEqualTo(python).Because($"'{terms}' without [{string.Join(",", ignored)}]");
				compared++;
			}
		}
		await Assert.That(compared).IsEqualTo(160 * ignoredLists.Length);
	}

	[Test]
	public async Task SearchTerms_StripIgnored_ignores_the_user_locale_where_Python_did_not()
	{
		CultureInfo culture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
		string python = (string)OracleAdapters.Call("strip_ignored_terms", "DIGITALPIN batman", new[] { "digitalpin" })!;
		string csharp = SearchTerms.StripIgnored("DIGITALPIN batman", ["digitalpin"]);
		CultureInfo.CurrentCulture = culture;

		await Assert.That(python).IsEqualTo("DIGITALPIN batman");
		await Assert.That(csharp).IsEqualTo(" batman");
	}

	private static IEnumerable<string> Descriptions()
	{
		string[] fixedDescriptions =
		[
			"", "Overview", "<h4>Overview</h4><p>Batman fights crime.</p>", "<p>One</p><p>Two</p>", "Line<br>Two<BR/>Three<br />Four<Br>Five",
			"<P >para</P>", "a&nbsp;b&nbsp c&nbsp;&nbsp;d", "Tom &amp; Jerry &quot;quoted&quot; &lt;b&gt;bold&lt;/b&gt;", "&amp;lt;", "a    b     c",
			"<p>Story</p><h4>List of covers and their creators:</h4><table><tr><td>Cover</td></tr></table>", "text\nlist of COVERS\nmore",
			"<a href=\"x\">link</a> text <i>it</i>", "<p>multi\nline <b>tag\nspan</b></p>", "  <p>  padded  </p>  ", " nbsp ", "<br><br><br>",
			"2 < 3 and 5 > 4", "<unclosed", "x<y>z", "Overview of Overview", "<p>emoji \U0001F600</p>", "Ébène &amp; Œuvre", "\t<p>\ttab</p>",
		];
		const string alphabet = "ab <>/pPbrBR&;nbspquotlgtamOverviewlist of covers\n  ";
		var random = new Random(31);
		var generated = new List<string>();
		for (int i = 0; i < 500; i++)
		{
			var text = new StringBuilder();
			for (int j = random.Next(0, 40); j > 0; j--)
			{
				text.Append(alphabet[random.Next(alphabet.Length)]);
			}
			generated.Add(text.ToString());
		}
		return [.. fixedDescriptions, .. generated];
	}

	[Test]
	public async Task SummaryText_Clean_matches_issue_parse_summary()
	{
		int compared = 0;
		foreach (string description in Descriptions())
		{
			string python = (string)OracleAdapters.Call("issue_summary", description)!;
			await Assert.That(SummaryText.Clean(description)).IsEqualTo(python).Because(description);
			compared++;
		}
		await Assert.That(compared).IsGreaterThan(500);
	}
}
