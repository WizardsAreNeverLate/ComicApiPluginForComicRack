namespace ComicApiPlugin.Books;

/// <summary>
/// What the merge decided for one field. MergeRules.Plan returns these in the order Python's ComicBook.update logs them,
/// and the engine writes them to the debug log, so Plan stays pure.
/// </summary>
/// <param name="Label">The label Python logs, such as "Issue Number".</param>
/// <param name="Written">The field is in the patch.</param>
/// <param name="Changed">The new value differs from the old one. Python marks such a line with '*'.</param>
/// <param name="Text">The logged value, cut to 70 characters for text. "--- skipped ---" when the field is not written.</param>
public sealed record FieldDecision(string Label, bool Written, bool Changed, string Text)
{
	public const string SkippedText = "--- skipped ---";

	/// <summary>The cover URL line of ComicBook.__update_cover_url.</summary>
	public static FieldDecision CoverArt(string? url) =>
		string.IsNullOrEmpty(url) ? new("Cover Art URL", false, false, "--- not available! ---") : new("Cover Art URL", true, false, url!);

	/// <summary>Python: log.debug("--> ", marker, label.ljust(15), ": ", text).</summary>
	public string ToLogLine() => $"--> {(Written && Changed ? '*' : ' ')}{Label,-15}: {Text}";
}
