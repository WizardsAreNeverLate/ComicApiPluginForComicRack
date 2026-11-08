using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Text;

/// <summary>
/// utils.natural_compare and natural_key: "2" before "10", "5" before "5a" before "5½" before "6", and "3.0" equal to "003".
/// A key is a list of parts. Python compares the lists element by element, where any number sorts before any
/// text, numbers compare by value, and text compares ordinally; a shorter list that is a prefix sorts first.
/// </summary>
public sealed class NaturalOrder : IComparer<string>
{
	private static readonly Regex UnicodeFraction = new(@"\s*(-?)\s*(\d*)\s*([⅛⅙⅕¼⅓⅜⅖½⅗⅝⅔¾⅘⅚⅞])\s*", RegexOptions.CultureInvariant);
	private static readonly Regex NumberPart = new(@"((?:^\s*-)?(?:(?:\d+\.\d+)|(?:\.\d+)|(?:\d+\.)|(?:\d+)))", RegexOptions.CultureInvariant);

	private static readonly Dictionary<char, double> Fractions = new()
	{
		['⅛'] = 1.0 / 8, ['⅙'] = 1.0 / 6, ['⅕'] = 0.2, ['¼'] = 0.25, ['⅓'] = 1.0 / 3, ['⅜'] = 3.0 / 8, ['⅖'] = 0.4, ['½'] = 0.5,
		['⅗'] = 0.6, ['⅝'] = 5.0 / 8, ['⅔'] = 2.0 / 3, ['¾'] = 0.75, ['⅘'] = 0.8, ['⅚'] = 5.0 / 6, ['⅞'] = 7.0 / 8,
	};

	private NaturalOrder() { }

	public static NaturalOrder Instance { get; } = new();

	public int Compare(string? x, string? y)
	{
		if (x is null || y is null)
		{
			return x is null ? (y is null ? 0 : -1) : 1;
		}
		return CompareKeys(Key(x), Key(y));
	}

	/// <summary>natural_key(x) == natural_key(y): the two issue numbers name the same issue.</summary>
	public static bool KeysEqual(string x, string y) => CompareKeys(Key(x), Key(y)) == 0;

	/// <summary>
	/// NaN parts compare equal to each other and below every other number, as .NET orders doubles. Python said
	/// NaN is below NaN, which no sort can honour (deviations.md #16).
	/// </summary>
	private static int CompareKeys(List<Part> x, List<Part> y)
	{
		for (int i = 0; i < Math.Min(x.Count, y.Count); i++)
		{
			int order = (x[i], y[i]) switch
			{
				({ Text: null } a, { Text: null } b) => a.Number.CompareTo(b.Number),
				({ Text: null }, _) => -1,
				(_, { Text: null }) => 1,
				var (a, b) => string.CompareOrdinal(a.Text, b.Text),
			};
			if (order != 0)
			{
				return Math.Sign(order);
			}
		}
		return x.Count.CompareTo(y.Count);
	}

	private static List<Part> Key(string s)
	{
		s = s.Trim();
		if (FractionValue(s) is { } fraction and not 0.0)
		{
			return [Part.Of(""), Part.Of(fraction), Part.Of("")];
		}

		var parts = new List<Part>();
		int end = 0;
		foreach (Match match in NumberPart.Matches(s))
		{
			parts.Add(Convert(s.Substring(end, match.Index - end)));
			parts.Add(Convert(match.Groups[1].Value));
			end = match.Index + match.Length;
		}
		parts.Add(Convert(s.Substring(end)));
		return parts;
	}

	private static Part Convert(string text) =>
		PyStr.TryParseFloat(text, out double number) ? Part.Of(number) : Part.Of(PyStr.Lower(text).Trim());

	/// <summary>A leading "5½" or "-¾" as a number. Text after the fraction is ignored, as in Python.</summary>
	private static double? FractionValue(string s)
	{
		Match match = UnicodeFraction.Match(s);
		if (!match.Success || match.Index != 0)
		{
			return null;
		}
		double whole = PyStr.TryParseFloat(match.Groups[2].Value, out double parsed) ? parsed : 0;
		double value = whole + Fractions[match.Groups[3].Value[0]];
		return match.Groups[1].Value.Length > 0 ? -value : value;
	}

	private readonly record struct Part(double Number, string? Text)
	{
		public static Part Of(double number) => new(number, null);

		public static Part Of(string text) => new(0, text);
	}
}
