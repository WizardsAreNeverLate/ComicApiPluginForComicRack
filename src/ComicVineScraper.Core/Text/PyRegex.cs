using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Text;

/// <summary>
/// A user's Python regular expression (ALT_SEARCH_REGEX in advanced.dat) as a .NET Regex. Ports IronPython 2.7.4's
/// re.PreParseRegex, which rewrites the Python-only syntax, such as (?P&lt;name&gt;...), before .NET compiles the pattern.
/// </summary>
public static class PyRegex
{
	/// <summary>
	/// Null where Python's re.compile raises. Python had no match timeout, so a pathological pattern could hang
	/// ComicRack; the timeout makes such a match fail instead.
	/// </summary>
	public static Regex? TryCompile(string pattern, TimeSpan matchTimeout)
	{
		if (PreParse(pattern) is not var (translated, options))
		{
			return null;
		}
		try
		{
			return new Regex(translated, options | RegexOptions.CultureInvariant, matchTimeout);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static (string Pattern, RegexOptions Options)? PreParse(string pattern)
	{
		RegexOptions options = RegexOptions.None;
		int start = 0;
		bool inClass = false;
		bool namedGroupSeen = false;
		int unnamedGroups = 0;
		while (true)
		{
			int i = pattern.IndexOfAny(['(', '{', '[', ']'], start);
			if (i > 0 && pattern[i - 1] == '\\' && IsEscaped(pattern, i))
			{
				start++;
				continue;
			}
			if (i == -1 || i == pattern.Length - 1)
			{
				break;
			}
			switch (pattern[i])
			{
				case '{':
					if (pattern[++i] == ',')
					{
						pattern = pattern.Insert(i, "0");
					}
					break;
				case '[':
					i++;
					inClass = true;
					break;
				case ']':
					i++;
					inClass = false;
					break;
				case '(' when inClass:
					i++;
					break;
				case '(':
					if (pattern[++i] != '?')
					{
						unnamedGroups++;
						if (namedGroupSeen)
						{
							pattern = pattern.Insert(i, "?<___PyRegexNameMangled" + unnamedGroups.ToString(CultureInfo.InvariantCulture) + ">");
						}
						break;
					}
					if (i == pattern.Length - 1)
					{
						return null;
					}
					switch (pattern[++i])
					{
						case 'P' when i + 1 < pattern.Length && pattern[i + 1] == '=':
							pattern = pattern.Remove(i - 2, 4).Insert(i - 2, "\\k<");
							int close = pattern.IndexOf(')', i);
							if (close == -1)
							{
								return null;
							}
							pattern = pattern.Substring(0, close) + ">" + pattern.Substring(close + 1);
							break;
						case 'P':
							namedGroupSeen = true;
							pattern = pattern.Remove(i, 1);
							break;
						case 'i':
							options |= RegexOptions.IgnoreCase;
							break;
						case 'L':
						case 'u':
							RemoveOption(ref pattern, ref i);
							break;
						case 'm':
							options |= RegexOptions.Multiline;
							break;
						case 's':
							options |= RegexOptions.Singleline;
							break;
						case 'x':
							options |= RegexOptions.IgnorePatternWhitespace;
							break;
						case '(':
							i++;
							break;
						case '!' or '#' or ':' or '<' or '=':
							break;
						default:
							return null;
					}
					break;
			}
			start = i;
		}
		return (RemoveUnknownEscapes(pattern), options);
	}

	private static bool IsEscaped(string pattern, int i)
	{
		int backslashes = 0;
		for (int j = i - 1; j >= 0 && pattern[j] == '\\'; j--)
		{
			backslashes++;
		}
		return (backslashes & 1) != 0;
	}

	private static void RemoveOption(ref string pattern, ref int i)
	{
		if (pattern[i - 1] == '?' && i < pattern.Length - 1 && pattern[i + 1] == ')')
		{
			pattern = pattern.Remove(i - 2, 4);
			i -= 2;
		}
		else
		{
			pattern = pattern.Remove(i--, 1);
		}
	}

	/// <summary>Python ignores a backslash before a letter, digit-like character, or underscore that is not an escape; .NET rejects it.</summary>
	private static string RemoveUnknownEscapes(string pattern)
	{
		int start = 0;
		do
		{
			int i = pattern.IndexOf('\\', start);
			if (i == -1 || i == pattern.Length - 1)
			{
				break;
			}
			start = ++i;
			char c = pattern[start];
			if ("ADPSWZ\\abcdefknprstuvwx".IndexOf(c) >= 0)
			{
				continue;
			}
			switch (char.GetUnicodeCategory(c))
			{
				case UnicodeCategory.UppercaseLetter:
				case UnicodeCategory.LowercaseLetter:
				case UnicodeCategory.TitlecaseLetter:
				case UnicodeCategory.ModifierLetter:
				case UnicodeCategory.OtherLetter:
				case UnicodeCategory.LetterNumber:
				case UnicodeCategory.OtherNumber:
				case UnicodeCategory.ConnectorPunctuation:
					pattern = pattern.Remove(i - 1, 1);
					start--;
					break;
			}
		}
		while (++start < pattern.Length);
		return pattern;
	}
}
