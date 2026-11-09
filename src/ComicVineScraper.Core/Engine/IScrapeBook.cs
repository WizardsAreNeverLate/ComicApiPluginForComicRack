using System.Drawing;
using ComicVineScraper.Books;

namespace ComicVineScraper.Engine;

/// <summary>
/// One library book. Every member is called on the thread that runs the scrape (ComicRack's UI thread
/// in the plugin) and is synchronous: none of them may do network I/O.
/// </summary>
public interface IScrapeBook
{
	BookSnapshot Read();

	/// <summary>A copy of the page with the back cover stripped, or null (fileless, unreadable, out of range). Caller disposes.</summary>
	Bitmap? GetPage(int index);

	/// <summary>Writes every field in patch.Fields, then sets the thumbnail if one is given. Idempotent. False when ComicRack refused the thumbnail.</summary>
	bool Apply(BookPatch patch, Bitmap? thumbnail);
}

public sealed record RunSummary(int Scraped, int Skipped, bool Cancelled);
