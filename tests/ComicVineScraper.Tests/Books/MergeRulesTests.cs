using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Config;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Books;

public sealed class MergeRulesTests
{
	private static readonly DateTime Now = new(2024, 1, 31, 13, 45, 10);

	private static readonly Issue Sample = new(
		new IssueId(12345), new SeriesId(796), "Batman", 1990, "12", "The Joker", "Summary.", "DC Comics", "",
		PartialDate.Of(1990, 6, 1), PartialDate.Of(1990, 4, 17), ["Knightfall"], ["Batman"], ["JLA"], ["Gotham"], ["Grant Morrison"],
		["Jim Lee"], ["Scott Williams"], ["Alex Sinclair"], ["Richard Starkings"], ["Jim Lee"], ["Bob Schreck"],
		"https://comicvine.gamespot.com/batman-12/4000-12345/", 3.5f, []);

	private static MergeSettings Settings(
		BookFields? update = null, bool overwrite = true, bool ignoreBlanks = false, bool convertImprints = true, bool noteDate = false,
		Dictionary<string, string>? imprints = null, Dictionary<string, string>? aliases = null, bool downloadThumbs = true, bool preserveThumbs = true) =>
		new(update ?? FieldTable.All | BookFields.Rating | BookFields.Notes, overwrite, ignoreBlanks, convertImprints, downloadThumbs, preserveThumbs, noteDate,
			imprints ?? [], aliases ?? []);

	private static BookFacts Facts(BookValues values, string path = "") => new(0, new BookSnapshot(values, path, 0, false), values, new StoredMarker.None(), null, "key");

	private static MergeSettings FromConfiguration(bool overwrite, bool ignoreBlanks) => new Configuration
	{
		Advanced = new AdvancedSettings(
			RawText: "", IgnoredPublishers: [], IgnoredSearchTerms: [], IgnoreBeforeYear: 1950, IgnoreAfterYear: 2030, NeverIgnoreThreshold: 5,
			ScrapeRating: false, ShowCovers: true, WelcomeDialog: true, AltSearchRegex: null, IgnoreFolders: false, ForceSeriesArt: false,
			NoteScrapeDate: false, PublisherAliases: new Dictionary<string, string>(), UserImprints: new Dictionary<string, string>(),
			ScrapeDelaySeconds: 2, MaxSearchResults: 100),
		OverwriteExisting = overwrite,
		IgnoreBlanks = ignoreBlanks,
	}.ToMergeSettings();

	[Test]
	public async Task A_full_merge_writes_every_enabled_field_from_the_issue()
	{
		BookPatch patch = MergeRules.Plan(Facts(new BookValues()), Sample, Settings(), Now);

		await Assert.That(patch.Values.Series).IsEqualTo("Batman");
		await Assert.That(patch.Values.Writers).IsEquivalentTo(["Grant Morrison"]);
		await Assert.That(patch.Values.Published).IsEqualTo(BookDate.Of(1990, 6, 1));
		await Assert.That(patch.Values.Volume).IsEqualTo(1990);
		await Assert.That(patch.Values.Rating).IsEqualTo(3.5f);
		await Assert.That(patch.Values.Notes).IsEqualTo("Scraped metadata from ComicVine [CVDB12345].");
		await Assert.That(patch.Values.IssueKey).IsEqualTo("12345");
		await Assert.That(patch.Values.SeriesKey).IsEqualTo("796");
		await Assert.That(patch.Fields).IsEqualTo(BookValuesDiff.AllFields & ~BookFields.Tags);
	}

	[Test]
	public async Task Without_overwrite_a_field_that_has_a_value_is_kept()
	{
		var old = new BookValues { Title = "Mine", Writers = ["Me"], Volume = 0, Rating = 1f, Published = BookDate.Of(null, 5, null) };

		BookPatch patch = MergeRules.Plan(Facts(old), Sample, Settings(overwrite: false), Now);

		await Assert.That(patch.Fields & (BookFields.Title | BookFields.Writers | BookFields.Volume | BookFields.Rating | BookFields.Published))
			.IsEqualTo(BookFields.None);
		await Assert.That(patch.Fields.HasFlag(BookFields.Summary)).IsTrue();
	}

	[Test]
	public async Task Ignore_blanks_keeps_a_value_the_issue_lacks()
	{
		var old = new BookValues { Title = "Mine", Writers = ["Me"], Volume = 1980, Rating = 1f, Published = BookDate.Of(1980, 1, 1) };
		Issue blank = Sample with { Title = " ", Writers = [], VolumeYear = 0, Rating = 0f, Published = PartialDate.None };

		BookPatch kept = MergeRules.Plan(Facts(old), blank, Settings(ignoreBlanks: true), Now);
		BookPatch cleared = MergeRules.Plan(Facts(old), blank, Settings(ignoreBlanks: false), Now);

		await Assert.That(kept.Fields & (BookFields.Title | BookFields.Writers | BookFields.Volume | BookFields.Rating)).IsEqualTo(BookFields.None);
		await Assert.That(cleared.Fields & (BookFields.Title | BookFields.Writers | BookFields.Volume | BookFields.Rating))
			.IsEqualTo(BookFields.Title | BookFields.Writers | BookFields.Volume | BookFields.Rating);
		await Assert.That(cleared.Values.Volume).IsNull();
		await Assert.That(cleared.Values.Rating).IsNull();
		await Assert.That(cleared.Fields & BookFields.Published).IsEqualTo(BookFields.None);
	}

	[Test]
	public async Task Ignore_blanks_applies_without_overwrite_as_python_reads_it()
	{
		// ConfigForm saves the checkbox even while it is disabled, and ComicBook.update reads it without looking at overwrite.
		var old = new BookValues { Summary = "" };
		Issue blank = Sample with { Summary = " " };

		BookPatch kept = MergeRules.Plan(Facts(old), blank, FromConfiguration(overwrite: false, ignoreBlanks: true), Now);
		BookPatch written = MergeRules.Plan(Facts(old), blank, FromConfiguration(overwrite: false, ignoreBlanks: false), Now);

		await Assert.That(kept.Fields.HasFlag(BookFields.Summary)).IsFalse();
		await Assert.That(written.Fields.HasFlag(BookFields.Summary)).IsTrue();
	}

	[Test]
	public async Task Series_and_number_always_ignore_blanks()
	{
		BookPatch patch = MergeRules.Plan(Facts(new BookValues { Series = "Mine", Number = "1" }), Sample with { SeriesName = "", IssueNumber = " " },
			Settings(ignoreBlanks: false), Now);

		await Assert.That(patch.Fields & (BookFields.Series | BookFields.Number)).IsEqualTo(BookFields.None);
	}

	[Test]
	public async Task A_field_whose_update_setting_is_off_is_not_written()
	{
		BookPatch patch = MergeRules.Plan(Facts(new BookValues()), Sample, Settings(update: BookFields.Series), Now);

		await Assert.That(patch.Fields).IsEqualTo(BookFields.Series | BookFields.Format | BookFields.IssueKey | BookFields.SeriesKey);
	}

	[Test]
	public async Task An_unset_release_date_is_filled_without_overwrite()
	{
		// deviations.md #11: Python read the unset date as 0001-01-01, which is not blank.
		BookPatch patch = MergeRules.Plan(Facts(new BookValues { Released = PartialDate.None }), Sample, Settings(overwrite: false), Now);

		await Assert.That(patch.Fields.HasFlag(BookFields.Released)).IsTrue();
		await Assert.That(patch.Values.Released).IsEqualTo(PartialDate.Of(1990, 4, 17));
	}

	[Test]
	public async Task A_release_date_is_written_only_when_complete_and_real()
	{
		BookPatch partial = MergeRules.Plan(Facts(new BookValues()), Sample with { Released = PartialDate.Of(1990, 4, null) }, Settings(), Now);
		BookPatch unreal = MergeRules.Plan(Facts(new BookValues()), Sample with { Released = PartialDate.Of(2010, 2, 31) }, Settings(), Now);
		BookPatch yearZero = MergeRules.Plan(Facts(new BookValues()), Sample with { Released = PartialDate.Of(0, 2, 3) }, Settings(), Now);

		await Assert.That(partial.Fields.HasFlag(BookFields.Released)).IsFalse();
		await Assert.That(unreal.Fields.HasFlag(BookFields.Released)).IsFalse();
		await Assert.That(yearZero.Fields.HasFlag(BookFields.Released)).IsFalse();
	}

	[Test]
	public async Task A_published_date_writes_only_the_parts_it_knows()
	{
		var old = new BookValues { Published = BookDate.Of(2009, 5, 3) };

		BookPatch patch = MergeRules.Plan(Facts(old), Sample with { Published = PartialDate.Of(2010, null, null) }, Settings(), Now);

		await Assert.That(patch.Fields & BookFields.Published).IsEqualTo(BookFields.PublishedYear);
		await Assert.That(patch.Values.Published.Year).IsEqualTo(2010);
	}

	[Test]
	public async Task Volume_must_be_above_zero_and_rating_within_zero_to_five()
	{
		BookPatch patch = MergeRules.Plan(Facts(new BookValues()), Sample with { VolumeYear = 0, Rating = 7f }, Settings(), Now);

		await Assert.That(patch.Values.Volume).IsNull();
		await Assert.That(patch.Values.Rating).IsNull();
	}

	[Test]
	public async Task Tags_and_notes_always_overwrite_when_enabled()
	{
		var old = new BookValues { Tags = ["Mine", "CVDB1"], Notes = "Scraped metadata from ComicVine [CVDB1]." };

		BookPatch patch = MergeRules.Plan(Facts(old), Sample, Settings(update: BookFields.Tags | BookFields.Notes, overwrite: false, noteDate: true), Now);

		await Assert.That(patch.Values.Tags).IsEquivalentTo(["Mine", "CVDB12345"]);
		await Assert.That(patch.Values.Notes).IsEqualTo("Scraped metadata from ComicVine [CVDB12345] on 2024.01.31 13:45:10.");
		await Assert.That(patch.Fields & (BookFields.Tags | BookFields.Notes)).IsEqualTo(BookFields.Tags | BookFields.Notes);
	}

	[Test]
	public async Task The_ids_and_the_format_are_always_written()
	{
		var old = new BookValues { Format = "Annual", IssueKey = "1", SeriesKey = "2" };

		BookPatch patch = MergeRules.Plan(Facts(old), Sample, Settings(update: BookFields.None, overwrite: false), Now);

		await Assert.That(patch.Fields).IsEqualTo(BookFields.Format | BookFields.IssueKey | BookFields.SeriesKey);
		await Assert.That(patch.Values.Format).IsEqualTo("Annual");
		await Assert.That(patch.Values.IssueKey).IsEqualTo("12345");
	}

	[Test]
	[Arguments("DC Comics", "Vertigo", true, "DC Comics", "Vertigo")]
	[Arguments("DC Comics", "Vertigo", false, "Vertigo", "")]
	[Arguments("Marvel", "", true, "Marvel Worldwide", "")]
	[Arguments("Marvel", "Max", true, "Marvel Worldwide", "MAX Comics")]
	[Arguments("Panini", "Icon", true, "Marvel Worldwide", "Icon")]
	[Arguments("Panini", "Icon", false, "Icon", "")]
	[Arguments("Image", "Image", true, "Image", "")]
	public async Task Publishers_map_imprints_then_aliases_then_drop_an_imprint_equal_to_the_publisher(
		string publisher, string imprint, bool convert, string expectedPublisher, string expectedImprint)
	{
		var imprints = new Dictionary<string, string> { ["icon"] = "Marvel" };
		var aliases = new Dictionary<string, string> { ["marvel"] = "Marvel Worldwide", ["max"] = "MAX Comics" };

		(string mappedPublisher, string mappedImprint) = Publishers.Resolve(publisher, imprint, Settings(convertImprints: convert, imprints: imprints, aliases: aliases));

		await Assert.That(mappedPublisher).IsEqualTo(expectedPublisher);
		await Assert.That(mappedImprint).IsEqualTo(expectedImprint);
	}

	[Test]
	public async Task Skip_forever_writes_the_skip_marker_and_every_field_as_read()
	{
		var effective = new BookValues { Series = "From file", Tags = ["CVDB12"], Notes = "Mine", Volume = 1990, Published = BookDate.Of(1990, null, null) };

		BookPatch patch = MergeRules.PlanSkipForever(Facts(effective), Settings(update: BookFields.Notes), Now);

		await Assert.That(patch.Values.Tags).IsEquivalentTo(["CVDB12"]);
		await Assert.That(patch.Values.Notes).IsEqualTo("Mine\n\nScraped metadata from ComicVine [CVDBSKIP].");
		await Assert.That(patch.Values.Series).IsEqualTo("From file");
		await Assert.That(patch.Fields).IsEqualTo(BookValuesDiff.AllFields & ~(BookFields.Released | BookFields.PublishedMonth | BookFields.PublishedDay));
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, true)]
	public async Task Skip_forever_marks_both_fields_when_both_or_neither_are_enabled(bool tags, bool notes)
	{
		BookFields update = (tags ? BookFields.Tags : BookFields.None) | (notes ? BookFields.Notes : BookFields.None);

		BookPatch patch = MergeRules.PlanSkipForever(Facts(new BookValues()), Settings(update: update), Now);

		await Assert.That(patch.Values.Tags).IsEquivalentTo(["CVDBSKIP"]);
		await Assert.That(patch.Values.Notes).IsEqualTo("Scraped metadata from ComicVine [CVDBSKIP].");
	}

	[Test]
	public async Task A_thumbnail_is_planned_only_for_a_fileless_book_that_may_take_one()
	{
		var cover = new Uri("https://comicvine.gamespot.com/a.jpg");
		var fileless = new BookSnapshot(new BookValues(), "", 0, false);
		var thumbed = fileless with { HasCustomThumbnail = true };
		var linked = fileless with { FilePath = @"C:\a.cbz" };

		await Assert.That(MergeRules.PlanThumbnail(fileless, cover, Settings())).IsEqualTo(cover);
		await Assert.That(MergeRules.PlanThumbnail(fileless, null, Settings())).IsNull();
		await Assert.That(MergeRules.PlanThumbnail(linked, cover, Settings())).IsNull();
		await Assert.That(MergeRules.PlanThumbnail(fileless, cover, Settings(downloadThumbs: false, preserveThumbs: false))).IsNull();
		await Assert.That(MergeRules.PlanThumbnail(thumbed, cover, Settings())).IsNull();
		await Assert.That(MergeRules.PlanThumbnail(thumbed, cover, Settings(preserveThumbs: false))).IsEqualTo(cover);
	}
}
