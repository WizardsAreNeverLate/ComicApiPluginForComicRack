using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Text;

/// <summary>utils.convert_number_words: "2" and "2nd" to "two" and "second", and back. Up to twenty, lowercase text only.</summary>
public static class NumberWords
{
	private static readonly (string Number, string Word)[] Map =
	[
		("0", "zero"), ("1", "one"), ("2", "two"), ("3", "three"), ("4", "four"), ("5", "five"), ("6", "six"), ("7", "seven"),
		("8", "eight"), ("9", "nine"), ("10", "ten"), ("11", "eleven"), ("12", "twelve"), ("13", "thirteen"), ("14", "fourteen"),
		("15", "fifteen"), ("16", "sixteen"), ("17", "seventeen"), ("18", "eighteen"), ("19", "nineteen"), ("20", "twenty"),
		("0th", "zeroth"), ("1rst", "first"), ("2nd", "second"), ("3rd", "third"), ("4th", "fourth"), ("5th", "fifth"),
		("6th", "sixth"), ("7th", "seventh"), ("8th", "eighth"), ("9th", "ninth"), ("10th", "tenth"), ("11th", "eleventh"),
		("12th", "twelveth"), ("13th", "thirteenth"), ("14th", "fourteenth"), ("15th", "fifteenth"), ("16th", "sixteenth"),
		("17th", "seventeenth"), ("18th", "eighteenth"), ("19th", "nineteenth"), ("20th", "twentieth"),
	];

	// No replacement output can match another entry, so the order of the rules does not change the result.
	private static readonly (Regex Pattern, string Replacement)[] ExpandRules =
		[.. Map.Select(entry => (Whole(entry.Number), entry.Word)), (Whole("1st"), "first")];

	private static readonly (Regex Pattern, string Replacement)[] ContractRules =
		[.. Map.Select(entry => (Whole(entry.Word), entry.Number)), (Whole("twelfth"), "12th"), (Whole("eightteenth"), "18th")];

	public static string Expand(string phrase) => Apply(phrase, ExpandRules);

	public static string Contract(string phrase) => Apply(phrase, ContractRules);

	private static string Apply(string phrase, IEnumerable<(Regex Pattern, string Replacement)> rules) =>
		rules.Aggregate(phrase, (text, rule) => rule.Pattern.Replace(text, rule.Replacement));

	private static Regex Whole(string word) => new(@"\b" + word + @"\b", RegexOptions.CultureInvariant);
}
