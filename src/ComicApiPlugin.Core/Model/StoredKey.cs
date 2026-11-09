using System.Globalization;

namespace ComicApiPlugin.Model;

/// <summary>Parses the text of a stored Comic Vine id, as IssueId.TryParseStored and SeriesId.TryParseStored need.</summary>
internal static class StoredKey
{
	/// <summary>
	/// "123", and also whole-number floats such as "12.0" (deviations.md #9). Null for blank, fractional, zero, negative,
	/// or out-of-range text, because Comic Vine ids start at 1.
	/// </summary>
	public static int? TryParse(string? text)
	{
		const NumberStyles Styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
		if (!decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out decimal number)
			|| number != decimal.Truncate(number) || number < 1 || number > int.MaxValue)
		{
			return null;
		}
		return (int)number;
	}
}
