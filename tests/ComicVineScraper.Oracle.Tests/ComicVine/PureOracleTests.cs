using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ComicVineScraper.ComicVine;

namespace ComicVineScraper.Oracle.Tests.ComicVine;

/// <summary>The Comic Vine client's pure rules against their Python originals, each over a generated corpus.</summary>
public sealed class PureOracleTests
{
	[Test]
	public async Task StripInvalidXmlChars_matches_strip_invalid_xml_chars_except_for_surrogate_pairs()
	{
		const string Alphabet = "ab \t\n\r\u0000\u0001\u0008\u000B\u001F\u007F\u0085퟿\uD800􏰀\uDFFF�￾￿";
		var random = new Random(51);
		string[] corpus = Enumerable.Range(0, 4000).Select(_ => Random(random, Alphabet, 12)).ToArray();
		int skipped = 0;
		var mismatches = new List<string>();
		foreach (string text in corpus)
		{
			if (HasSurrogatePair(text))
			{
				skipped++;
				continue;
			}
			Check(mismatches, text, (string)Oracle.Call("cvconnection", "__strip_invalid_xml_chars", text)!, ResponseText.StripInvalidXmlChars(text));
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
		await Assert.That(corpus.Length - skipped).IsGreaterThan(1500);
	}

	[Test]
	public async Task WebUtility_HtmlDecode_matches_the_HttpUtility_HtmlDecode_Python_used()
	{
		string[] fixedCorpus =
		[
			"&amp;", "&amp;amp;", "&lt;b&gt;", "&quot;", "&apos;", "&#39;", "&#x27;", "&nbsp;", "&nbsp", "&NBSP;", "&eacute;", "&Eacute;",
			"&#1;", "&#0;", "&#xFFFE;", "&#xD800;", "&#xDC00;", "&#x1F600;", "&#128512;", "&#x110000;", "&#99999999999;", "&#;", "&#x;",
			"&#65", "&amp", "&unknown;", "& amp;", "&&amp;;", "<![CDATA[&lt;]]>", "a&b", "&#x41;&#X42;", "&hellip;", "&euro;", "&#8364;",
		];
		const string Alphabet = "&#;x0123456789abcdefnbspAMPltgquo ";
		var random = new Random(484);
		string[] corpus = [.. fixedCorpus, .. Enumerable.Range(0, 3000).Select(_ => Random(random, Alphabet, 10))];
		var mismatches = new List<string>();
		foreach (string text in corpus)
		{
			Check(mismatches, text, (string)Oracle.Call("cvoracle", "html_decode", text)!, WebUtility.HtmlDecode(text));
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
	}

	[Test]
	public async Task Alternate_matches_alternate_issue_num_outside_the_fraction_bugs()
	{
		string[] fixedCorpus = ["0.5", ".5", "00.50", "0.50a", "½", "0½", "5.5", "15", "25", "x5", "5", "", " ", "1.25", "1.75", "1.5a"];
		const string Alphabet = "0055.27½¼x a-";
		var random = new Random(403);
		string[] corpus = [.. fixedCorpus, .. Enumerable.Range(0, 3000).Select(_ => Random(random, Alphabet, 6))];
		int skipped = 0;
		var mismatches = new List<string>();
		foreach (string number in corpus)
		{
			if (CatalogOracleTests.PythonFractionBug(number))
			{
				skipped++;
				continue;
			}
			Check(mismatches, number, (string)Oracle.Call("cvdb", "__alternate_issue_num_s", number)!, IssueNumbers.Alternate(number));
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
		await Assert.That(corpus.Length - skipped).IsGreaterThan(1000);
	}

	[Test]
	public async Task FindParentPublisher_matches_find_parent_publisher_except_for_Amerotica_and_Eurotica()
	{
		string[] variants = Imprints.Names
			.SelectMany(name => new[] { name, " " + name + " ", name + " ", name.ToLowerInvariant(), name.ToUpperInvariant(), name + "x", "\t" + name + "\n" })
			.Concat(["", " ", "DC Comics", "Marvel", "Image", "Amerotica ", "Eurotica ", "Unknown Press", "Vertigo Comics"])
			.ToArray();
		int skipped = 0;
		var mismatches = new List<string>();
		foreach (string publisher in variants)
		{
			if (publisher.Trim() is "Amerotica" or "Eurotica")
			{
				skipped++;
				continue;
			}
			Check(mismatches, publisher, (string)Oracle.Call("cvimprints", "find_parent_publisher", publisher)!, Imprints.FindParentPublisher(publisher));
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
		await Assert.That(skipped).IsEqualTo(10);
	}

	[Test]
	public async Task Amerotica_and_Eurotica_never_match_in_Python_and_map_to_Nbm_in_CSharp()
	{
		foreach (string imprint in new[] { "Amerotica", "Eurotica" })
		{
			await Assert.That((string)Oracle.Call("cvimprints", "find_parent_publisher", imprint)!).IsEqualTo(imprint);
			await Assert.That(Imprints.FindParentPublisher(imprint)).IsEqualTo("Nbm");
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CleanSearchTerms_matches_cleanup_search_terms(bool alternate)
	{
		const string Alphabet = "abcdefghijklmnopqrstuvwxyzABC0123456789 &'.:-_#!?()[]ü½\t";
		string[] words = ["c2c", "ctc", "noads", "noadss", "tbp", "1", "2nd", "1st", "twelfth", "eightteenth", "twenty", "20th", "one", "&", "0th", "zero"];
		var random = new Random(290);
		string[] corpus =
		[
			"", " ", "Batman", "x-men & avengers", "C2C batman TBP", "fantastic 4", "the 1st issue", "twelfth night", "a & b", "a&b",
			.. Enumerable.Range(0, 1500).Select(_ => Random(random, Alphabet, 15)),
			.. Enumerable.Range(0, 1500).Select(_ => string.Join(random.Next(3) == 0 ? "-" : " ", Enumerable.Range(0, random.Next(1, 5)).Select(_ => words[random.Next(words.Length)]))),
		];
		var mismatches = new List<string>();
		foreach (string terms in corpus)
		{
			Check(mismatches, terms, (string)Oracle.Call("cvdb", "__cleanup_search_terms", terms, alternate)!, Pending3a.CleanSearchTerms(terms, alternate));
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
	}

	[Test]
	public async Task CleanSummary_matches_issue_parse_summary()
	{
		string[] pieces =
		[
			"Overview", "<p>", "<P >", "</p>", "<br>", "<BR/>", "<br />", "<b>", "<a href=\"x\">", "</a>", "<", ">", "&nbsp;", "&nbsp", "&amp;",
			"&quot;", "&lt;", "&gt;", "&amp;lt;", "  ", "   ", "\n", "text", "List of covers", "list OF Covers", "x", " ", "<h4>", "\r\n",
		];
		var random = new Random(628);
		string[] corpus = Enumerable.Range(0, 3000).Select(_ => string.Concat(Enumerable.Range(0, random.Next(1, 12)).Select(_ => pieces[random.Next(pieces.Length)]))).ToArray();
		var mismatches = new List<string>();
		foreach (string html in corpus)
		{
			Check(mismatches, html, (string)Oracle.Call("cvoracle", "summary", html)!, Pending3a.CleanSummary(html));
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
	}

	[Test]
	public async Task StripBackCover_matches_strip_back_cover_pixel_for_pixel()
	{
		var sizes = new List<(int Width, int Height)>();
		for (int width = 10; width <= 80; width += 7)
		{
			for (int height = 10; height <= 70; height += 9)
			{
				sizes.Add((width, height));
			}
		}
		sizes.AddRange([(121, 100), (149, 100), (150, 100), (120, 100), (131, 100), (13, 10), (3, 2)]);
		var mismatches = new List<string>();
		foreach ((int width, int height) in sizes)
		{
			foreach (float dpi in new[] { 96f, 72f })
			{
				using Bitmap python = (Bitmap)Oracle.Call("utils", "strip_back_cover", Pattern(width, height, dpi))!;
				using Bitmap csharp = Pending3a.StripBackCover(Pattern(width, height, dpi));
				Check(mismatches, $"{width}x{height}@{dpi}", Describe(python), Describe(csharp));
			}
		}

		await Assert.That(Report(mismatches)).IsEqualTo("");
		await Assert.That(sizes.Count).IsGreaterThan(80);
	}

	private static Bitmap Pattern(int width, int height, float dpi)
	{
		var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		image.SetResolution(dpi, dpi);
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				image.SetPixel(x, y, Color.FromArgb(255, (x * 37) % 256, (y * 53) % 256, (x * y) % 256));
			}
		}
		return image;
	}

	private static string Describe(Bitmap image)
	{
		var text = new StringBuilder($"{image.Width}x{image.Height} {image.PixelFormat} {image.HorizontalResolution}:");
		for (int x = 0; x < image.Width; x++)
		{
			for (int y = 0; y < image.Height; y++)
			{
				text.Append(image.GetPixel(x, y).ToArgb().ToString("x8")).Append(' ');
			}
		}
		return text.ToString();
	}

	private static bool HasSurrogatePair(string text) =>
		Enumerable.Range(0, Math.Max(0, text.Length - 1)).Any(i => char.IsSurrogatePair(text[i], text[i + 1]));

	private static string Random(Random random, string alphabet, int maxLength) =>
		new(Enumerable.Range(0, random.Next(0, maxLength + 1)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

	private static void Check(List<string> mismatches, string input, string python, string csharp)
	{
		if (python != csharp)
		{
			mismatches.Add($"input {Escape(input)}: Python {Escape(python)} C# {Escape(csharp)}");
		}
	}

	private static string Report(List<string> mismatches)
	{
		foreach (string mismatch in mismatches)
		{
			Console.WriteLine(mismatch);
		}
		return mismatches.Count == 0 ? "" : $"{mismatches.Count} mismatches (all on standard output), first:\n" + mismatches[0];
	}

	private static string Escape(string text)
	{
		string shown = string.Concat(text.Select(c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));
		return "'" + (shown.Length > 200 ? shown.Substring(0, 200) + "..." : shown) + "'";
	}
}
