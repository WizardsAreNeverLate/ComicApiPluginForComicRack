// ComicVineScraper.Core: diagnostics.

namespace ComicVineScraper.Diagnostics;

/// <summary>
/// The in-memory debug log (log.py). Thread-safe append; nothing goes to disk unless the user saves it.
/// Every line is also written to Trace so ComicRack's own log listeners see it.
/// </summary>
public sealed class DebugLog
{
    public void Debug(string message) => throw new NotImplementedException();
    public void Error(Exception error) => throw new NotImplementedException();
    public IReadOnlyList<string> Snapshot() => throw new NotImplementedException();
    public void SaveTo(TextWriter writer) => throw new NotImplementedException();
}
