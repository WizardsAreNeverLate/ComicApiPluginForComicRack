using System;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Config;

/// <summary>Python 2 regex syntax, as users write it in ALT_SEARCH_REGEX, on top of .NET Regex.</summary>
public static class PythonRegex
{
	/// <summary>Null where Python's re.compile would raise. Named groups written as (?P&lt;name&gt;...) and (?P=name) are accepted.</summary>
	public static Regex? TryCompile(string pattern)
	{
		string translated = pattern.Replace("(?P<", "(?<");
		translated = Regex.Replace(translated, @"\(\?P=(\w+)\)", @"\k<$1>");
		try
		{
			return new Regex(translated, RegexOptions.CultureInvariant);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}
}
