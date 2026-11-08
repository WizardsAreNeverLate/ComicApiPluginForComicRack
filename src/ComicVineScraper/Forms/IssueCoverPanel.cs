using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Model;

namespace ComicVineScraper.Plugin;

/// <summary>What a cover panel shows: a series' art, or one issue's covers.</summary>
internal abstract record CoverSubject
{
	private CoverSubject() { }

	public sealed record OfSeries(SeriesRef Series) : CoverSubject;

	public sealed record OfIssue(IssueRef Issue) : CoverSubject;
}

internal enum CoverSearch
{
	NotSearched,
	Searching,
	Searched,
}

/// <summary>issuecoverpanel._ButtonModel: a subject's own cover first, then any alternates, and the one on show.</summary>
internal sealed class CoverChoices(Uri? own, CoverSearch search)
{
	private readonly List<Uri?> covers = [own];

	public CoverSearch Search { get; set; } = search;
	public int Position { get; private set; }
	public int Count => covers.Count;
	public Uri? Current => covers[Position];
	public bool CanNext => Position < covers.Count - 1;
	public bool CanPrevious => Position > 0;

	public void Next() => Position = Math.Min(covers.Count - 1, Position + 1);

	public void Previous() => Position = Math.Max(0, Position - 1);

	public void AddAlternates(IEnumerable<Uri> urls)
	{
		foreach (Uri url in urls.Where(url => !covers.Skip(1).Contains(url)))
		{
			covers.Add(url);
		}
	}
}

/// <summary>
/// issuecoverpanel.IssueCoverPanel: a cover, a label naming it, and buttons that page through alternate covers. A series
/// shown with an issue number hint shows that issue's cover when Comic Vine has it. The user's alternate choice is
/// PreferredCover; the forms record it only when the user confirms with OK.
/// </summary>
internal sealed class IssueCoverPanel : Panel
{
	private readonly PluginContext ctx;
	private readonly IComicVine comicVine;
	private readonly bool showCovers;
	private readonly string? issueHint;
	private readonly CoverPictureBox cover;
	private readonly LinkLabel label;
	private readonly Button next;
	private readonly Button previous;
	private readonly LatestLoader lookup;
	private readonly CancellationTokenSource life = new();
	private readonly Dictionary<CoverSubject, CoverChoices> choices = [];
	private readonly Dictionary<SeriesRef, CoverSubject> resolved = [];
	private CoverSubject? subject;

	/// <param name="issueHint">The book's issue number, or null to show series art for a series.</param>
	public IssueCoverPanel(PluginContext ctx, IComicVine comicVine, bool showCovers, string? issueHint)
	{
		this.ctx = ctx;
		this.comicVine = comicVine;
		this.showCovers = showCovers;
		this.issueHint = string.IsNullOrWhiteSpace(issueHint) ? null : issueHint;
		lookup = new LatestLoader(ctx.Log);
		Name = "coverPanel";
		Size = new Size(195, 360);
		cover = new CoverPictureBox(comicVine, ctx) { Name = "cover", Location = new Point(0, 0), Size = new Size(195, 320), Visible = showCovers };
		label = new LinkLabel
		{
			Name = "coverLabel",
			UseMnemonic = false,
			Visible = showCovers,
			Location = new Point(18, 326),
			Size = new Size(155, 36),
			TextAlign = ContentAlignment.MiddleCenter,
		};
		label.LinkClicked += (_, _) => SearchAlternates();
		next = PageButton("nextCover", ">", new Point(173, 332));
		next.Click += (_, _) => Page(forward: true);
		previous = PageButton("previousCover", "<", new Point(2, 332));
		previous.Click += (_, _) => Page(forward: false);
		Controls.AddRange([cover, previous, label, next]);
		UpdateView();
	}

	/// <summary>The issue and alternate cover on show, or null when the first cover or series art is on show.</summary>
	public (IssueId Issue, Uri Cover)? PreferredCover =>
		subject is CoverSubject.OfIssue { Issue: var issue } && Choices(subject) is { CanPrevious: true, Current: { } url } ? (issue.Id, url) : null;

	public void ShowSeries(SeriesRef? series)
	{
		lookup.Cancel();
		if (series is null)
		{
			Show(null);
		}
		else if (issueHint is null || !showCovers)
		{
			Show(new CoverSubject.OfSeries(series));
		}
		else if (resolved.TryGetValue(series, out CoverSubject? known))
		{
			Show(known);
		}
		else
		{
			_ = lookup.Load(ct => FindHintedIssue(series, ct), found =>
			{
				if (found.Cacheable)
				{
					resolved[series] = found.Subject;
				}
				Show(found.Subject);
			});
		}
	}

	public void ShowIssue(IssueRef? issue)
	{
		lookup.Cancel();
		Show(issue is null ? null : new CoverSubject.OfIssue(issue));
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			lookup.Cancel();
			life.Cancel();
			life.Dispose();
		}
		base.Dispose(disposing);
	}

	private async Task<(CoverSubject Subject, bool Cacheable)> FindHintedIssue(SeriesRef series, CancellationToken ct)
	{
		try
		{
			IssueRef? issue = await comicVine.FindIssueAsync(series.Id, issueHint!, ct);
			return (issue is null ? new CoverSubject.OfSeries(series) : new CoverSubject.OfIssue(issue), true);
		}
		catch (ComicVineException error)
		{
			ctx.Log.Error(error);
			return (new CoverSubject.OfSeries(series), false);
		}
	}

	private void Show(CoverSubject? shown)
	{
		subject = shown;
		UpdateView();
	}

	private CoverChoices Choices(CoverSubject shown)
	{
		if (!choices.TryGetValue(shown, out CoverChoices? found))
		{
			found = shown switch
			{
				CoverSubject.OfSeries s => new CoverChoices(s.Series.ThumbUrl, CoverSearch.Searched),
				CoverSubject.OfIssue i => new CoverChoices(i.Issue.ThumbUrl, CoverSearch.NotSearched),
				_ => throw new ArgumentOutOfRangeException(nameof(shown)),
			};
			choices[shown] = found;
		}
		return found;
	}

	private void Page(bool forward)
	{
		if (subject is null)
		{
			return;
		}
		CoverChoices shown = Choices(subject);
		if (forward)
		{
			shown.Next();
		}
		else
		{
			shown.Previous();
		}
		UpdateView();
	}

	private void SearchAlternates()
	{
		if (subject is CoverSubject.OfIssue { Issue: var issue } searched && Choices(searched) is { Search: CoverSearch.NotSearched } model)
		{
			model.Search = CoverSearch.Searching;
			UpdateView();
			_ = LoadAlternates(issue, model);
		}
	}

	/// <summary>Not cancelled when the user moves on, as in Python: the result is kept for when the issue is shown again.</summary>
	private async Task LoadAlternates(IssueRef issue, CoverChoices model)
	{
		try
		{
			Issue details = await comicVine.GetIssueAsync(issue.Id, includeRating: true, life.Token);
			model.AddAlternates(details.CoverUrls.Skip(1));
		}
		catch (OperationCanceledException) when (life.IsCancellationRequested)
		{
			return;
		}
		catch (Exception error)
		{
			ctx.Log.Error(error);
		}
		model.Search = CoverSearch.Searched;
		if (!IsDisposed)
		{
			UpdateView();
		}
	}

	private void UpdateView()
	{
		label.Links.Clear();
		if (subject is null)
		{
			cover.ShowCover(null);
			next.Visible = previous.Visible = false;
			label.Text = "";
			return;
		}
		CoverChoices model = Choices(subject);
		cover.ShowCover(model.Current);
		next.Visible = showCovers && model.CanNext;
		previous.Visible = showCovers && model.CanPrevious;
		string number = subject is CoverSubject.OfIssue { Issue: var issue } ? issue.IssueNumber : "";
		switch (model.Search)
		{
			case CoverSearch.Searched when number.Length == 0:
				label.Text = ctx.Strings[TextKeys.IssueCoverPanelSeries];
				break;
			case CoverSearch.Searched when model.Count > 1:
				label.Text = ctx.Strings.Format(TextKeys.IssueCoverPanelPlural, number, model.Position + 1, model.Count);
				break;
			case CoverSearch.Searched:
				label.Text = ctx.Strings.Format(TextKeys.IssueCoverPanelSingle, number);
				break;
			case CoverSearch.Searching:
				label.Text = ctx.Strings.Format(TextKeys.IssueCoverPanelSearching, number);
				break;
			case CoverSearch.NotSearched:
				label.Text = ctx.Strings.Format(TextKeys.IssueCoverPanelSearchable, number);
				int start = label.Text.IndexOf('(');
				int end = start < 0 ? -1 : label.Text.IndexOf(')', start);
				if (end > start)
				{
					label.Links.Add(start + 1, end - start - 1);
				}
				break;
		}
	}

	private static Button PageButton(string name, string text, Point location)
	{
		var button = new Button { Name = name, Text = text, Location = location, Size = new Size(20, 24), UseVisualStyleBackColor = true };
		button.Font = new Font(button.Font, FontStyle.Bold);
		return button;
	}
}
