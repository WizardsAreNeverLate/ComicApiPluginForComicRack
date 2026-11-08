using System.Globalization;

namespace ComicVineScraper.Model;

/// <summary>Comic Vine issue id (the N in 4000-N).</summary>
public readonly record struct IssueId(int Value)
{
	public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// Parses a stored key: "123", and also whole-number floats such as "12.0" (deviations.md #9).
	/// Returns null for blank, fractional, negative, or out-of-range text. This is the only place a stored issue key is parsed.
	/// </summary>
	public static IssueId? TryParseStored(string? text)
	{
		const NumberStyles Styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
		if (!decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out decimal number)
			|| number != decimal.Truncate(number) || number < 0 || number > int.MaxValue)
		{
			return null;
		}
		return new IssueId((int)number);
	}
}
