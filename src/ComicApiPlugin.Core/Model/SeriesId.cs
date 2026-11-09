using System.Globalization;

namespace ComicApiPlugin.Model;

/// <summary>Comic Vine volume id (the N in 4050-N).</summary>
public readonly record struct SeriesId(int Value)
{
	public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

	/// <summary>Parses the custom value comicvine_volume with the rules of IssueId.TryParseStored.</summary>
	public static SeriesId? TryParseStored(string? text) => StoredKey.TryParse(text) is int value ? new SeriesId(value) : null;
}
