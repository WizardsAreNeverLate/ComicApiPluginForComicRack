using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Text;

/// <summary>The series, issue number, and year parsed from a file name. Number and Year are "" when not found.</summary>
public sealed record ParsedName(string Series, string Number, string Year);

/// <summary>
/// fnameparser.py. The patterns are the Python ones verbatim: IronPython hands them to .NET's Regex with
/// CultureInvariant, so the same pattern text gives the same matches here.
/// </summary>
public static class FileNameParser
{
	private const RegexOptions Python = RegexOptions.CultureInvariant;

	private static readonly Regex LeadingNumberPrefix = new(@"^\s*(\d+)[\s._-]+([^#]+?#-?\d+.*)", Python);
	private static readonly Regex TitleAfterNumber = new(@"^((?:[a-zA-Z,.-]+\s+)+#?(?:\d+[.0-9]*))\s*(?:-).*?((\(.*)?)$", Python);
	private static readonly Regex[] Brackets = [new(@"\([^\(]*?\)", Python), new(@"\{[^\{]*?\}", Python), new(@"\[[^\[]*?\]", Python)];
	private static readonly Regex Volume = new(@"(?i)(\b((v|vol)\.?|volume))\s*-?\s*[0-9]+[.0-9a-z]*", Python);
	private static readonly Regex PageCount = new(@"(?i)\b[.,]?\s*\d+\s*(p|pg|pgs|pages)\b[.,]?", Python);
	private static readonly Regex CoverCount = new(@"(?i)(\d+\s*of\s*\d+\s*covers)", Python);
	private static readonly Regex OfTotal = new(@"(?i)(?<=\d)(\s*(of|de|di|von|van|z)\s*#*\d+)", Python);
	private static readonly Regex Range = new(@"(?<=\d)(-\d+)", Python);
	private static readonly Regex Dash = new(@"(?<![-_# ])-", Python);
	private static readonly Regex NumberCandidate = new(@"(^|[_\s#])(-?\d*\.?\d\w*)", Python);
	private static readonly Regex Is2000AD = new(@"(?i)\s*2000[\s\.-_]*a[\s.-_]*d.*", Python);
	private static readonly Regex IsBeano = new(@"(?i)\s*the[\s\.-_]+beano[\s.-_]+#?\d{4}", Python);
	private static readonly Regex ReadingListPrefix = new(@"^\s*\d+(\.\s+|\s*-\s*(?=\D))", Python);
	private static readonly Regex LeadingZeros = new(@"^(0+)([0-9].*)$", Python);
	private static readonly Regex PlainNumber = new(@"^-?[.0-9]+$", Python);
	private static readonly Regex RepeatedSpace = new(@"\s{2,}", Python);
	private static readonly Regex VYear = new(@"(?i)(^|[, -_])v(\d{4})($|[, -_])", Python);
	private static readonly Regex[] BracketedText = [new(@"\([^[\](){}]*?\)", Python), new(@"\[[^[\](){}]*?\]", Python), new(@"\{[^[\](){}]*?\}", Python)];
	private static readonly Regex YearRange = new(@"(\d{4})\s*-\s*\d{1,4}", Python);
	private static readonly Regex FourDigits = new(@"^\d{4}$", Python);
	private static readonly char[] BracketChars = "()[]{}".ToCharArray();
	private static readonly char[] SeriesTrimChars = " ,-_".ToCharArray();

	/// <summary>
	/// The user's ALT_SEARCH_REGEX first, then <see cref="Extract"/>, as comicbook.py fills in a book's missing
	/// details. The regex comes from <see cref="PyRegex.TryCompile"/>.
	/// </summary>
	public static ParsedName Parse(string fileName, Regex? altSearchRegex) =>
		(altSearchRegex is null ? null : MatchAltSearchRegex(fileName, altSearchRegex)) ?? Extract(fileName);

	/// <summary>
	/// fnameparser.regex: the named groups series, num, and year, from a match at the start of the name.
	/// Null unless the series group is not blank. A match that times out counts as no match, as Python's
	/// "except" did for any failure.
	/// </summary>
	public static ParsedName? MatchAltSearchRegex(string fileName, Regex regex)
	{
		Match match;
		try
		{
			match = regex.Match(fileName);
		}
		catch (RegexMatchTimeoutException)
		{
			return null;
		}
		if (!match.Success || match.Index != 0 || Found(match, "series") is not { } series)
		{
			return null;
		}
		return new ParsedName(series, Found(match, "num") ?? "", Found(match, "year") ?? "");
	}

	/// <summary>fnameparser.extract. Never fails to give a series: on any parse failure it is the name without its extension.</summary>
	public static ParsedName Extract(string fileName)
	{
		string name = Path.GetFileName(fileName.Trim());
		int lastPeriod = name.LastIndexOf('.');
		name = lastPeriod <= 0 ? name : name.Substring(0, lastPeriod);

		Match match = LeadingNumberPrefix.Match(name);
		if (match.Success)
		{
			name = match.Groups[2].Value;
		}
		match = TitleAfterNumber.Match(name);
		if (match.Success)
		{
			name = match.Groups[1].Value + " " + match.Groups[2].Value;
		}

		ParsedName? parsed = ExtractDetails(name);
		return parsed is null || parsed.Series.Trim().Length == 0 ? new ParsedName(name, "", "") : parsed;
	}

	private static string? Found(Match match, string group)
	{
		Group found = match.Groups[group];
		return found.Success && found.Value.Trim().Length > 0 ? found.Value : null;
	}

	/// <summary>fnameparser.__extract. Null where the Python raised (int() of a year in non-ASCII digits), which extract() catches.</summary>
	private static ParsedName? ExtractDetails(string name)
	{
		string s = name;
		if (ExtractYear(s) is not { } year)
		{
			return null;
		}

		foreach (Regex bracket in Brackets)
		{
			while (bracket.IsMatch(s))
			{
				s = bracket.Replace(s, "");
			}
		}
		s = s.Replace('_', ' ');
		s = Volume.Replace(s, "");
		s = PageCount.Replace(s, "");
		s = CoverCount.Replace(s, "");
		s = OfTotal.Replace(s, "");
		s = Range.Replace(s, "");
		if (s.Contains("-") && !s.Contains(" "))
		{
			s = Dash.Replace(s, " ");
		}

		if (NumberCandidates(s) is not { } matches)
		{
			return null;
		}
		if (matches.Count > 1 && ReadingListPrefix.IsMatch(s))
		{
			s = ReadingListPrefix.Replace(s, "", 1);
			if (NumberCandidates(s) is not { } relisted)
			{
				return null;
			}
			matches = relisted;
		}

		string number = "";
		string series = s;
		if (matches.Count > 0)
		{
			Match last = matches[matches.Count - 1];
			number = NormalizeNumber(last.Groups[2].Value);
			series = s.Substring(0, last.Index) + s.Substring(last.Index + last.Length);
		}
		series = RepeatedSpace.Replace(series, " ").Trim(SeriesTrimChars);
		return new ParsedName(series, number, year);
	}

	/// <summary>Strips leading zeros, then writes plain numbers the way Python's str() does: "21.0000" is "21.0".</summary>
	private static string NormalizeNumber(string number)
	{
		Match zeros = LeadingZeros.Match(number);
		number = zeros.Success ? zeros.Groups[2].Value : number;
		if (!PlainNumber.IsMatch(number) || !PyStr.TryParseFloat(number, out double value))
		{
			return number;
		}
		return number.Contains(".")
			? PyStr.Format(value)
			: BigInteger.Parse(number, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>fnameparser.__extract_numbers. Null where __isYear raised.</summary>
	private static List<Match>? NumberCandidates(string s)
	{
		List<Match> matches = NumberCandidate.Matches(s).Cast<Match>().ToList();
		if (IsAtStart(Is2000AD, s) || IsAtStart(IsBeano, s))
		{
			return matches;
		}
		var kept = new List<Match>();
		foreach (Match match in matches)
		{
			Group number = match.Groups[2];
			if (IsYear(number.Value) is not { } isYear)
			{
				return null;
			}
			if (!isYear || (number.Index > 0 && s[number.Index - 1] == '#'))
			{
				kept.Add(match);
			}
		}
		return kept;
	}

	/// <summary>fnameparser.__extract_year: one "V2003" tag, else the last bracketed year or year range. Null where __isYear raised.</summary>
	private static string? ExtractYear(string s)
	{
		var vYears = new List<string>();
		foreach (Match match in VYear.Matches(s))
		{
			string candidate = match.Groups[2].Value;
			if (IsYear(candidate) is not { } isYear)
			{
				return null;
			}
			if (isYear)
			{
				vYears.Add(candidate);
			}
		}
		if (vYears.Count == 1)
		{
			return vYears[0];
		}

		string found = "";
		foreach (Match match in BracketedText.SelectMany(pattern => pattern.Matches(s).Cast<Match>()))
		{
			string candidate = YearRange.Replace(match.Value.Trim(BracketChars).Trim(), "$1");
			if (IsYear(candidate) is not { } isYear)
			{
				return null;
			}
			if (isYear)
			{
				found = candidate;
			}
		}
		return found;
	}

	/// <summary>fnameparser.__isYear: four digits, strictly between 1900 and 2100. Null where Python's int() raised.</summary>
	private static bool? IsYear(string text)
	{
		if (!FourDigits.IsMatch(text))
		{
			return false;
		}
		if (!PyStr.TryParseInt(text, out long year))
		{
			return null;
		}
		return year is > 1900 and < 2100;
	}

	private static bool IsAtStart(Regex regex, string s)
	{
		Match match = regex.Match(s);
		return match.Success && match.Index == 0;
	}
}
