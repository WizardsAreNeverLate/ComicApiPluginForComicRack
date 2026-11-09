// ComicApiPlugin.Core: the pure algorithms. Every public method here is an oracle seam: primitive or
// record inputs, deterministic output, no I/O, no clock. Tests call the Python function of the same
// name under IronPython 2.7.4 with the same inputs and compare.

namespace ComicApiPlugin.Text;

/// <summary>Python 2 str() of numbers: 21.0 stays "21.0", 12 significant digits. Used by the parser and DatFile.</summary>
public static class PyStr
{
    public static string Format(double value) => throw new NotImplementedException();
    /// <summary>Python float(): accepts "nan", "inf", "1e3"; InvariantCulture.</summary>
    public static bool TryParseFloat(string text, out double value) => throw new NotImplementedException();
}

public sealed record ParsedName(string Series, string Number, string Year);

/// <summary>fnameparser.extract and regex(). Instance because ALT_SEARCH_REGEX failures are remembered per run.</summary>
public sealed class FileNameParser(Regex? altSearchRegex)
{
    public ParsedName Extract(string fileName) => throw new NotImplementedException();
}

/// <summary>utils.natural_key / natural_compare. Python 2 orders numbers before strings; this comparer encodes that.</summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static NaturalOrder Instance { get; } = new();
    public int Compare(string? x, string? y) => throw new NotImplementedException();
    public static bool KeysEqual(string x, string y) => throw new NotImplementedException();
}

/// <summary>Search term cleanup and number words (cvdb.py:290-319, utils.py:194).</summary>
public static class SearchTerms
{
    public static string Clean(string terms, IReadOnlyList<string> ignoredTerms) => throw new NotImplementedException();
    public static string WithNumberWords(string cleaned) => throw new NotImplementedException();
}

/// <summary>Issue summary cleanup, the eight ordered steps of cvdb.py:628-652.</summary>
public static class SummaryText
{
    public static string Clean(string html) => throw new NotImplementedException();
}

/// <summary>CVDBn / CVDBSKIP markers in Tags and Notes, and the scrape note.</summary>
public static class KeyMarkers
{
    public static StoredMarker Find(IReadOnlyList<string> tags, string notes, string issueKeyCustomValue) => throw new NotImplementedException();
    public static IReadOnlyList<string> WithIssueTag(IReadOnlyList<string> tags, IssueId? issue) => throw new NotImplementedException(); // null = CVDBSKIP

    /// <summary>Replaces an existing scrape note, matching the invariant HH:mm:ss form and the old locale %X forms (deviations #10).</summary>
    public static string WithIssueNote(string notes, IssueId? issue, DateTime now, bool includeDate) => throw new NotImplementedException();
}

/// <summary>4050-N / 4000-N / 49-N in a URL; cvinfo.txt and cvinfo lookup next to the book.</summary>
public static class ComicVineUrl
{
    public static ComicVineTarget? TryParse(string text) => throw new NotImplementedException();
    /// <summary>The only file read the engine does besides settings. Path in, target out.</summary>
    public static ComicVineTarget? FindCvInfo(string bookPath) => throw new NotImplementedException();
}

/// <summary>unique_series_s: \W-stripped lowercase series+format, then the first 5 MD5 bytes (hex) of folder+volume.</summary>
public static class SeriesKey
{
    public static string? Compute(string series, string format, string? folder, int? volume, bool ignoreFolders) => throw new NotImplementedException();
}

/// <summary>The book queue order: skip/known first, then UniqueSeriesKey (culture compare, as Python), then padded number.</summary>
public static class BookOrder
{
    public static IReadOnlyList<BookFacts> Sort(IReadOnlyList<BookFacts> books) => throw new NotImplementedException();
}
