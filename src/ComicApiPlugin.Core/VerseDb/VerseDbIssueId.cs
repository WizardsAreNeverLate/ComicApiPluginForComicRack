using System.Globalization;

namespace ComicApiPlugin.VerseDb;

/// <summary>A VerseDB issue id. Not a Comic Vine IssueId: the two id spaces must never meet.</summary>
public readonly record struct VerseDbIssueId(int Value)
{
	public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
