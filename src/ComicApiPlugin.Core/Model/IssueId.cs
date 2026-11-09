using System.Globalization;

namespace ComicApiPlugin.Model;

/// <summary>Comic Vine issue id (the N in 4000-N).</summary>
public readonly record struct IssueId(int Value)
{
	public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// Parses a stored key: "123", and also whole-number floats such as "12.0" (deviations.md #9).
	/// Returns null for blank, fractional, zero, negative, or out-of-range text, because Comic Vine ids start at 1. This is the only place a stored issue key is parsed.
	/// </summary>
	public static IssueId? TryParseStored(string? text) => StoredKey.TryParse(text) is int value ? new IssueId(value) : null;
}
