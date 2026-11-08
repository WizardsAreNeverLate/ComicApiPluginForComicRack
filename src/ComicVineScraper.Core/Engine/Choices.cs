using System.Collections.Generic;
using ComicVineScraper.Books;
using ComicVineScraper.Model;

namespace ComicVineScraper.Engine;

/// <summary>User answers. Each dialog's result is a closed union; the engine switches on it exhaustively.</summary>
public abstract record SearchChoice
{
	private SearchChoice() { }

	public sealed record Search(string Terms) : SearchChoice;

	public sealed record Skip : SearchChoice;

	/// <summary>Ctrl+Skip.</summary>
	public sealed record SkipForever : SearchChoice;

	public sealed record Cancel : SearchChoice;
}

public abstract record SeriesChoice
{
	private SeriesChoice() { }

	/// <summary>OK, or "Show Issues".</summary>
	public sealed record Pick(SeriesRef Series, bool ShowIssues) : SeriesChoice;

	public sealed record SearchAgain : SeriesChoice;

	public sealed record Skip : SeriesChoice;

	public sealed record SkipForever : SeriesChoice;

	public sealed record Cancel : SeriesChoice;
}

public abstract record IssueChoice
{
	private IssueChoice() { }

	public sealed record Pick(IssueRef Issue) : IssueChoice;

	public sealed record Back : IssueChoice;

	public sealed record Skip : IssueChoice;

	public sealed record SkipForever : IssueChoice;

	public sealed record Cancel : IssueChoice;
}

/// <summary>A candidate series with its match score.</summary>
public sealed record RankedSeries(SeriesRef Series, int Score);

/// <summary>Candidates are best first.</summary>
public sealed record SeriesQuestion(BookFacts Book, string SearchTerms, IReadOnlyList<RankedSeries> Candidates);

/// <summary>Issues are in natural order. Hint is the issue the book's number points at, if any.</summary>
public sealed record IssueQuestion(BookFacts Book, SeriesRef Series, IReadOnlyList<IssueRef> Issues, IssueRef? Hint);
