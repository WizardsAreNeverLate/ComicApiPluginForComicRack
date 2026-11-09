// ComicApiPlugin.Oracle.Tests: differential tests against the Python plugin.

namespace ComicApiPlugin.Oracle.Tests;

/// <summary>
/// Calls the original modules in-process under IronPython 2.7.4 from ComicRack's install (ComicRackDir).
/// Inputs are primitives or dictionaries; outputs come back as tuples/lists and are converted to the C#
/// result record here, so the comparison is record equality. Skips exactly the inputs deviations.md lists.
/// </summary>
public sealed class PythonOracle : IDisposable
{
    public static PythonOracle Start(string pythonSourceFolder) => throw new NotImplementedException();
    public ParsedName Extract(string fileName) => throw new NotImplementedException();   // fnameparser.extract
    public int MatchScore(object book, object seriesRef) => throw new NotImplementedException();
    /// <summary>ComicBook.update over a pure-Python BookData stub; returns the (field, value) pairs it would write.</summary>
    public IReadOnlyDictionary<string, object> Update(object bookData, object issue, object config) => throw new NotImplementedException();
    public void Dispose() => throw new NotImplementedException();
}
