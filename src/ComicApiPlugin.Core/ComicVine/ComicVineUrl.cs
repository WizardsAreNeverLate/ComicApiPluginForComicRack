using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.ComicVine;

/// <summary>The 4000-N, 4050-N, and 49-N ids in a Comic Vine URL, and the cvinfo file next to a book (cvdb.py:89-120, 242-286).</summary>
public static class ComicVineUrl
{
	// cvdb.py's patterns, with \d narrowed to [0-9]: IronPython's \d also matches other scripts' digits, but Comic Vine
	// finds nothing for such an id, so Python's extra request has no visible effect.
	private static readonly Regex IssuePattern = new(@"^.*?\b(4000)-(?<num>[0-9]{2,})\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	private static readonly Regex SeriesPattern = new(@"^.*?\b(49|4050)-(?<num>[0-9]{2,})\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>The issue id when the text has one, else the series id, else null.</summary>
	public static ComicVineTarget? TryParse(string text)
	{
		IReadOnlyList<ComicVineTarget> targets = Targets(text);
		return targets.Count > 0 ? targets[0] : null;
	}

	/// <summary>
	/// The cvinfo.txt or cvinfo file in the book's folder (bookPath may be the folder itself). When both exist, cvinfo
	/// wins, as in Python's loop. Null when there is no file, it cannot be read, or it names no id.
	/// </summary>
	public static ComicVineTarget? FindCvInfo(string? bookPath)
	{
		if (string.IsNullOrEmpty(bookPath))
		{
			return null;
		}
		try
		{
			string? folder = Directory.Exists(bookPath) ? bookPath : Path.GetDirectoryName(bookPath);
			if (folder is null || !Directory.Exists(folder))
			{
				return null;
			}
			string? file = null;
			foreach (string name in new[] { "cvinfo.txt", "cvinfo" })
			{
				string candidate = Path.Combine(folder, name);
				if (File.Exists(candidate))
				{
					file = candidate;
				}
			}
			return file is null ? null : TryParse(File.ReadAllText(file, Encoding.UTF8).Trim());
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
		{
			return null;
		}
	}

	/// <summary>Every id the text names, in the order Python tries them: the issue first, then the series.</summary>
	internal static IReadOnlyList<ComicVineTarget> Targets(string text)
	{
		string trimmed = text.Trim();
		var targets = new List<ComicVineTarget>(2);
		if (Id(IssuePattern, trimmed) is { } issue)
		{
			targets.Add(new ComicVineTarget.OfIssue(new IssueId(issue)));
		}
		if (Id(SeriesPattern, trimmed) is { } series)
		{
			targets.Add(new ComicVineTarget.OfSeries(new SeriesId(series)));
		}
		return targets;
	}

	private static int? Id(Regex pattern, string text)
	{
		Match match = pattern.Match(text);
		return match.Success && int.TryParse(match.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
			? id
			: null;
	}
}
