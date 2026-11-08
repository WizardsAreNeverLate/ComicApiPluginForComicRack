using ComicVineScraper.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using ComicVineScraper.Config;
using ComicVineScraper.Model;

namespace ComicVineScraper.Books;

/// <summary>
/// The only code that decides what a scrape writes into a book. Ports ComicBook.update, the __massage_* rules,
/// skip_forever, and the thumbnail rule of PluginBookData.update.
/// </summary>
public static class MergeRules
{
	private static readonly BookFields EveryField = Enum.GetValues(typeof(BookFields)).Cast<BookFields>()
		.Aggregate(BookFields.None, (all, field) => all | field);

	/// <summary>One line per field, in Python's order. The patch always writes Format, IssueKey, and SeriesKey.</summary>
	public static BookPatch Plan(BookFacts book, Issue issue, MergeSettings settings, DateTime now)
	{
		BookValues old = book.Effective;
		var plan = new Planner(old, settings);
		plan.Text(BookFields.Series, issue.SeriesName, v => v.Series, (v, x) => v with { Series = x }, ignoreBlanks: true);
		plan.Text(BookFields.Number, issue.IssueNumber, v => v.Number, (v, x) => v with { Number = x }, ignoreBlanks: true);
		plan.Text(BookFields.Title, issue.Title, v => v.Title, (v, x) => v with { Title = x });
		plan.List(BookFields.Crossovers, issue.Crossovers, v => v.Crossovers, (v, x) => v with { Crossovers = x });
		plan.Text(BookFields.Summary, issue.Summary, v => v.Summary, (v, x) => v with { Summary = x });
		plan.Date(BookFields.Released, old.Released.IsBlank, issue.Released, v => v with { Released = issue.Released });
		plan.Date(BookFields.Published, old.Published.IsBlank, issue.Published, v => v with { Published = issue.Published });
		plan.Number(BookFields.Volume, old.Volume is null, issue.VolumeYear is > 0 ? issue.VolumeYear : null, v => v with { Volume = issue.VolumeYear is > 0 ? issue.VolumeYear : null });
		(string publisher, string imprint) = Publishers.Resolve(issue.Publisher, issue.Imprint, settings);
		plan.Text(BookFields.Imprint, imprint, v => v.Imprint, (v, x) => v with { Imprint = x });
		plan.Text(BookFields.Publisher, publisher, v => v.Publisher, (v, x) => v with { Publisher = x });
		plan.List(BookFields.Characters, issue.Characters, v => v.Characters, (v, x) => v with { Characters = x });
		plan.List(BookFields.Teams, issue.Teams, v => v.Teams, (v, x) => v with { Teams = x });
		plan.List(BookFields.Locations, issue.Locations, v => v.Locations, (v, x) => v with { Locations = x });
		plan.List(BookFields.Writers, issue.Writers, v => v.Writers, (v, x) => v with { Writers = x });
		plan.List(BookFields.Pencillers, issue.Pencillers, v => v.Pencillers, (v, x) => v with { Pencillers = x });
		plan.List(BookFields.Inkers, issue.Inkers, v => v.Inkers, (v, x) => v with { Inkers = x });
		plan.List(BookFields.Colorists, issue.Colorists, v => v.Colorists, (v, x) => v with { Colorists = x });
		plan.List(BookFields.Letterers, issue.Letterers, v => v.Letterers, (v, x) => v with { Letterers = x });
		plan.List(BookFields.CoverArtists, issue.CoverArtists, v => v.CoverArtists, (v, x) => v with { CoverArtists = x });
		plan.List(BookFields.Editors, issue.Editors, v => v.Editors, (v, x) => v with { Editors = x });
		plan.Text(BookFields.Webpage, issue.Webpage, v => v.Webpage, (v, x) => v with { Webpage = x });
		float? rating = issue.Rating is > 0f and <= 5f ? issue.Rating : null;
		plan.Number(BookFields.Rating, old.Rating is null, rating, v => v with { Rating = rating });
		plan.List(BookFields.Tags, KeyMarkers.WithKeyTag(old.Tags, issue.Id), v => v.Tags, (v, x) => v with { Tags = x }, overwrite: true, ignoreBlanks: false);
		plan.Text(BookFields.Notes, KeyMarkers.WithKeyNote(old.Notes, issue.Id, settings.NoteScrapeDate ? now : null), v => v.Notes,
			(v, x) => v with { Notes = x }, overwrite: true, ignoreBlanks: false);
		plan.Text(BookFields.IssueKey, issue.Id.ToString(), v => v.IssueKey, (v, x) => v with { IssueKey = x }, update: true, overwrite: true, ignoreBlanks: false);
		plan.Text(BookFields.SeriesKey, issue.SeriesId.ToString(), v => v.SeriesKey, (v, x) => v with { SeriesKey = x }, update: true, overwrite: true, ignoreBlanks: false);
		return plan.Build();
	}

	/// <summary>
	/// CVDBSKIP goes into the tags, the notes, or both: both when both or neither are enabled. Like Python, which saves
	/// every property it read, the patch writes every field with the book's effective values, so the series, number,
	/// and year parsed from the file name are kept.
	/// </summary>
	public static BookPatch PlanSkipForever(BookFacts book, MergeSettings settings, DateTime now)
	{
		bool tags = settings.Update.HasFlag(BookFields.Tags);
		bool notes = settings.Update.HasFlag(BookFields.Notes);
		BookValues values = book.Effective;
		if (tags == notes || tags)
		{
			values = values with { Tags = KeyMarkers.WithKeyTag(values.Tags, null) };
		}
		if (tags == notes || notes)
		{
			values = values with { Notes = KeyMarkers.WithKeyNote(values.Notes, null, settings.NoteScrapeDate ? now : null) };
		}
		return new BookPatch(values, DateGate.Restrict(EveryField, values));
	}

	/// <summary>The cover to install as the custom thumbnail: only for a fileless book, and never over a kept thumbnail.</summary>
	public static Uri? PlanThumbnail(BookSnapshot snapshot, Uri? cover, MergeSettings settings) =>
		cover is not null && snapshot.FilePath.Length == 0 && settings.DownloadThumbs && !(snapshot.HasCustomThumbnail && settings.PreserveThumbs)
			? cover
			: null;

	/// <summary>
	/// The massage rule: write when the field's update setting is on, and overwrite is on or the old value is blank, and
	/// not (ignore blanks and the new value is blank).
	/// </summary>
	private sealed class Planner(BookValues old, MergeSettings settings)
	{
		private readonly BookValues old = old;
		private BookValues values = old;

		// Python never drops format_s, so the ShadowFormat it read is always written to Format.
		private BookFields fields = BookFields.Format;

		public void Text(BookFields field, string incoming, Func<BookValues, string> get, Func<BookValues, string, BookValues> set,
			bool? update = null, bool? overwrite = null, bool? ignoreBlanks = null)
		{
			string next = incoming.Trim();
			Write(field, get(old).Length == 0, next.Length == 0, v => set(v, next), update, overwrite, ignoreBlanks);
		}

		public void List(BookFields field, IReadOnlyList<string> incoming, Func<BookValues, IReadOnlyList<string>> get,
			Func<BookValues, IReadOnlyList<string>, BookValues> set, bool? overwrite = null, bool? ignoreBlanks = null)
		{
			string[] next = incoming.Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
			Write(field, get(old).Count == 0, next.Length == 0, v => set(v, next), null, overwrite, ignoreBlanks);
		}

		public void Number<T>(BookFields field, bool oldBlank, T? incoming, Func<BookValues, BookValues> set) where T : struct =>
			Write(field, oldBlank, incoming is null, set, null, null, null);

		public void Date(BookFields field, bool oldBlank, PartialDate incoming, Func<BookValues, BookValues> set) =>
			Write(field, oldBlank, incoming.IsBlank, set, null, null, null);

		public BookPatch Build() => new(values, DateGate.Restrict(fields, values));

		private void Write(BookFields field, bool oldBlank, bool newBlank, Func<BookValues, BookValues> set, bool? update, bool? overwrite, bool? ignoreBlanks)
		{
			if ((update ?? settings.Update.HasFlag(field))
				&& ((overwrite ?? settings.OverwriteExisting) || oldBlank)
				&& !((ignoreBlanks ?? settings.IgnoreBlanks) && newBlank))
			{
				values = set(values);
				fields |= field;
			}
		}
	}
}

/// <summary>comicbook.py's __update_publishers.</summary>
public static class Publishers
{
	/// <summary>
	/// In Python's order: a user imprint names the parent publisher; without convert-imprints the imprint becomes the
	/// publisher; aliases rename both; an imprint equal to the publisher is dropped.
	/// </summary>
	public static (string Publisher, string Imprint) Resolve(string publisher, string imprint, MergeSettings settings)
	{
		string key = imprint.Length > 0 ? imprint : publisher;
		if (key.Length > 0 && settings.UserImprints.TryGetValue(Lower(key), out string? parent))
		{
			publisher = parent;
			imprint = key;
		}
		if (!settings.ConvertImprints && imprint.Length > 0)
		{
			publisher = imprint;
			imprint = "";
		}
		if (settings.PublisherAliases.TryGetValue(Lower(publisher), out string? publisherAlias))
		{
			publisher = publisherAlias;
		}
		if (settings.PublisherAliases.TryGetValue(Lower(imprint), out string? imprintAlias))
		{
			imprint = imprintAlias;
		}
		return (publisher, publisher == imprint ? "" : imprint);
	}

	// IronPython 2.7.4's str.lower() is ToLower(new CultureInfo("en")).
	private static string Lower(string text) => PyStr.Lower(text);
}
