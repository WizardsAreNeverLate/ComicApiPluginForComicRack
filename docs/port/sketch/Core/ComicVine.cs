// ComicVineScraper.Core: the Comic Vine client.
// Public surface: IComicVine, ComicVineCatalog, ComicVineHttp, ComicVineException. XML stays internal.

namespace ComicVineScraper.ComicVine;

/// <summary>
/// What the engine and the forms ask Comic Vine. Domain types only; no URLs to build, no XML, no paging.
/// All methods throw OperationCanceledException on ct and ComicVineException on failure.
/// </summary>
public interface IComicVine
{
    /// <summary>Cleans the terms (ignored terms, search cleanup, number-word retry), pages to MaxSearchResults, filters by advanced settings. Result order is Comic Vine's.</summary>
    Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct);

    Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct);

    /// <summary>The filter=volume:N,issue_number:M query with leading-zero stripping and the 0.5 retries (deviations #1, #2).</summary>
    Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct);

    Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct);

    /// <summary>A series by id, or the series of an issue for ComicVineTarget.OfIssue (cvinfo, stored keys).</summary>
    Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct);

    /// <summary>A cover or thumbnail, back cover stripped. Null when it cannot be fetched after one retry. Caller disposes.</summary>
    Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct);
}

/// <summary>
/// Per-run facade, the port of db.py. Owns the in-memory caches (search map cleared above 10 entries,
/// one series' issue refs, unbounded series details) so a run starts cold and nothing leaks across runs.
/// </summary>
public sealed class ComicVineCatalog(ComicVineHttp http, SearchSettings settings) : IComicVine
{
    public Task<IReadOnlyList<SeriesRef>> SearchSeriesAsync(string terms, IProgress<QueryProgress>? progress, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<IssueRef>> GetIssuesAsync(SeriesId series, IProgress<QueryProgress>? progress, CancellationToken ct) => throw new NotImplementedException();
    public Task<IssueRef?> FindIssueAsync(SeriesId series, string issueNumber, CancellationToken ct) => throw new NotImplementedException();
    public Task<Issue> GetIssueAsync(IssueId issue, bool includeRating, CancellationToken ct) => throw new NotImplementedException();
    public Task<SeriesRef?> ResolveSeriesAsync(ComicVineTarget target, CancellationToken ct) => throw new NotImplementedException();
    public Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct) => throw new NotImplementedException();
    // TODO SearchSeriesAsync paging: page 1 has no &page; pages 2+ add &page=N. Stop at MaxSearchResults,
    //   at total, or on an empty page (bug 329, issues 33 and 396 are handled, not thrown).
}

/// <summary>The advanced.dat settings that shape search results.</summary>
public sealed record SearchSettings(
    IReadOnlyList<string> IgnoredSearchTerms,
    IReadOnlyList<string> IgnoredPublishers, // lowercase
    int IgnoreBeforeYear, int IgnoreAfterYear, int NeverIgnoreThreshold,
    int MaxSearchResults);                   // clamped 10..5000

/// <summary>
/// Transport. One instance per plugin lifetime: holds the HttpClient and the throttle.
/// Lives in Core so recorded-response tests drive it through a fake HttpMessageHandler.
/// </summary>
public sealed class ComicVineHttp : IDisposable
{
    /// <param name="handler">HttpClientHandler in the plugin; a fixture replayer in tests.</param>
    /// <param name="apiKey">Never logged: every URL in a log line or exception passes through Redact.</param>
    public ComicVineHttp(HttpMessageHandler handler, string apiKey, string userAgent, IClock clock) => throw new NotImplementedException();

    /// <summary>
    /// GET an API path (e.g. "search/?...") and return the parsed, cleaned document.
    /// Policy, in order:
    ///   1. gate: await Throttle (1100 ms since the previous request of any kind, across threads: deviations #6)
    ///   2. send with a 100 s timeout; read the body on every status, 4xx included (deviations #4)
    ///   3. HtmlDecode the body, strip XML-illegal chars keeping valid surrogate pairs (deviations #8), parse
    ///   4. status_code 1 -> ok; 100 -> InvalidApiKey; 107 -> RateLimited (neither retried: deviations #5)
    ///   5. network error, 5xx, empty body, parse error, missing or other status_code -> wait 2500 ms, retry once, then throw
    /// </summary>
    internal Task<XDocument> GetAsync(string pathAndQuery, CancellationToken ct) => throw new NotImplementedException();

    /// <summary>Images share the throttle; one immediate retry, then null.</summary>
    internal Task<Bitmap?> GetImageAsync(Uri url, CancellationToken ct) => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}

/// <summary>Enforces the minimum gap between requests. A SemaphoreSlim(1,1) held across the wait and the send start.</summary>
internal sealed class Throttle(IClock clock, TimeSpan gap)
{
    public Task<IDisposable> EnterAsync(CancellationToken ct) => throw new NotImplementedException();
}

public enum ComicVineError
{
    Network,        // Python code "0": unreachable, timeout, repeated bad responses
    InvalidApiKey,  // status_code 100, also sent with HTTP 401
    RateLimited,    // status_code 107
    Api,            // any other status_code; ApiStatus holds it
}

/// <summary>The only exception type IComicVine throws. Message and Url are redacted ("api_key=...").</summary>
public sealed class ComicVineException(ComicVineError error, string redactedUrl, int? apiStatus, Exception? inner)
    : Exception($"Comic Vine request failed ({error}): {redactedUrl}", inner)
{
    public ComicVineError Error { get; } = error;
    public string Url { get; } = redactedUrl;
    public int? ApiStatus { get; } = apiStatus;

    /// <summary>True when retrying the book later can help. False for a bad key or a rate limit, which end the run.</summary>
    public bool IsRecoverable => Error is ComicVineError.Network or ComicVineError.Api;
}

/// <summary>Response XML to domain records. Pure; oracle-tested against cvdb.py's mapping on the same fixture bodies.</summary>
internal static class ComicVineXml
{
    public static (IReadOnlyList<SeriesRef> Page, int Total) ParseSeriesSearch(XDocument doc) => throw new NotImplementedException();
    public static (IReadOnlyList<IssueRef> Page, int Total) ParseIssueList(XDocument doc) => throw new NotImplementedException();
    public static SeriesRef ParseSeries(XDocument doc) => throw new NotImplementedException();
    public static Issue ParseIssue(XDocument doc, bool includeRating) => throw new NotImplementedException();
}
