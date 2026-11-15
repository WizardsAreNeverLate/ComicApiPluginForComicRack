using System;
using System.Collections.Generic;

namespace ComicApiPlugin.VerseDb;

/// <summary>One issue VerseDB thinks a cover photo shows.</summary>
public sealed record CoverCandidate(
	VerseDbIssueId IssueId,
	string SeriesName,
	int? StartYear,
	int? Volume,
	string IssueNumber,
	string Title,
	string Publisher,
	string CoverDate,   // as VerseDB sent it, "" when unknown
	Uri? CoverUrl);     // cover_md, else cover_url, else thumb

/// <summary>VerseDB's answer to a cover photo: the candidates are best first, and ClearMatch says it is sure of the first.</summary>
public sealed record CoverLookup(bool ClearMatch, IReadOnlyList<CoverCandidate> Candidates);
