using ComicApiPlugin.Model;

namespace ComicApiPlugin.Books;

/// <summary>What a previous scrape left in Tags, Notes, or the comicvine_issue custom value.</summary>
public abstract record StoredMarker
{
	private StoredMarker() { }

	public sealed record None : StoredMarker;

	/// <summary>CVDBSKIP, any case.</summary>
	public sealed record SkipForever : StoredMarker;

	/// <summary>CVDBn, legacy ComicVine[n, or the custom value.</summary>
	public sealed record Issue(IssueId Id) : StoredMarker;
}
