using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using ComicVineScraper.Matching;
using ComicVineScraper.Model;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins.Automation;

namespace ComicVineScraper.Plugin;

/// <summary>
/// Adapts one ComicRack book to IScrapeBook. The only code that reads or writes ComicBook properties.
/// Which fields to write, and with what values, is decided in Core before Apply runs.
/// </summary>
internal sealed class ComicRackBook(ComicBook book, IApplication app) : IScrapeBook
{
	private const string IssueKeyName = "comicvine_issue";
	private const string SeriesKeyName = "comicvine_volume";

	private static readonly BookFields[] SingleFields = Enum.GetValues(typeof(BookFields)).Cast<BookFields>()
		.Where(field => field != BookFields.None && (field & (field - 1)) == 0)
		.ToArray();

	private static readonly Regex CommaBeforeSpace = new(@",(\s+)");

	/// <summary>Reads like PluginBookData: text is stripped, lists are split on commas, and blank parts are dropped.</summary>
	public BookSnapshot Read() => new(
		new BookValues
		{
			Series = Text(book.Series),
			Number = Text(book.Number),
			Volume = book.ShadowVolume >= 0 ? book.ShadowVolume : null,
			Format = Text(book.ShadowFormat),
			Title = Text(book.Title),
			Crossovers = List(book.AlternateSeries),
			Summary = Text(book.Summary),
			Publisher = Text(book.Publisher),
			Imprint = Text(book.Imprint),
			Characters = List(book.Characters),
			Teams = List(book.Teams),
			Locations = List(book.Locations),
			Writers = List(book.Writer),
			Pencillers = List(book.Penciller),
			Inkers = List(book.Inker),
			Colorists = List(book.Colorist),
			Letterers = List(book.Letterer),
			CoverArtists = List(book.CoverArtist),
			Editors = List(book.Editor),
			Tags = List(book.Tags),
			Notes = Text(book.Notes),
			Webpage = Text(book.Web),
			Rating = book.CommunityRating is > 0f and <= 5f ? book.CommunityRating : null,
			IssueKey = Text(book.GetCustomValue(IssueKeyName)),
			SeriesKey = Text(book.GetCustomValue(SeriesKeyName)),
			Released = book.ReleasedTime == DateTime.MinValue
				? PartialDate.None
				: PartialDate.Of(book.ReleasedTime.Year, book.ReleasedTime.Month, book.ReleasedTime.Day),
			Published = BookDate.Of(book.Year, book.Month, book.Day),
		},
		book.FilePath ?? "",
		book.PageCount,
		!string.IsNullOrEmpty(book.CustomThumbnailKey));

	/// <summary>pluginbookdata.create_image_of_page. ComicRack's GetComicPage returns a clone, so the caller owns it.</summary>
	public Bitmap? GetPage(int index)
	{
		if (string.IsNullOrEmpty(book.FilePath))
		{
			return null;
		}
		Bitmap? page;
		try
		{
			page = app.GetComicPage(book, index);
		}
		catch (Exception)
		{
			// Python bug 235: after "clear data" the page count is 0, so a failed read is the only sign of no page.
			return null;
		}
		if (page is null)
		{
			return null;
		}
		Bitmap front = CoverImage.StripBackCover(page);
		if (!ReferenceEquals(front, page))
		{
			page.Dispose();
		}
		return front;
	}

	/// <summary>
	/// Writes in one synchronous pass with no awaits, so a write can only be partial if a ComicRack setter throws.
	/// A patch whose date bits break DateGate, or a thumbnail for a book with a file, is a planning bug, and is refused before anything is written.
	/// </summary>
	public void Apply(BookPatch patch, Bitmap? thumbnail)
	{
		if (thumbnail is not null && !string.IsNullOrEmpty(book.FilePath))
		{
			throw new ArgumentException("A thumbnail is only ever set on a book without a file.", nameof(thumbnail));
		}
		BookFields ungated = patch.Fields & ~DateGate.Restrict(patch.Fields, patch.Values);
		if (ungated != BookFields.None)
		{
			throw new ArgumentException($"The patch writes {ungated} without the date parts those fields need.", nameof(patch));
		}
		foreach (BookFields field in SingleFields.Where(field => patch.Fields.HasFlag(field)))
		{
			Write(field, patch.Values);
		}
		if (thumbnail is not null && !app.SetCustomBookThumbnail(book, thumbnail))
		{
			Trace.WriteLine("Comic Vine Scraper: ComicRack did not accept the thumbnail for " + book.FilePath);
		}
	}

	private void Write(BookFields field, BookValues values)
	{
		switch (field)
		{
			case BookFields.Series: book.Series = values.Series; break;
			case BookFields.Number: book.Number = values.Number; break;
			case BookFields.Volume: book.Volume = values.Volume ?? -1; break;
			case BookFields.Title: book.Title = values.Title; break;
			case BookFields.Crossovers: book.AlternateSeries = Join(values.Crossovers); break;
			case BookFields.Summary: book.Summary = values.Summary; break;
			case BookFields.Publisher: book.Publisher = values.Publisher; break;
			case BookFields.Imprint: book.Imprint = values.Imprint; break;
			case BookFields.Characters: book.Characters = Join(values.Characters); break;
			case BookFields.Teams: book.Teams = Join(values.Teams); break;
			case BookFields.Locations: book.Locations = Join(values.Locations); break;
			case BookFields.Writers: book.Writer = Join(values.Writers); break;
			case BookFields.Pencillers: book.Penciller = Join(values.Pencillers); break;
			case BookFields.Inkers: book.Inker = Join(values.Inkers); break;
			case BookFields.Colorists: book.Colorist = Join(values.Colorists); break;
			case BookFields.Letterers: book.Letterer = Join(values.Letterers); break;
			case BookFields.CoverArtists: book.CoverArtist = Join(values.CoverArtists); break;
			case BookFields.Editors: book.Editor = Join(values.Editors); break;
			case BookFields.Webpage: book.Web = values.Webpage; break;
			case BookFields.Rating: book.CommunityRating = values.Rating ?? 0f; break;
			case BookFields.Tags: book.Tags = Join(values.Tags); break;
			case BookFields.Notes: book.Notes = values.Notes; break;
			case BookFields.IssueKey: book.SetCustomValue(IssueKeyName, values.IssueKey); break;
			case BookFields.SeriesKey: book.SetCustomValue(SeriesKeyName, values.SeriesKey); break;
			case BookFields.Released: book.ReleasedTime = new DateTime(values.Released.Year!.Value, values.Released.Month!.Value, values.Released.Day!.Value); break;
			case BookFields.PublishedYear: book.Year = values.Published.Year!.Value; break;
			case BookFields.PublishedMonth: book.Month = values.Published.Month!.Value; break;
			case BookFields.PublishedDay: book.Day = values.Published.Day!.Value; break;
			case BookFields.Format: book.Format = values.Format; break;
			default: throw new ArgumentOutOfRangeException(nameof(field), field, "ComicRackBook has no mapping for this field.");
		}
	}

	private static string Text(string? value) => value?.Trim() ?? "";

	private static IReadOnlyList<string> List(string? value) =>
		string.IsNullOrEmpty(value) ? [] : value!.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();

	// pluginbookdata.cleanup: a comma before whitespace is dropped, any other comma becomes a space.
	private static string Join(IReadOnlyList<string> items) =>
		string.Join(", ", items.Select(item => CommaBeforeSpace.Replace(item, "$1").Replace(',', ' ')));
}
