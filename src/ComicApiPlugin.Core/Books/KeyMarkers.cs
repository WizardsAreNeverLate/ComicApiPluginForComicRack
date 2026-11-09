using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.Books;

/// <summary>
/// The CVDBn and CVDBSKIP markers a scrape leaves in Tags and Notes. Ports comicbook.py's __extract_issue_ref,
/// __add_key_to_tags, and __add_key_to_notes, with cvdb.py's key tag parsing.
/// </summary>
public static class KeyMarkers
{
	private const string SkipTag = "CVDBSKIP";
	private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

	private static readonly Regex Skip = new(SkipTag, Options);
	private static readonly Regex KeyTag = new(@"CVDB(\d{1,})", Options);
	private static readonly Regex LegacyKeyTag = new(@"ComicVine.?\[(\d{1,})", Options);

	/// <summary>Python's order: CVDBSKIP in tags then notes, then a key tag in tags then notes, then the custom value.</summary>
	public static StoredMarker Find(IReadOnlyList<string> tags, string notes, string issueKey)
	{
		string tagText = string.Join(", ", tags);
		if (Skip.IsMatch(tagText) || Skip.IsMatch(notes))
		{
			return new StoredMarker.SkipForever();
		}
		IssueId? id = IssueId.TryParseStored(KeyDigits(tagText))
			?? IssueId.TryParseStored(KeyDigits(notes))
			?? IssueId.TryParseStored(issueKey);
		return id is IssueId found ? new StoredMarker.Issue(found) : new StoredMarker.None();
	}

	/// <summary>Replaces every copy of the previous key tag with the new one (CVDBSKIP when issue is null), or appends it.</summary>
	public static IReadOnlyList<string> WithKeyTag(IReadOnlyList<string> tags, IssueId? issue)
	{
		string tagText = string.Join(", ", tags).Trim();
		string keyTag = KeyTagOf(issue);
		string updated;
		if (tagText.Length == 0)
		{
			updated = keyTag;
		}
		else if (PreviousKeyTag(tagText) is Regex previous && previous.IsMatch(tagText))
		{
			updated = previous.Replace(tagText, _ => keyTag);
		}
		else
		{
			updated = (tagText.EndsWith(",", StringComparison.Ordinal) ? tagText.Substring(0, tagText.Length - 1) : tagText) + ", " + keyTag;
		}
		return updated.Split([", "], StringSplitOptions.None).Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToArray();
	}

	/// <summary>
	/// Replaces the previous key note with "Scraped metadata from ComicVine [CVDBn] on yyyy.MM.dd HH:mm:ss." (no date
	/// when scrapedAt is null), or replaces a bare previous key tag, or appends the note after a blank line.
	/// </summary>
	public static string WithKeyNote(string notes, IssueId? issue, DateTime? scrapedAt)
	{
		notes = notes.Trim();
		string keyTag = KeyTagOf(issue);
		string date = scrapedAt is DateTime at ? " on " + at.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture) : "";
		string keyNote = $"Scraped metadata from ComicVine [{keyTag}]{date}.";
		if (notes.Length == 0)
		{
			return keyNote;
		}
		string? previous = PreviousKeyDigits(notes);
		if (previous is null)
		{
			return notes + "\n\n" + keyNote;
		}
		string previousTag = "CVDB" + previous;
		var keyNotePattern = new Regex(@"Scraped.*?" + previousTag + @"(]\.|.*?[\d\.]{8,} [\d:]{6,}\.)", Options);
		if (keyNotePattern.IsMatch(notes))
		{
			return keyNotePattern.Replace(notes, _ => keyNote).Trim();
		}
		var bareTag = new Regex(previousTag, Options);
		return bareTag.IsMatch(notes) ? bareTag.Replace(notes, _ => keyTag).Trim() : notes + "\n\n" + keyNote;
	}

	private static string KeyTagOf(IssueId? issue) => issue is IssueId id ? "CVDB" + id : SkipTag;

	private static Regex? PreviousKeyTag(string text) => PreviousKeyDigits(text) is string digits ? new Regex("CVDB" + digits, Options) : null;

	// cvdb.create_key_tag_s(int(digits)): leading zeros go, and a key of 0 counts as none.
	private static string? PreviousKeyDigits(string text) => KeyDigits(text)?.TrimStart('0') is { Length: > 0 } digits ? digits : null;

	private static string? KeyDigits(string text)
	{
		Match match = KeyTag.Match(text);
		if (!match.Success)
		{
			match = LegacyKeyTag.Match(text);
		}
		return match.Success ? AsciiDigits(match.Groups[1].Value) : null;
	}

	// IronPython's int() reads any Unicode decimal digit, so CVDB١٢ is issue 12 there too.
	private static string AsciiDigits(string digits) =>
		string.Concat(digits.Select(digit => (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(digit))));
}
