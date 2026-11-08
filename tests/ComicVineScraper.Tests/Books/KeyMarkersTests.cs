using System;
using System.Threading.Tasks;
using ComicVineScraper.Books;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.Books;

public sealed class KeyMarkersTests
{
	private static readonly string[] NoTags = [];
	private static readonly DateTime Now = new(2024, 1, 31, 13, 45, 10);

	[Test]
	public async Task Skip_in_tags_or_notes_wins_over_any_key()
	{
		await Assert.That(KeyMarkers.Find(["CVDB5", "cvdbSkip"], "", "7")).IsEqualTo(new StoredMarker.SkipForever());
		await Assert.That(KeyMarkers.Find(["CVDB5"], "please CVDBSKIP", "7")).IsEqualTo(new StoredMarker.SkipForever());
	}

	[Test]
	public async Task Tags_come_before_notes_and_notes_before_the_custom_value()
	{
		await Assert.That(KeyMarkers.Find(["CVDB1"], "CVDB2", "3")).IsEqualTo(new StoredMarker.Issue(new IssueId(1)));
		await Assert.That(KeyMarkers.Find(NoTags, "Scraped metadata from ComicVine [CVDB2].", "3")).IsEqualTo(new StoredMarker.Issue(new IssueId(2)));
		await Assert.That(KeyMarkers.Find(NoTags, "", " 3 ")).IsEqualTo(new StoredMarker.Issue(new IssueId(3)));
		await Assert.That(KeyMarkers.Find(NoTags, "", "")).IsEqualTo(new StoredMarker.None());
	}

	[Test]
	public async Task The_legacy_ComicVine_bracket_form_is_read()
	{
		await Assert.That(KeyMarkers.Find(["ComicVine [77"], "", "")).IsEqualTo(new StoredMarker.Issue(new IssueId(77)));
	}

	[Test]
	public async Task A_whole_number_custom_value_such_as_12_0_is_read()
	{
		// deviations.md #9: int("12.0") raises in Python.
		await Assert.That(KeyMarkers.Find(NoTags, "", "12.0")).IsEqualTo(new StoredMarker.Issue(new IssueId(12)));
		await Assert.That(KeyMarkers.Find(NoTags, "", "nan")).IsEqualTo(new StoredMarker.None());
	}

	[Test]
	public async Task A_stored_key_that_is_no_Comic_Vine_id_is_passed_over()
	{
		// deviations.md #13: Python raises on CVDB0 and keeps a key beyond int.MaxValue.
		await Assert.That(KeyMarkers.Find(["CVDB0"], "CVDB5", "")).IsEqualTo(new StoredMarker.Issue(new IssueId(5)));
		await Assert.That(KeyMarkers.Find(["CVDB99999999999"], "", "7")).IsEqualTo(new StoredMarker.Issue(new IssueId(7)));
	}

	[Test]
	public async Task The_key_tag_replaces_the_previous_one_or_is_appended()
	{
		await Assert.That(KeyMarkers.WithKeyTag(["Marvel", "cvdb12"], new IssueId(99))).IsEquivalentTo(["Marvel", "CVDB99"]);
		await Assert.That(KeyMarkers.WithKeyTag(["Marvel"], new IssueId(99))).IsEquivalentTo(["Marvel", "CVDB99"]);
		await Assert.That(KeyMarkers.WithKeyTag(NoTags, new IssueId(99))).IsEquivalentTo(["CVDB99"]);
		await Assert.That(KeyMarkers.WithKeyTag(["CVDB12"], null)).IsEquivalentTo(["CVDBSKIP"]);
	}

	[Test]
	public async Task The_key_note_is_written_with_an_invariant_time()
	{
		await Assert.That(KeyMarkers.WithKeyNote("", new IssueId(5), Now))
			.IsEqualTo("Scraped metadata from ComicVine [CVDB5] on 2024.01.31 13:45:10.");
		await Assert.That(KeyMarkers.WithKeyNote(" My note ", new IssueId(5), null))
			.IsEqualTo("My note\n\nScraped metadata from ComicVine [CVDB5].");
	}

	[Test]
	public async Task An_old_note_is_replaced_whole()
	{
		string old = "Mine\n\nScraped metadata from ComicVine [CVDB7] on 1392.02.24 21:43:06.\n\nAfter";

		string updated = KeyMarkers.WithKeyNote(old, new IssueId(8), Now);

		await Assert.That(updated).IsEqualTo("Mine\n\nScraped metadata from ComicVine [CVDB8] on 2024.01.31 13:45:10.\n\nAfter");
	}

	[Test]
	public async Task A_note_whose_time_Python_did_not_write_keeps_its_text_and_gets_the_new_tag()
	{
		// IronPython 2.7.4 always wrote %X as HH:mm:ss, so a 12-hour time came from elsewhere; Python then replaced only the tag.
		string old = "Scraped metadata from ComicVine [CVDB7] on 2013.05.14 9:43:06 PM.";

		await Assert.That(KeyMarkers.WithKeyNote(old, new IssueId(8), Now))
			.IsEqualTo("Scraped metadata from ComicVine [CVDB8] on 2013.05.14 9:43:06 PM.");
	}

	[Test]
	public async Task A_key_tag_typed_into_the_notes_by_hand_is_replaced_in_place()
	{
		await Assert.That(KeyMarkers.WithKeyNote("see CVDB7 here", new IssueId(8), Now)).IsEqualTo("see CVDB8 here");
	}
}
