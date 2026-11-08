using System.Text.RegularExpressions;

namespace ComicVineScraper.ComicVine;

/// <summary>Issue number rewrites for the issue_number filter (cvconnection.py:125-129, cvdb.py:413-429).</summary>
internal static class IssueNumbers
{
	private static readonly Regex OneHalf = new(@"^0*\.50*$", RegexOptions.CultureInvariant);
	private static readonly Regex Half = new(@"\.50*[^0-9]*$", RegexOptions.CultureInvariant);
	private static readonly Regex Quarter = new(@"\.250*[^0-9]*$", RegexOptions.CultureInvariant);
	private static readonly Regex ThreeQuarters = new(@"\.750*[^0-9]*$", RegexOptions.CultureInvariant);

	/// <summary>Comic Vine does not match leading zeros (Python issue 403); an all-zero number becomes "0". Null when blank.</summary>
	public static string? ForQuery(string number)
	{
		string trimmed = number.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}
		string stripped = trimmed.TrimStart('0').Trim();
		return stripped.Length > 0 ? stripped : "0";
	}

	/// <summary>
	/// The next form to try when a number finds nothing, or the number itself when there is none.
	/// Only a number equal to 0.5 becomes "0½" (deviations #1); "5.5" becomes "5½", "5.25" "5¼", and "5.75" "5¾" (deviations #2).
	/// </summary>
	public static string Alternate(string number)
	{
		if (OneHalf.IsMatch(number) || number == "½")
		{
			return "0½";
		}
		if (number == "0½")
		{
			return "½";
		}
		number = Half.Replace(number, "½");
		number = Quarter.Replace(number, "¼");
		return ThreeQuarters.Replace(number, "¾");
	}
}
