using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ComicApiPlugin.Books;
using ComicApiPlugin.Config;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Tests.Books;

public sealed class MergeRulesVerseDbTests
{
	private static readonly BookFields IdentityFields = BookFields.Tags | BookFields.Notes | BookFields.IssueKey | BookFields.SeriesKey;

	private static readonly IssueMetadata Sample = new(
		"Batman", 1990, "12", "The Joker", "Summary.", "DC Comics", "", PartialDate.Of(1990, 6, 1), PartialDate.Of(1990, 4, 17),
		["Knightfall"], ["Batman"], ["JLA"], ["Gotham"], ["Grant Morrison"], ["Jim Lee"], ["Scott Williams"], ["Alex Sinclair"],
		["Richard Starkings"], ["Jim Lee"], ["Bob Schreck"], "", 3.5f);

	private static MergeSettings Settings(bool overwrite = true, bool ignoreBlanks = false) => new(
		FieldTable.All | BookFields.Rating | BookFields.Notes | BookFields.Tags, overwrite, ignoreBlanks, true, true, true, true,
		new Dictionary<string, string>(), new Dictionary<string, string>());

	private static BookFacts Facts(BookValues values) => new(0, new BookSnapshot(values, "", 0, false), values, new StoredMarker.None(), null, "key");

	[Test]
	public async Task It_writes_the_metadata_fields_the_settings_allow()
	{
		BookPatch patch = MergeRules.PlanFromVerseDb(Facts(new BookValues()), Sample, Settings());

		await Assert.That(patch.Values.Series).IsEqualTo("Batman");
		await Assert.That(patch.Values.Title).IsEqualTo("The Joker");
		await Assert.That(patch.Values.Writers).IsEquivalentTo(["Grant Morrison"]);
		await Assert.That(patch.Values.Published).IsEqualTo(BookDate.Of(1990, 6, 1));
		await Assert.That(patch.Values.Rating).IsEqualTo(3.5f);
	}

	[Test]
	public async Task It_never_touches_the_Comic_Vine_markers()
	{
		var old = new BookValues { Tags = ["keep", "CVDB99"], Notes = "Scraped metadata from ComicVine [CVDB99].", IssueKey = "99", SeriesKey = "7" };

		BookPatch patch = MergeRules.PlanFromVerseDb(Facts(old), Sample, Settings());

		await Assert.That(patch.Fields & IdentityFields).IsEqualTo(BookFields.None);
		await Assert.That(patch.Values.Tags).IsEquivalentTo(old.Tags);
		await Assert.That(patch.Values.Notes).IsEqualTo(old.Notes);
		await Assert.That(patch.Values.IssueKey).IsEqualTo("99");
		await Assert.That(patch.Values.SeriesKey).IsEqualTo("7");
	}

	[Test]
	public async Task Without_overwrite_a_field_that_has_a_value_is_kept()
	{
		BookPatch patch = MergeRules.PlanFromVerseDb(Facts(new BookValues { Title = "Mine" }), Sample, Settings(overwrite: false));

		await Assert.That(patch.Fields.HasFlag(BookFields.Title)).IsFalse();
		await Assert.That(patch.Fields.HasFlag(BookFields.Summary)).IsTrue();
	}

	[Test]
	public async Task Ignore_blanks_keeps_the_old_value_when_VerseDB_has_none()
	{
		BookPatch patch = MergeRules.PlanFromVerseDb(Facts(new BookValues { Publisher = "Mine" }), Sample with { Publisher = "" }, Settings(ignoreBlanks: true));

		await Assert.That(patch.Fields.HasFlag(BookFields.Publisher)).IsFalse();
	}

	[Test]
	public async Task Plan_for_Comic_Vine_is_the_VerseDB_plan_plus_the_markers()
	{
		var issue = new Issue(new IssueId(5), new SeriesId(6), Sample.SeriesName, Sample.VolumeYear, Sample.IssueNumber, Sample.Title, Sample.Summary,
			Sample.Publisher, Sample.Imprint, Sample.Published, Sample.Released, Sample.Crossovers, Sample.Characters, Sample.Teams, Sample.Locations,
			Sample.Writers, Sample.Pencillers, Sample.Inkers, Sample.Colorists, Sample.Letterers, Sample.CoverArtists, Sample.Editors, Sample.Webpage,
			Sample.Rating, []);
		BookFacts facts = Facts(new BookValues());

		BookPatch comicVine = MergeRules.Plan(facts, issue, Settings(), new DateTime(2024, 1, 1));
		BookPatch verseDb = MergeRules.PlanFromVerseDb(facts, Sample, Settings());

		await Assert.That(comicVine.Fields & ~IdentityFields).IsEqualTo(verseDb.Fields);
		await Assert.That(comicVine.Fields.HasFlag(BookFields.IssueKey)).IsTrue();
	}
}
