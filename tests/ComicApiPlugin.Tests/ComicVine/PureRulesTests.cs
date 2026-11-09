using System;
using System.IO;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Tests.ComicVine;

public sealed class PureRulesTests
{
	[Test]
	[Arguments("a😀b", "a😀b")]
	[Arguments("a\uD83Db", "ab")]
	[Arguments("a\uDE00b", "ab")]
	[Arguments("\uDE00\uD83D", "")]
	[Arguments("\uD83D😀", "😀")]
	[Arguments("x\u0000\u0008\t\n\r\u001F �￾￿y", "x\t\n\r �y")]
	[Arguments("plain", "plain")]
	public async Task StripInvalidXmlChars_keeps_valid_surrogate_pairs_only(string text, string expected)
	{
		await Assert.That(ResponseText.StripInvalidXmlChars(text)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("497", "497")]
	[Arguments(" 007 ", "7")]
	[Arguments("000", "0")]
	[Arguments("0 5", "5")]
	[Arguments("0.5", ".5")]
	[Arguments("½", "½")]
	public async Task ForQuery_strips_leading_zeros(string number, string expected)
	{
		await Assert.That(IssueNumbers.ForQuery(number)).IsEqualTo(expected);
	}

	[Test]
	public async Task ForQuery_is_null_for_a_blank_number()
	{
		await Assert.That(IssueNumbers.ForQuery("  ")).IsNull();
	}

	[Test]
	[Arguments("0.5", "0½")]
	[Arguments(".5", "0½")]
	[Arguments("00.500", "0½")]
	[Arguments("½", "0½")]
	[Arguments("0½", "½")]
	[Arguments("5.5", "5½")]
	[Arguments("5.50", "5½")]
	[Arguments("5.5a", "5½")]
	[Arguments("5.25", "5¼")]
	[Arguments("5.75", "5¾")]
	[Arguments("15", "15")]
	[Arguments("25", "25")]
	[Arguments("x5", "x5")]
	[Arguments("5.55", "5.55")]
	[Arguments("1", "1")]
	public async Task Alternate_rewrites_only_real_fractions(string number, string expected)
	{
		await Assert.That(IssueNumbers.Alternate(number)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("https://comicvine.gamespot.com/batman/4050-796/", "series", 796)]
	[Arguments("http://comicvine.com/batman/49-796/", "series", 796)]
	[Arguments("4000-37566", "issue", 37566)]
	[Arguments("x 4050-12 and 4000-34 y", "issue", 34)]
	[Arguments("  4050-12  \n", "series", 12)]
	public async Task TryParse_finds_the_issue_id_before_the_series_id(string text, string kind, int id)
	{
		ComicVineTarget? target = ComicVineUrl.TryParse(text);

		await Assert.That(target).IsEqualTo(kind == "issue"
			? new ComicVineTarget.OfIssue(new IssueId(id))
			: new ComicVineTarget.OfSeries(new SeriesId(id)));
	}

	[Test]
	[Arguments("4050-7")]
	[Arguments("x4050-796")]
	[Arguments("4050-796x")]
	[Arguments("4051-796")]
	[Arguments("4050-12\nmore")]
	[Arguments("4050-00")]
	[Arguments("batman")]
	[Arguments("")]
	public async Task TryParse_rejects_text_without_a_whole_id(string text)
	{
		await Assert.That(ComicVineUrl.TryParse(text)).IsNull();
	}

	[Test]
	public async Task FindCvInfo_reads_the_file_next_to_the_book_and_prefers_cvinfo_over_cvinfo_txt()
	{
		string folder = Path.Combine(Path.GetTempPath(), "cvs-cvinfo-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		try
		{
			string book = Path.Combine(folder, "Batman 497.cbz");
			ComicVineTarget? none = ComicVineUrl.FindCvInfo(book);
			File.WriteAllText(Path.Combine(folder, "cvinfo.txt"), "﻿ https://comicvine.gamespot.com/batman/4050-796/ \r\n");
			ComicVineTarget? fromTxt = ComicVineUrl.FindCvInfo(book);
			ComicVineTarget? fromFolder = ComicVineUrl.FindCvInfo(folder);
			File.WriteAllText(Path.Combine(folder, "cvinfo"), "4000-37566");
			ComicVineTarget? fromBare = ComicVineUrl.FindCvInfo(book);

			await Assert.That(none).IsNull();
			await Assert.That(fromTxt).IsEqualTo(new ComicVineTarget.OfSeries(new SeriesId(796)));
			await Assert.That(fromFolder).IsEqualTo(fromTxt);
			await Assert.That(fromBare).IsEqualTo(new ComicVineTarget.OfIssue(new IssueId(37566)));
			await Assert.That(ComicVineUrl.FindCvInfo("")).IsNull();
			await Assert.That(ComicVineUrl.FindCvInfo(Path.Combine(folder, "missing", "x.cbz"))).IsNull();
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Test]
	[Arguments("Vertigo", "DC Comics")]
	[Arguments("  Vertigo ", "DC Comics")]
	[Arguments("Amerotica", "Nbm")]
	[Arguments("Eurotica", "Nbm")]
	[Arguments("vertigo", "vertigo")]
	[Arguments(" DC Comics ", " DC Comics ")]
	public async Task Imprints_map_to_their_parent_publisher(string publisher, string expected)
	{
		await Assert.That(Imprints.FindParentPublisher(publisher)).IsEqualTo(expected);
	}

	[Test]
	public async Task Redaction_removes_the_key_parameter_and_the_key_anywhere_else()
	{
		await Assert.That(ApiKeyRedaction.Redact("https://x/?api_key=secret&client=c echo secret", "secret"))
			.IsEqualTo("https://x/?api_key=...&client=c echo ...");
	}
}
