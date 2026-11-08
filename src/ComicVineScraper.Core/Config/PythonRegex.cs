using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Config;

/// <summary>
/// Python 2 regex syntax, as users write it in ALT_SEARCH_REGEX, on top of .NET Regex. IronPython 2.7.4 hands the pattern to .NET
/// after a small rewrite, so this class repeats that rewrite and leaves everything else to .NET.
/// </summary>
public static class PythonRegex
{
	/// <summary>Escapes .NET gives a meaning or its own error to. Any other letter after a backslash is that letter, as in Python 2.</summary>
	private const string DotNetEscapeLetters = "abBcdDefGknpPrsStuvwWxzZA";

	private static readonly Regex Repeat = new(@"\G\{,(\d*)\}", RegexOptions.CultureInvariant);

	/// <summary>
	/// Null where IronPython's re.compile would raise. Accepted beyond plain .NET: (?P&lt;name&gt;...) and (?P=name); a leading u or L
	/// in an inline flag group, which are dropped; {,n} as {0,n}; and a backslash before any other letter as that letter.
	/// </summary>
	public static Regex? TryCompile(string pattern)
	{
		try
		{
			return new Regex(Translate(pattern), RegexOptions.CultureInvariant);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static string Translate(string pattern)
	{
		var output = new StringBuilder(pattern.Length);
		bool inClass = false;
		int classStart = 0;
		int i = 0;
		while (i < pattern.Length)
		{
			char c = pattern[i];
			if (c == '\\' && i + 1 < pattern.Length)
			{
				char next = pattern[i + 1];
				bool unknown = DotNetEscapeLetters.IndexOf(next) < 0 && (next == '_' || char.IsLetter(next));
				output.Append(unknown ? next.ToString() : "\\" + next);
				i += 2;
				continue;
			}
			if (inClass)
			{
				if (c == ']' && i > classStart)
				{
					inClass = false;
				}
				output.Append(c);
				i++;
				continue;
			}
			if (c == '[')
			{
				inClass = true;
				classStart = i + 1 < pattern.Length && pattern[i + 1] == '^' ? i + 2 : i + 1;
				output.Append(c);
				i++;
				continue;
			}
			if (c == '(' && TryGroup(pattern, i, out string? group, out int length))
			{
				output.Append(group);
				i += length;
				continue;
			}
			if (c == '{' && Repeat.Match(pattern, i) is { Success: true } repeat)
			{
				output.Append("{0,").Append(repeat.Groups[1].Value).Append('}');
				i += repeat.Length;
				continue;
			}
			output.Append(c);
			i++;
		}
		return output.ToString();
	}

	private static bool TryGroup(string pattern, int start, out string? group, out int length)
	{
		group = null;
		length = 0;
		if (string.CompareOrdinal(pattern, start, "(?P<", 0, 4) == 0)
		{
			group = "(?<";
			length = 4;
			return true;
		}
		if (string.CompareOrdinal(pattern, start, "(?P=", 0, 4) == 0)
		{
			int close = pattern.IndexOf(')', start);
			string name = close < 0 ? "" : pattern.Substring(start + 4, close - start - 4);
			if (name.Length == 0 || !Regex.IsMatch(name, @"^\w+$"))
			{
				return false;
			}
			group = @"\k<" + name + ">";
			length = close - start + 1;
			return true;
		}
		if (string.CompareOrdinal(pattern, start, "(?", 0, 2) != 0 || start + 2 >= pattern.Length || pattern[start + 2] is not ('u' or 'L'))
		{
			return false;
		}
		int end = start + 3;
		while (end < pattern.Length && "imsx".IndexOf(pattern[end]) >= 0)
		{
			end++;
		}
		if (end >= pattern.Length || pattern[end] is not (')' or ':'))
		{
			return false;
		}
		string flags = pattern.Substring(start + 3, end - start - 3);
		group = flags.Length == 0 ? "(?:" + (pattern[end] == ')' ? ")" : "") : "(?" + flags + pattern[end];
		length = end - start + 1;
		return true;
	}
}
