using ComicApiPlugin.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ComicApiPlugin.Config;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Books;

/// <summary>
/// The only code that decides what a scrape writes into a book. Ports ComicBook.update, the __massage_* rules,
/// skip_forever, and the thumbnail rule of PluginBookData.update.
/// </summary>
public static class MergeRules
{
	private static readonly BookFields EveryField = Enum.GetValues(typeof(BookFields)).Cast<BookFields>()
		.Aggregate(BookFields.None, (all, field) => all | field);

	/// <summary>
	/// One decision per field, in Python's order, on the patch. The patch always writes Format, IssueKey, and SeriesKey.
	/// Plan only decides. The engine writes the decisions to the debug log.
	/// </summary>
	public static BookPatch Plan(BookFacts book, Issue issue, MergeSettings settings, DateTime now)
	{
		BookValues old = book.Effective;
		var plan = new Planner(old, settings);
		plan.Text("Series", BookFields.Series, issue.SeriesName, v => v.Series, (v, x) => v with { Series = x }, ignoreBlanks: true);
		plan.Text("Issue Number", BookFields.Number, issue.IssueNumber, v => v.Number, (v, x) => v with { Number = x }, ignoreBlanks: true);
		plan.Text("Title", BookFields.Title, issue.Title, v => v.Title, (v, x) => v with { Title = x });
		plan.List("Crossovers", BookFields.Crossovers, issue.Crossovers, v => v.Crossovers, (v, x) => v with { Crossovers = x });
		plan.Text("Summary", BookFields.Summary, issue.Summary, v => v.Summary, (v, x) => v with { Summary = x });
		plan.Date("Release Date", BookFields.Released, (old.Released.Year, old.Released.Month, old.Released.Day), old.Released.IsBlank, issue.Released, v => v with { Released = issue.Released });
		plan.Date("Publish Date", BookFields.Published, (old.Published.Year, old.Published.Month, old.Published.Day), old.Published.IsBlank, issue.Published, v => v with { Published = issue.Published });
		plan.Volume(old.Volume, issue.VolumeYear is > 0 ? issue.VolumeYear : null, v => v with { Volume = issue.VolumeYear is > 0 ? issue.VolumeYear : null });
		(string publisher, string imprint) = Publishers.Resolve(issue.Publisher, issue.Imprint, settings);
		plan.Text("Imprint", BookFields.Imprint, imprint, v => v.Imprint, (v, x) => v with { Imprint = x });
		plan.Text("Publisher", BookFields.Publisher, publisher, v => v.Publisher, (v, x) => v with { Publisher = x });
		plan.List("Characters", BookFields.Characters, issue.Characters, v => v.Characters, (v, x) => v with { Characters = x });
		plan.List("Teams", BookFields.Teams, issue.Teams, v => v.Teams, (v, x) => v with { Teams = x });
		plan.List("Locations", BookFields.Locations, issue.Locations, v => v.Locations, (v, x) => v with { Locations = x });
		plan.List("Writers", BookFields.Writers, issue.Writers, v => v.Writers, (v, x) => v with { Writers = x });
		plan.List("Pencillers", BookFields.Pencillers, issue.Pencillers, v => v.Pencillers, (v, x) => v with { Pencillers = x });
		plan.List("Inkers", BookFields.Inkers, issue.Inkers, v => v.Inkers, (v, x) => v with { Inkers = x });
		plan.List("Colorists", BookFields.Colorists, issue.Colorists, v => v.Colorists, (v, x) => v with { Colorists = x });
		plan.List("Letterers", BookFields.Letterers, issue.Letterers, v => v.Letterers, (v, x) => v with { Letterers = x });
		plan.List("CoverArtists", BookFields.CoverArtists, issue.CoverArtists, v => v.CoverArtists, (v, x) => v with { CoverArtists = x });
		plan.List("Editors", BookFields.Editors, issue.Editors, v => v.Editors, (v, x) => v with { Editors = x });
		plan.Text("Webpage", BookFields.Webpage, issue.Webpage, v => v.Webpage, (v, x) => v with { Webpage = x });
		float? rating = issue.Rating is > 0f and <= 5f ? issue.Rating : null;
		plan.Rating(old.Rating, rating, v => v with { Rating = rating });
		plan.List("Tags", BookFields.Tags, KeyMarkers.WithKeyTag(old.Tags, issue.Id), v => v.Tags, (v, x) => v with { Tags = x }, overwrite: true, ignoreBlanks: false);
		plan.Text("Notes", BookFields.Notes, KeyMarkers.WithKeyNote(old.Notes, issue.Id, settings.NoteScrapeDate ? now : null), v => v.Notes,
			(v, x) => v with { Notes = x }, overwrite: true, ignoreBlanks: false);
		plan.Text("Issue Key", BookFields.IssueKey, issue.Id.ToString(), v => v.IssueKey, (v, x) => v with { IssueKey = x }, update: true, overwrite: true, ignoreBlanks: false);
		plan.Text("Series Key", BookFields.SeriesKey, issue.SeriesId.ToString(), v => v.SeriesKey, (v, x) => v with { SeriesKey = x }, update: true, overwrite: true, ignoreBlanks: false);
		return plan.Build();
	}

	/// <summary>
	/// CVDBSKIP goes into the tags, the notes, or both: both when both or neither are enabled. Like Python, which saves
	/// every property it read, the patch writes every field with the book's effective values, so the series, number,
	/// and year parsed from the file name are kept.
	/// </summary>
	public static BookPatch PlanSkipForever(BookFacts book, MergeSettings settings, DateTime now)
	{
		(bool tags, bool notes) = SkipForeverTargets(settings);
		BookValues values = book.Effective;
		if (tags)
		{
			values = values with { Tags = KeyMarkers.WithKeyTag(values.Tags, null) };
		}
		if (notes)
		{
			values = values with { Notes = KeyMarkers.WithKeyNote(values.Notes, null, settings.NoteScrapeDate ? now : null) };
		}
		return new BookPatch(values, DateGate.Restrict(EveryField, values));
	}

	/// <summary>Which of the tags and the notes skip_forever writes CVDBSKIP to.</summary>
	public static (bool Tags, bool Notes) SkipForeverTargets(MergeSettings settings)
	{
		bool tags = settings.Update.HasFlag(BookFields.Tags);
		bool notes = settings.Update.HasFlag(BookFields.Notes);
		return (tags == notes || tags, tags == notes || notes);
	}

	/// <summary>The debug lines Python's skip_forever writes, before it saves the book.</summary>
	public static IEnumerable<string> SkipForeverLog(MergeSettings settings)
	{
		(bool tags, bool notes) = SkipForeverTargets(settings);
		if (tags)
		{
			yield return "Added CVDBSKIP flag to comic book 'Tags'";
		}
		if (notes)
		{
			yield return "Added CVDBSKIP flag to comic book 'Notes'";
		}
	}

	/// <summary>The cover to install as the custom thumbnail: only for a fileless book, and never over a kept thumbnail.</summary>
	public static Uri? PlanThumbnail(BookSnapshot snapshot, Uri? cover, MergeSettings settings) =>
		cover is not null && snapshot.FilePath.Length == 0 && settings.DownloadThumbs && !(snapshot.HasCustomThumbnail && settings.PreserveThumbs)
			? cover
			: null;

	/// <summary>
	/// The massage rule: write when the field's update setting is on, and overwrite is on or the old value is blank, and
	/// not (ignore blanks and the new value is blank). Every field leaves a FieldDecision, written or skipped.
	/// </summary>
	private sealed class Planner(BookValues old, MergeSettings settings)
	{
		// Python cuts a logged text value to 70 characters.
		private const int LogLimit = 70;

		private readonly BookValues old = old;
		private readonly List<FieldDecision> decisions = [];
		private BookValues values = old;

		// Python never drops format_s, so the ShadowFormat it read is always written to Format.
		private BookFields fields = BookFields.Format;

		public void Text(string label, BookFields field, string incoming, Func<BookValues, string> get, Func<BookValues, string, BookValues> set,
			bool? update = null, bool? overwrite = null, bool? ignoreBlanks = null)
		{
			string next = incoming.Trim();
			string before = get(old).Trim();
			Write(label, field, before.Length == 0, next.Length == 0, v => set(v, next), update, overwrite, ignoreBlanks, before != next, () => Cut(next));
		}

		public void List(string label, BookFields field, IReadOnlyList<string> incoming, Func<BookValues, IReadOnlyList<string>> get,
			Func<BookValues, IReadOnlyList<string>, BookValues> set, bool? overwrite = null, bool? ignoreBlanks = null)
		{
			string[] next = incoming.Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
			// Python logs and compares the new items as they came, against the book's items, which are already stripped.
			string[] raw = incoming.Where(item => item.Trim().Length > 0).ToArray();
			string[] before = get(old).Where(item => item.Trim().Length > 0).ToArray();
			Write(label, field, before.Length == 0, next.Length == 0, v => set(v, next), null, overwrite, ignoreBlanks,
				!before.SequenceEqual(raw), () => Cut(string.Join(", ", raw)));
		}

		public void Volume(int? before, int? incoming, Func<BookValues, BookValues> set) =>
			Write("Volume", BookFields.Volume, before is null, incoming is null, set, null, null, null,
				(before ?? -1) != (incoming ?? -1), () => incoming?.ToString(CultureInfo.InvariantCulture) ?? "");

		public void Rating(float? before, float? incoming, Func<BookValues, BookValues> set) =>
			Write("Rating", BookFields.Rating, before is null, incoming is null, set, null, null, null,
				(before ?? 0f) != (incoming ?? 0f), () => incoming is { } rating ? PythonFloat(rating) : "");

		public void Date(string label, BookFields field, (int? Year, int? Month, int? Day) before, bool oldBlank, PartialDate incoming, Func<BookValues, BookValues> set) =>
			Write(label, field, oldBlank, incoming.IsBlank, set, null, null, null,
				before != (incoming.Year, incoming.Month, incoming.Day),
				() => incoming.IsBlank ? "" : string.Join("-", new[] { incoming.Year, incoming.Month, incoming.Day }.Select(part => part?.ToString(CultureInfo.InvariantCulture) ?? "??")));

		public BookPatch Build() => new(values, DateGate.Restrict(fields, values)) { Decisions = decisions };

		// The rating is a float here and a Python float there, so go through its shortest text: 3.67f is "3.67", not 3.6700000762939453.
		private static string PythonFloat(float value) =>
			PyStr.Format(double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));

		private static string Cut(string text)
		{
			text = text.Replace('\n', ' ');
			return text.Length > LogLimit ? text.Substring(0, LogLimit) + " ..." : text;
		}

		private void Write(string label, BookFields field, bool oldBlank, bool newBlank, Func<BookValues, BookValues> set, bool? update, bool? overwrite,
			bool? ignoreBlanks, bool changed, Func<string> text)
		{
			if ((update ?? settings.Update.HasFlag(field))
				&& ((overwrite ?? settings.OverwriteExisting) || oldBlank)
				&& !((ignoreBlanks ?? settings.IgnoreBlanks) && newBlank))
			{
				values = set(values);
				fields |= field;
				decisions.Add(new FieldDecision(label, true, changed, text()));
			}
			else
			{
				decisions.Add(new FieldDecision(label, false, false, FieldDecision.SkippedText));
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
