using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Model;
using ComicVineScraper.Plugin;
using ComicVineScraper.Tests.Books;
using cYo.Projects.ComicRack.Engine;

namespace ComicVineScraper.Tests.Plugin;

public sealed class ComicRackBookTests
{
	private static readonly BookValues Sample = new()
	{
		Series = "Batman",
		Number = "12",
		Volume = 1990,
		Format = "Annual",
		Title = "The Joker",
		Crossovers = ["Knightfall", "Zero Hour"],
		Summary = "Batman beats up people!",
		Publisher = "DC Comics",
		Imprint = "Vertigo",
		Characters = ["Batman", "Robin"],
		Teams = ["Justice League"],
		Locations = ["Gotham"],
		Writers = ["Grant Morrison"],
		Pencillers = ["Jim Lee"],
		Inkers = ["Scott Williams"],
		Colorists = ["Alex Sinclair"],
		Letterers = ["Richard Starkings"],
		CoverArtists = ["Jim Lee", "Alex Ross"],
		Editors = ["Bob Schreck"],
		Tags = ["Favourite", "CVDB12345"],
		Notes = "Scraped metadata from ComicVine [CVDB12345].",
		Webpage = "https://comicvine.gamespot.com/batman-12/4000-12345/",
		Rating = 3.5f,
		IssueKey = "12345",
		SeriesKey = "796",
		Released = PartialDate.Of(1990, 4, 17),
		Published = BookDate.Of(1990, 6, 1),
	};

	[Test]
	public async Task Every_single_field_bit_has_a_projection()
	{
		await Assert.That(BookValuesDiff.SingleFields.Where(field => !BookValuesDiff.Projections.ContainsKey(field))).IsEmpty();
	}

	[Test]
	public async Task Each_field_bit_writes_its_own_field_and_no_other()
	{
		List<string> wrong = [];
		foreach (BookFields field in BookValuesDiff.SingleFields)
		{
			var adapter = new ComicRackBook(new ComicBook(), new FakeApplication());
			BookValues before = adapter.Read().Values;

			adapter.Apply(new BookPatch(Sample, field), thumbnail: null);

			BookFields changed = BookValuesDiff.Changed(before, adapter.Read().Values);
			if (changed != field)
			{
				wrong.Add($"{field} changed {changed}");
			}
		}

		await Assert.That(wrong).IsEmpty();
	}

	[Test]
	public async Task Read_after_writing_every_field_returns_the_written_values()
	{
		var adapter = new ComicRackBook(new ComicBook(), new FakeApplication());

		adapter.Apply(new BookPatch(Sample, BookValuesDiff.AllFields), thumbnail: null);

		await Assert.That(BookValuesDiff.Changed(Sample, adapter.Read().Values)).IsEqualTo(BookFields.None);
	}

	[Test]
	public async Task Applying_what_was_read_changes_nothing()
	{
		var book = new ComicBook();
		var adapter = new ComicRackBook(book, new FakeApplication());
		adapter.Apply(new BookPatch(Sample, BookValuesDiff.AllFields), thumbnail: null);
		BookValues first = adapter.Read().Values;
		string before = Dump(book);

		adapter.Apply(new BookPatch(first, BookValuesDiff.AllFields), thumbnail: null);

		await Assert.That(BookValuesDiff.Changed(first, adapter.Read().Values)).IsEqualTo(BookFields.None);
		await Assert.That(Dump(book)).IsEqualTo(before);
	}

	[Test]
	public async Task Read_strips_text_like_bookdata()
	{
		var book = new ComicBook
		{
			Series = " Batman ",
			Number = " 1a ",
			Title = "    Batman!!   ",
			Summary = "    Batman beats up people!",
			Publisher = " DC ",
			Imprint = "Wildstorm\t",
			Notes = " A nice note",
			Web = " https:\\batman.com ",
		};
		book.SetCustomValue("comicvine_issue", " 9393 ");
		book.SetCustomValue("comicvine_volume", "9393\n");

		BookValues values = new ComicRackBook(book, new FakeApplication()).Read().Values;

		await Assert.That(values.Series).IsEqualTo("Batman");
		await Assert.That(values.Number).IsEqualTo("1a");
		await Assert.That(values.Title).IsEqualTo("Batman!!");
		await Assert.That(values.Summary).IsEqualTo("Batman beats up people!");
		await Assert.That(values.Publisher).IsEqualTo("DC");
		await Assert.That(values.Imprint).IsEqualTo("Wildstorm");
		await Assert.That(values.Notes).IsEqualTo("A nice note");
		await Assert.That(values.Webpage).IsEqualTo("https:\\batman.com");
		await Assert.That(values.IssueKey).IsEqualTo("9393");
		await Assert.That(values.SeriesKey).IsEqualTo("9393");
	}

	[Test]
	public async Task Read_splits_lists_on_commas_and_drops_blank_items_like_bookdata()
	{
		var book = new ComicBook
		{
			AlternateSeries = "Killing Joke,,,The Last Laugh",
			Characters = "Hawkgirl, ,Batman",
			Teams = ",The Avengers, Justice League",
			Locations = ",Edmonton",
			Writer = ",Warren Ellis,,",
			Tags = ",Hentai, Marvel,",
		};

		BookValues values = new ComicRackBook(book, new FakeApplication()).Read().Values;

		await Assert.That(values.Crossovers).IsEquivalentTo(["Killing Joke", "The Last Laugh"]);
		await Assert.That(values.Characters).IsEquivalentTo(["Hawkgirl", "Batman"]);
		await Assert.That(values.Teams).IsEquivalentTo(["The Avengers", "Justice League"]);
		await Assert.That(values.Locations).IsEquivalentTo(["Edmonton"]);
		await Assert.That(values.Writers).IsEquivalentTo(["Warren Ellis"]);
		await Assert.That(values.Tags).IsEquivalentTo(["Hentai", "Marvel"]);
	}

	[Test]
	public async Task Read_takes_volume_and_format_from_the_shadow_values()
	{
		var book = new ComicBook { Volume = 1934, Format = " Giant " };

		BookValues values = new ComicRackBook(book, new FakeApplication()).Read().Values;

		await Assert.That(values.Volume).IsEqualTo(1934);
		await Assert.That(values.Format).IsEqualTo("Giant");
	}

	[Test]
	[Arguments(-1, null)]
	[Arguments(-7, null)]
	[Arguments(0, 0)]
	[Arguments(2015, 2015)]
	public async Task Read_keeps_a_volume_of_zero_or_more(int volume, int? expected)
	{
		BookValues values = new ComicRackBook(new ComicBook { Volume = volume }, new FakeApplication()).Read().Values;

		await Assert.That(values.Volume).IsEqualTo(expected);
	}

	[Test]
	public async Task Read_treats_a_rating_of_zero_as_blank()
	{
		BookValues rated = new ComicRackBook(new ComicBook { CommunityRating = 3.4f }, new FakeApplication()).Read().Values;
		BookValues unrated = new ComicRackBook(new ComicBook(), new FakeApplication()).Read().Values;

		await Assert.That(rated.Rating).IsEqualTo(3.4f);
		await Assert.That(unrated.Rating).IsNull();
	}

	[Test]
	public async Task Read_treats_an_unset_release_date_as_blank()
	{
		BookValues unset = new ComicRackBook(new ComicBook(), new FakeApplication()).Read().Values;
		BookValues set = new ComicRackBook(new ComicBook { ReleasedTime = new DateTime(2010, 5, 3) }, new FakeApplication()).Read().Values;

		await Assert.That(unset.Released.IsBlank).IsTrue();
		await Assert.That(set.Released).IsEqualTo(PartialDate.Of(2010, 5, 3));
	}

	[Test]
	public async Task Read_keeps_a_published_month_that_has_no_year()
	{
		BookValues values = new ComicRackBook(new ComicBook { Year = -1, Month = 5, Day = -1 }, new FakeApplication()).Read().Values;

		await Assert.That(values.Published).IsEqualTo(BookDate.Of(null, 5, null));
	}

	[Test]
	public async Task Read_reports_the_path_page_count_and_custom_thumbnail()
	{
		BookSnapshot fileless = new ComicRackBook(new ComicBook { CustomThumbnailKey = "thumb" }, new FakeApplication()).Read();
		BookSnapshot linked = new ComicRackBook(new ComicBook { FilePath = @"C:\Comics\Batman 012.cbz" }, new FakeApplication()).Read();

		await Assert.That(fileless.FilePath).IsEqualTo("");
		await Assert.That(fileless.HasCustomThumbnail).IsTrue();
		await Assert.That(linked.FilePath).IsEqualTo(@"C:\Comics\Batman 012.cbz");
		await Assert.That(linked.HasCustomThumbnail).IsFalse();
	}

	[Test]
	public async Task Apply_removes_commas_inside_list_items_like_pluginbookdata()
	{
		var book = new ComicBook();

		new ComicRackBook(book, new FakeApplication()).Apply(
			new BookPatch(new BookValues { Writers = ["Smith, John", "a,b", "x,", "Jo,\tAnn"] }, BookFields.Writers), thumbnail: null);

		await Assert.That(book.Writer).IsEqualTo("Smith John, a b, x , Jo\tAnn");
	}

	[Test]
	public async Task Apply_writes_blank_volume_rating_and_custom_values_as_cleared()
	{
		var book = new ComicBook { Volume = 1990, CommunityRating = 4f };
		book.SetCustomValue("comicvine_issue", "5");

		new ComicRackBook(book, new FakeApplication()).Apply(
			new BookPatch(new BookValues(), BookFields.Volume | BookFields.Rating | BookFields.IssueKey), thumbnail: null);

		await Assert.That(book.Volume).IsEqualTo(-1);
		await Assert.That(book.CommunityRating).IsEqualTo(0f);
		await Assert.That(book.GetCustomValue("comicvine_issue")).IsNull();
	}

	[Test]
	[Arguments(BookFields.PublishedMonth)]
	[Arguments(BookFields.Released)]
	public async Task Apply_rejects_a_patch_with_an_ungated_date_and_writes_nothing(BookFields dateField)
	{
		var book = new ComicBook { Series = "Before" };
		var patch = new BookPatch(
			new BookValues { Series = "After", Published = BookDate.Of(null, 5, null), Released = PartialDate.Of(2010, 2, 31) },
			BookFields.Series | dateField);

		await Assert.That(() => new ComicRackBook(book, new FakeApplication()).Apply(patch, thumbnail: null)).Throws<ArgumentException>();
		await Assert.That(book.Series).IsEqualTo("Before");
	}

	[Test]
	public async Task Apply_sets_the_thumbnail_after_the_fields()
	{
		var app = new FakeApplication();
		var book = new ComicBook();
		using var thumbnail = new Bitmap(4, 4);

		new ComicRackBook(book, app).Apply(new BookPatch(new BookValues { Series = "Batman" }, BookFields.Series), thumbnail);

		await Assert.That(app.Thumbnails.Count).IsEqualTo(1);
		await Assert.That(app.Thumbnails[0].Book).IsSameReferenceAs(book);
		await Assert.That(app.Thumbnails[0].Image).IsSameReferenceAs(thumbnail);
		await Assert.That(app.SeriesWhenThumbnailSet).IsEqualTo("Batman");
	}

	[Test]
	public async Task Apply_does_not_throw_when_ComicRack_refuses_the_thumbnail()
	{
		var app = new FakeApplication { Accepts = false };
		using var thumbnail = new Bitmap(4, 4);

		new ComicRackBook(new ComicBook(), app).Apply(new BookPatch(new BookValues(), BookFields.None), thumbnail);

		await Assert.That(app.Thumbnails.Count).IsEqualTo(1);
	}

	internal static string Dump(ComicBook book) => string.Join("|",
		book.Series, book.Number, book.Volume, book.Format, book.Title, book.AlternateSeries, book.Summary, book.Publisher, book.Imprint,
		book.Characters, book.Teams, book.Locations, book.Writer, book.Penciller, book.Inker, book.Colorist, book.Letterer,
		book.CoverArtist, book.Editor, book.Tags, book.Notes, book.Web, book.CommunityRating, book.CustomValuesStore,
		book.ReleasedTime.Ticks, book.Year, book.Month, book.Day);
}
