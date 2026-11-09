
namespace ComicApiPlugin.Model;

/// <summary>A date where later parts are known only if earlier parts are. Month needs Year; Day needs Month.</summary>
public readonly record struct PartialDate
{
	private PartialDate(int? year, int? month, int? day)
	{
		Year = year;
		Month = month;
		Day = day;
	}

	public int? Year { get; }
	public int? Month { get; }
	public int? Day { get; }

	public static PartialDate None => default;

	/// <summary>
	/// Validates like Issue's Python setters: day 1..31, month 1..12, year >= 0, anything else becomes unknown.
	/// Then drops any part whose predecessor is unknown, so (null, 5, 3) cannot exist.
	/// </summary>
	public static PartialDate Of(int? year, int? month, int? day)
	{
		int? validYear = year is >= 0 ? year : null;
		int? validMonth = validYear is not null && month is >= 1 and <= 12 ? month : null;
		int? validDay = validMonth is not null && day is >= 1 and <= 31 ? day : null;
		return new PartialDate(validYear, validMonth, validDay);
	}

	public bool IsComplete => Day is not null;
	public bool IsBlank => Year is null;
}
