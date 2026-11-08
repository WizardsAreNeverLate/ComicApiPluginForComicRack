namespace ComicVineScraper.Books;

/// <summary>The values the scraper reads or writes, in engine types. Phase 3d adds the other fields of BookFields.</summary>
public sealed record BookValues
{
	public string Notes { get; init; } = "";
}

/// <summary>What the adapter reads from one ComicRack book, on the UI thread, in one synchronous call.</summary>
public sealed record BookSnapshot(BookValues Values, string FilePath, int PageCount, bool HasCustomThumbnail);

/// <summary>One write: every flag in Fields takes its value from Values.</summary>
public sealed record BookPatch(BookValues Values, BookFields Fields);
