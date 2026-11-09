using System.Collections.Generic;
using System.Linq;
using ComicApiPlugin.Config;
using ComicApiPlugin.Model;
using ComicApiPlugin.Text;

namespace ComicApiPlugin.Matching;

/// <summary>dbutils.filter_series_refs: the advanced.dat publisher and year filters.</summary>
public static class SeriesFilter
{
	/// <summary>
	/// Drops a series whose publisher is ignored or whose start year is outside the range, unless it has at least
	/// NeverIgnoreThreshold issues. An unknown start year passes. Keeps Comic Vine's order, where Python returned a set.
	/// </summary>
	public static IReadOnlyList<SeriesRef> Apply(IReadOnlyList<SeriesRef> found, SearchSettings settings) =>
		found.Where(series => series.IssueCount >= settings.NeverIgnoreThreshold || (YearPasses(series, settings) && PublisherPasses(series, settings))).ToList();

	private static bool YearPasses(SeriesRef series, SearchSettings settings) =>
		series.StartYear is not { } year || (year >= settings.IgnoreBeforeYear && year <= settings.IgnoreAfterYear);

	private static bool PublisherPasses(SeriesRef series, SearchSettings settings) =>
		!settings.IgnoredPublishers.Contains(PyStr.Lower(series.Publisher).Trim());
}
