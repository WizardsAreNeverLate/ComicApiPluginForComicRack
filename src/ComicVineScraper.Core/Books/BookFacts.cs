using ComicVineScraper.Model;

namespace ComicVineScraper.Books;

/// <summary>
/// The engine's per-book input: the snapshot plus everything derived from it. Phase 3d adds the pure factory
/// BookFacts.From, which fills Effective and Marker and computes UniqueSeriesKey.
/// </summary>
public sealed record BookFacts(
	int Index,                  // position in the run; the nameless-book fallback for the series key
	BookSnapshot Snapshot,
	BookValues Effective,       // Series, Number, Published.Year filled from the file name when blank
	StoredMarker Marker,        // checked in Python's order: tags then notes for SKIP, then CVDB, then custom value
	SeriesId? StoredSeries,     // custom value comicvine_volume
	string UniqueSeriesKey);    // groups books of one series within a run
