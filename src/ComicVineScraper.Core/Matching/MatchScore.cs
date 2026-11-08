using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ComicVineScraper.Books;
using ComicVineScraper.Engine;
using ComicVineScraper.Model;
using ComicVineScraper.Text;

namespace ComicVineScraper.Matching;

/// <summary>matchscore.MatchScore.compute_n: how well a series matches a book. Higher is better; scores can be negative.</summary>
public static class MatchScore
{
	private static readonly Regex Apostrophe = new("'", RegexOptions.CultureInvariant);
	private static readonly Regex NonWord = new(@"\W+", RegexOptions.CultureInvariant);
	private static readonly Regex GiantSize = new("giant[- ]*sized?", RegexOptions.CultureInvariant);
	private static readonly Regex KingSize = new("king[- ]*sized?", RegexOptions.CultureInvariant);
	private static readonly Regex OneShot = new("one[- ]*shot", RegexOptions.CultureInvariant);
	private static readonly Regex NotNumeric = new(@"[^\d.-]+", RegexOptions.CultureInvariant);

	/// <param name="book">The book's effective values: Series, Number, Format, and the published and released years.</param>
	/// <param name="chosenBefore">Whether the user picked this series in an earlier scrape (series.dat).</param>
	public static double Compute(BookValues book, SeriesRef series, Func<SeriesId, bool> chosenBefore, int currentYear)
	{
		string bookName = book.Series.Length > 0 && book.Format.Length > 0 ? book.Series + " " + book.Format : book.Series;
		List<string> seriesWords = Words(series.Name);
		int nameScore = 0;
		foreach (string word in Words(bookName))
		{
			nameScore += seriesWords.Remove(word) ? 5 : -1;
		}
		nameScore -= seriesWords.Count;

		int priorScore = chosenBefore(series.Id) ? 7 : 0;

		string publisher = PyStr.Lower(series.Publisher);
		int publisherScore = publisher.Contains("panini") || publisher.Contains("deagostina")
			|| publisher is "marvel italia" or "marvel uk" or "semic_as" or "abril" ? -6 : 0;

		double bookNumber = PyStr.TryParseFloat(NotNumeric.Replace(book.Number.Length > 0 ? book.Number : "-1000", ""), out double parsed) ? parsed : -999;
		int bookScore = series.IssueCount > 100 || bookNumber - 1 <= series.IssueCount ? 100 : -100;

		bool IsValidYear(int year) => year > 1900 && year <= currentYear + 1;
		int seriesYear = series.StartYear ?? -1;
		int bookYear = book.Published.Year is { } published && IsValidYear(published) ? published : book.Released.Year ?? -1;
		int yearScore = !IsValidYear(bookYear) ? 0 : !IsValidYear(seriesYear) ? -100 : seriesYear > bookYear ? -500 : 0;

		double recencyScore = IsValidYear(seriesYear) ? -(currentYear - seriesYear) / 100.0 : -1.0;

		return bookScore + nameScore + publisherScore + priorScore + yearScore + recencyScore;
	}

	/// <summary>Best first. Equal scores keep Comic Vine's order; Python's DataGridView sort left ties in any order.</summary>
	public static IReadOnlyList<RankedSeries> Rank(BookValues book, IReadOnlyList<SeriesRef> found, Func<SeriesId, bool> chosenBefore, int currentYear) =>
		found.Select(series => new RankedSeries(series, Compute(book, series, chosenBefore, currentYear))).OrderByDescending(ranked => ranked.Score).ToList();

	private static List<string> Words(string name)
	{
		string s = PyStr.Lower(Apostrophe.Replace(name, ""));
		s = NonWord.Replace(s, " ");
		s = GiantSize.Replace(s, "giant size");
		s = KingSize.Replace(s, "king size");
		s = OneShot.Replace(s, "one shot");
		return [.. s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];
	}
}
