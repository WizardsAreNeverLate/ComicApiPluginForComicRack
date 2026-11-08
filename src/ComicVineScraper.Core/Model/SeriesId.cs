using System.Globalization;

namespace ComicVineScraper.Model;

/// <summary>Comic Vine volume id (the N in 4050-N).</summary>
public readonly record struct SeriesId(int Value)
{
	public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
