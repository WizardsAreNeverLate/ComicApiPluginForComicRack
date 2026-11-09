using System;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Model;
using ComicApiPlugin.Plugin;
using cYo.Projects.ComicRack.Engine;

namespace ComicApiPlugin.Tests.Plugin;

/// <summary>
/// The cases of the original test_bookdata.py (src/py/tests), one test per Python test, run against the C# equivalents.
/// Python's BookData normalises on assignment. The port normalises when ComicRackBook.Read takes the values off a ComicBook
/// (text and lists) and in BookDate.Of (published date parts), so each case sets the ComicBook field and reads it back.
/// A Python input that has no C# form is listed as skipped inside its test, with the reason:
/// - A numeric string such as "2015", " 11 " or "2.42" has no C# form. The ComicBook properties and BookValues are typed,
///   and the string-to-number conversion belongs to Python's untyped setters.
/// - A None list item has no C# form. ComicRack stores a list as one comma-separated string, so None and "" are both an empty part.
/// Python's "del book.x" resets a property to blank. In C# that is a ComicBook that never had the field set.
/// </summary>
public sealed class BookDataGoldenTests
{
	// Python input for every credit list: [None, "Warren Ellis", "", ""]
	private const string Credits = ",Warren Ellis,,";

	private static BookValues Read(ComicBook book) => new ComicRackBook(book, new FakeApplication()).Read().Values;

	private static BookSnapshot ReadAll(ComicBook book) => new ComicRackBook(book, new FakeApplication()).Read();

	[Test]
	public async Task test_series_s()
	{
		await Assert.That(Read(new ComicBook()).Series).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Series = "bob" }).Series).IsEqualTo("bob");
	}

	[Test]
	public async Task test_issue_num_s()
	{
		await Assert.That(Read(new ComicBook()).Number).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Number = "1a" }).Number).IsEqualTo("1a");
	}

	[Test]
	public async Task test_volume_year_n()
	{
		await Assert.That(Read(new ComicBook()).Volume).IsNull();
		await Assert.That(Read(new ComicBook { Volume = 1934 }).Volume).IsEqualTo(1934);
		await Assert.That(Read(new ComicBook { Volume = -1 }).Volume).IsNull();
		// skipped: volume_year_n = "2015" (numeric string)
	}

	[Test]
	public async Task test_pub_year_n()
	{
		await Assert.That(Read(new ComicBook()).Published.Year).IsNull();
		await Assert.That(Read(new ComicBook { Year = 2012 }).Published.Year).IsEqualTo(2012);
		await Assert.That(Read(new ComicBook { Year = -1 }).Published.Year).IsNull();
		// skipped: pub_year_n = "2013" (numeric string)
	}

	[Test]
	public async Task test_pub_month_n()
	{
		await Assert.That(Read(new ComicBook()).Published.Month).IsNull();
		await Assert.That(Read(new ComicBook { Month = 9 }).Published.Month).IsEqualTo(9);
		await Assert.That(Read(new ComicBook { Month = 11 }).Published.Month).IsEqualTo(11);
		await Assert.That(Read(new ComicBook { Month = -1 }).Published.Month).IsNull();
		// skipped: pub_month_n = " 11 " (numeric string)
	}

	[Test]
	public async Task test_pub_day_n()
	{
		await Assert.That(Read(new ComicBook()).Published.Day).IsNull();
		await Assert.That(Read(new ComicBook { Day = 15 }).Published.Day).IsEqualTo(15);
		await Assert.That(Read(new ComicBook { Day = 16 }).Published.Day).IsEqualTo(16);
		await Assert.That(Read(new ComicBook { Day = -1 }).Published.Day).IsNull();
		// skipped: pub_day_n = "16" (numeric string)
	}

	[Test]
	public async Task test_rel_year_n()
	{
		await Assert.That(Read(new ComicBook()).Released.Year).IsNull();
		await Assert.That(Read(new ComicBook { ReleasedTime = new DateTime(2012, 1, 1) }).Released.Year).IsEqualTo(2012);
		await Assert.That(Read(new ComicBook { ReleasedTime = new DateTime(2013, 1, 1) }).Released.Year).IsEqualTo(2013);
		// skipped: rel_year_n = "2013" (numeric string)
	}

	[Test]
	public async Task test_rel_month_n()
	{
		await Assert.That(Read(new ComicBook()).Released.Month).IsNull();
		await Assert.That(Read(new ComicBook { ReleasedTime = new DateTime(2012, 9, 1) }).Released.Month).IsEqualTo(9);
		await Assert.That(Read(new ComicBook { ReleasedTime = new DateTime(2012, 11, 1) }).Released.Month).IsEqualTo(11);
		// skipped: rel_month_n = " 11 " (numeric string)
	}

	[Test]
	public async Task test_rel_day_n()
	{
		await Assert.That(Read(new ComicBook()).Released.Day).IsNull();
		await Assert.That(Read(new ComicBook { ReleasedTime = new DateTime(2012, 9, 15) }).Released.Day).IsEqualTo(15);
		await Assert.That(Read(new ComicBook { ReleasedTime = new DateTime(2012, 9, 16) }).Released.Day).IsEqualTo(16);
		// skipped: rel_day_n = "16" (numeric string)
	}

	[Test]
	public async Task test_format_s()
	{
		await Assert.That(Read(new ComicBook()).Format).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Format = "BIG" }).Format).IsEqualTo("BIG");
	}

	[Test]
	public async Task test_title_s()
	{
		await Assert.That(Read(new ComicBook()).Title).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Title = "    Batman!!   " }).Title).IsEqualTo("Batman!!");
	}

	[Test]
	public async Task test_crossovers_sl()
	{
		await Assert.That(Read(new ComicBook()).Crossovers).IsEmpty();
		// Python input: ["Killing Joke", None, "", "The Last Laugh"]
		await Assert.That(Read(new ComicBook { AlternateSeries = "Killing Joke,,,The Last Laugh" }).Crossovers)
			.IsEquivalentTo(["Killing Joke", "The Last Laugh"]);
	}

	[Test]
	public async Task test_summary_s()
	{
		await Assert.That(Read(new ComicBook()).Summary).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Summary = "    Batman beats up people!" }).Summary).IsEqualTo("Batman beats up people!");
	}

	[Test]
	public async Task test_publisher_s()
	{
		await Assert.That(Read(new ComicBook()).Publisher).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Publisher = " DC " }).Publisher).IsEqualTo("DC");
	}

	[Test]
	public async Task test_imprint_s()
	{
		await Assert.That(Read(new ComicBook()).Imprint).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Imprint = "Wildstorm" }).Imprint).IsEqualTo("Wildstorm");
	}

	[Test]
	public async Task test_characters_sl()
	{
		await Assert.That(Read(new ComicBook()).Characters).IsEmpty();
		// Python input: ["Hawkgirl", "", "Batman"]
		await Assert.That(Read(new ComicBook { Characters = "Hawkgirl,,Batman" }).Characters).IsEquivalentTo(["Hawkgirl", "Batman"]);
	}

	[Test]
	public async Task test_teams_sl()
	{
		await Assert.That(Read(new ComicBook()).Teams).IsEmpty();
		// Python input: [None, "The Avengers", "Justice League"]
		await Assert.That(Read(new ComicBook { Teams = ",The Avengers,Justice League" }).Teams).IsEquivalentTo(["The Avengers", "Justice League"]);
	}

	[Test]
	public async Task test_locations_sl()
	{
		await Assert.That(Read(new ComicBook()).Locations).IsEmpty();
		// Python input: [None, "Edmonton"]
		await Assert.That(Read(new ComicBook { Locations = ",Edmonton" }).Locations).IsEquivalentTo(["Edmonton"]);
	}

	[Test]
	public async Task test_writers_sl()
	{
		await Assert.That(Read(new ComicBook()).Writers).IsEmpty();
		await Assert.That(Read(new ComicBook { Writer = Credits }).Writers).IsEquivalentTo(["Warren Ellis"]);
	}

	[Test]
	public async Task test_pencillers_sl()
	{
		await Assert.That(Read(new ComicBook()).Pencillers).IsEmpty();
		await Assert.That(Read(new ComicBook { Penciller = Credits }).Pencillers).IsEquivalentTo(["Warren Ellis"]);
	}

	[Test]
	public async Task test_inkers_sl()
	{
		await Assert.That(Read(new ComicBook()).Inkers).IsEmpty();
		await Assert.That(Read(new ComicBook { Inker = Credits }).Inkers).IsEquivalentTo(["Warren Ellis"]);
	}

	[Test]
	public async Task test_colorists_sl()
	{
		await Assert.That(Read(new ComicBook()).Colorists).IsEmpty();
		await Assert.That(Read(new ComicBook { Colorist = Credits }).Colorists).IsEquivalentTo(["Warren Ellis"]);
	}

	[Test]
	public async Task test_letterers_sl()
	{
		await Assert.That(Read(new ComicBook()).Letterers).IsEmpty();
		await Assert.That(Read(new ComicBook { Letterer = Credits }).Letterers).IsEquivalentTo(["Warren Ellis"]);
	}

	[Test]
	public async Task test_cover_artists_sl()
	{
		await Assert.That(Read(new ComicBook()).CoverArtists).IsEmpty();
		await Assert.That(Read(new ComicBook { CoverArtist = Credits }).CoverArtists).IsEquivalentTo(["Warren Ellis"]);
	}

	[Test]
	public async Task test_editors_sl()
	{
		await Assert.That(Read(new ComicBook()).Editors).IsEmpty();
		await Assert.That(Read(new ComicBook { Editor = Credits }).Editors).IsEquivalentTo(["Warren Ellis"]);
	}

	[Test]
	public async Task test_tags_sl()
	{
		await Assert.That(Read(new ComicBook()).Tags).IsEmpty();
		// Python input: [None, "Hentai", "Marvel", ""]
		await Assert.That(Read(new ComicBook { Tags = ",Hentai,Marvel," }).Tags).IsEquivalentTo(["Hentai", "Marvel"]);
	}

	[Test]
	public async Task test_notes_s()
	{
		await Assert.That(Read(new ComicBook()).Notes).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Notes = " A nice note" }).Notes).IsEqualTo("A nice note");
	}

	[Test]
	public async Task test_path_s()
	{
		await Assert.That(ReadAll(new ComicBook()).FilePath).IsEqualTo("");
		await Assert.That(ReadAll(new ComicBook { FilePath = @"d:\bob\bob2\file.cbz" }).FilePath).IsEqualTo(@"d:\bob\bob2\file.cbz");
	}

	[Test]
	public async Task test_webpage_s()
	{
		await Assert.That(Read(new ComicBook()).Webpage).IsEqualTo("");
		await Assert.That(Read(new ComicBook { Web = @"https:\\batman.com" }).Webpage).IsEqualTo(@"https:\\batman.com");
	}

	[Test]
	[Skip("BookValues has no cover URL. The cover is a Uri on the Comic Vine issue, and MergeRules.PlanThumbnail passes it on as it is, so there is no normalisation to port.")]
	public Task test_cover_url_s() => Task.CompletedTask;

	[Test]
	public async Task test_rating_n()
	{
		await Assert.That(Read(new ComicBook()).Rating).IsNull();
		await Assert.That(Read(new ComicBook { CommunityRating = 3.4f }).Rating).IsEqualTo(3.4f);
		await Assert.That(Read(new ComicBook { CommunityRating = 0f }).Rating).IsNull();
		// skipped: rating_n = "2.42" (numeric string)
	}

	[Test]
	public async Task test_page_count_n()
	{
		await Assert.That(ReadAll(new ComicBook()).PageCount).IsEqualTo(0);
		// skipped: page_count_n = 3.4 (truncated to 3) and "44" (numeric string). ComicRack computes PageCount from the
		// book's page list, so the port never assigns it.
	}

	[Test]
	public async Task test_issue_key_s()
	{
		var book = new ComicBook();
		await Assert.That(Read(book).IssueKey).IsEqualTo("");
		book.SetCustomValue("comicvine_issue", "9393");
		await Assert.That(Read(book).IssueKey).IsEqualTo("9393");
	}

	[Test]
	public async Task test_series_key_s()
	{
		var book = new ComicBook();
		await Assert.That(Read(book).SeriesKey).IsEqualTo("");
		book.SetCustomValue("comicvine_volume", "9393");
		await Assert.That(Read(book).SeriesKey).IsEqualTo("9393");
	}
}
