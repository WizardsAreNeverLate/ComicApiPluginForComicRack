using System;
using System.Globalization;
using System.Text;

namespace ComicVineScraper.Text;

/// <summary>
/// Number text the way the original plugin produced and read it under IronPython 2.7.4, which is not
/// always CPython 2.7. Each method ports the IronPython runtime code it names, and the oracle tests pin it.
/// </summary>
public static class PyStr
{
	private const int StrPrecision = 12;
	private static readonly CultureInfo PythonCasing = CultureInfo.GetCultureInfo("en");
	private static readonly NumberFormatInfo PythonNumbers = CreatePythonNumbers();

	/// <summary>
	/// Python str(float): IronPython's "%.12g" with a ".0" after whole numbers (DoubleOps.__str__, StringFormatter.AdjustForG).
	/// It builds on .NET Framework's own "E", "F", and "f" formatting, so it must run on .NET Framework to match.
	/// </summary>
	public static string Format(double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return value.ToString("g" + StrPrecision, PythonNumbers);
		}
		double magnitude = Math.Abs(value);
		string text;
		if ((value != 0.0 && magnitude < 0.0001) || magnitude >= Math.Pow(10.0, StrPrecision))
		{
			string mantissa = magnitude.ToString("E" + (StrPrecision - 1), CultureInfo.InvariantCulture);
			int precision = mantissa.Substring(0, mantissa.IndexOf('E')).TrimEnd('0').Length - 2;
			text = RemoveExponentPaddingZero(value.ToString("e" + precision, PythonNumbers));
		}
		else
		{
			int digits = StrPrecision + (magnitude < 0.001 ? 3 : magnitude < 0.01 ? 2 : magnitude < 0.1 ? 1 : 0);
			string fixedText = magnitude.ToString("F" + digits, CultureInfo.InvariantCulture).TrimEnd('0');
			int fractionLength = fixedText.Length - fixedText.IndexOf('.') - 1;
			int precision = magnitude < 1.0 ? fractionLength : Math.Max(0, Math.Min(StrPrecision - (1 + (int)Math.Log10(magnitude)), fractionLength));
			text = value.ToString("f" + precision, PythonNumbers);
			if (precision == 0)
			{
				text += ".0";
			}
		}
		return IsNegative(value) && value > -1.0 && text[0] != '-' && IsAllZero(text) ? "-" + text : text;
	}

	/// <summary>Python float(text) (DoubleOps.ParseFloat, LiteralParser.ParseFloat). False where Python raises ValueError.</summary>
	public static bool TryParseFloat(string text, out double value)
	{
		value = 0.0;
		if (text.Length > 0 && text[text.Length - 1] == '\0')
		{
			return false;
		}
		double? special = SpecialFloat(text.ToLower(PythonCasing)) ?? LiteralSpecialFloat(ReplaceArabicIndicDigits(text).ToLowerInvariant().TrimStart());
		if (special is { } known)
		{
			value = known;
			return true;
		}
		bool negative = text.TrimStart().StartsWith("-", StringComparison.Ordinal);
		try
		{
			value = double.Parse(ReplaceArabicIndicDigits(text), NumberStyles.Float, CultureInfo.InvariantCulture);
		}
		catch (FormatException)
		{
			return false;
		}
		catch (OverflowException)
		{
			value = negative ? double.NegativeInfinity : double.PositiveInfinity;
			return true;
		}
		if (value == 0.0 && negative)
		{
			value = -0.0;
		}
		return true;
	}

	/// <summary>
	/// Python int(text) in base 10 (LiteralParser.ParseIntegerSign): optional white space, an optional sign, optional
	/// white space, then digits and optional trailing white space. False where Python raises ValueError, and also for
	/// values outside the long range, which Python would return as a long.
	/// </summary>
	public static bool TryParseInt(string text, out long value)
	{
		value = 0;
		int i = SkipWhiteSpace(text, 0);
		bool negative = false;
		if (i < text.Length && (text[i] == '-' || text[i] == '+'))
		{
			negative = text[i] == '-';
			i = SkipWhiteSpace(text, i + 1);
		}
		int start = i;
		try
		{
			for (; i < text.Length && DigitValue(text[i]) is { } digit; i++)
			{
				value = checked(value * 10 + (negative ? -digit : digit));
			}
		}
		catch (OverflowException)
		{
			value = 0;
			return false;
		}
		return i > start && SkipWhiteSpace(text, i) == text.Length;
	}

	private static double? SpecialFloat(string lowered) => lowered switch
	{
		"inf" or "+inf" or "infinity" or "+infinity" => double.PositiveInfinity,
		"-inf" or "-infinity" => double.NegativeInfinity,
		"nan" or "+nan" or "-nan" => double.NaN,
		_ => null,
	};

	private static double? LiteralSpecialFloat(string lowered) => lowered switch
	{
		"inf" or "+inf" => double.PositiveInfinity,
		"-inf" => double.NegativeInfinity,
		"nan" or "+nan" or "-nan" => double.NaN,
		_ => null,
	};

	private static int? DigitValue(char c) => c switch
	{
		>= '0' and <= '9' => c - '0',
		>= '٠' and <= '٩' => c - '٠',
		_ => null,
	};

	private static string ReplaceArabicIndicDigits(string text)
	{
		var builder = new StringBuilder(text);
		for (int i = 0; i < builder.Length; i++)
		{
			if (builder[i] is >= '٠' and <= '٩')
			{
				builder[i] = (char)(builder[i] - '٠' + '0');
			}
		}
		return builder.ToString();
	}

	private static int SkipWhiteSpace(string text, int i)
	{
		while (i < text.Length && char.IsWhiteSpace(text, i))
		{
			i++;
		}
		return i;
	}

	private static bool IsNegative(double value) => value < 0.0 || (value == 0.0 && double.IsNegativeInfinity(1.0 / value));

	private static bool IsAllZero(string text)
	{
		foreach (char c in text)
		{
			if (c is not ('0' or '.'))
			{
				return false;
			}
		}
		return true;
	}

	private static string RemoveExponentPaddingZero(string text)
	{
		int n = text.Length;
		bool padded = text[n - 3] == '0' && ((text[n - 5] is 'e' or 'E' && text[n - 4] is '+' or '-') || text[n - 4] is 'e' or 'E');
		return padded ? text.Substring(0, n - 3) + text.Substring(n - 2, 2) : text;
	}

	private static NumberFormatInfo CreatePythonNumbers()
	{
		var numbers = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
		numbers.PositiveInfinitySymbol = "inf";
		numbers.NegativeInfinitySymbol = "-inf";
		numbers.NaNSymbol = "nan";
		return numbers;
	}
}
