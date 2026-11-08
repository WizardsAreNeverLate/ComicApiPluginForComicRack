using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins.Automation;

namespace ComicVineScraper.Plugin;

/// <summary>
/// Adapts one ComicRack book to IScrapeBook. The only code that reads or writes ComicBook properties.
/// Which fields to write, and with what values, is decided in Core before Apply runs.
/// </summary>
internal sealed class ComicRackBook(ComicBook book, IApplication app) : IScrapeBook
{
	private static readonly BookFields[] SingleFields = Enum.GetValues(typeof(BookFields)).Cast<BookFields>()
		.Where(field => field != BookFields.None && (field & (field - 1)) == 0)
		.ToArray();

	public BookSnapshot Read() =>
		new(new BookValues { Notes = book.Notes ?? "" }, book.FilePath ?? "", book.PageCount, !string.IsNullOrEmpty(book.CustomThumbnailKey));

	public Bitmap? GetPage(int index) => throw new NotImplementedException("GetPage lands with the cover panel in phase 4.");

	/// <summary>Writes in one synchronous pass with no awaits, so a write can only be partial if a ComicRack setter throws.</summary>
	public void Apply(BookPatch patch, Bitmap? thumbnail)
	{
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
			case BookFields.Notes:
				book.Notes = values.Notes;
				break;
			default:
				throw new NotImplementedException($"Writing {field} lands with the book mapping in phase 3d.");
		}
	}
}
