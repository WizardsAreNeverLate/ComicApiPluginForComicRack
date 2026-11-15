using System.Collections.Generic;
using System.Linq;

namespace ComicApiPlugin.Matching;

/// <summary>The rule both providers share for accepting a candidate without asking.</summary>
public static class CoverAutoAccept
{
	/// <summary>
	/// The candidate whose cover matches, when exactly one does. With no match there is nothing to accept, and with
	/// two or more the cover cannot tell them apart, so the user decides. A candidate with no score (an image that could not be read) does not match.
	/// </summary>
	public static T? PickSingleMatch<T>(IReadOnlyList<(T Candidate, CoverSimilarity? Score)> scored)
		where T : class
	{
		var matches = scored.Where(s => s.Score is { IsMatch: true }).Take(2).ToList();
		return matches.Count == 1 ? matches[0].Candidate : null;
	}
}
