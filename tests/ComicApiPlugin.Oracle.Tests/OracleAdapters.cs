using System;
using System.IO;

namespace ComicApiPlugin.Oracle.Tests;

/// <summary>Loads the adapter module in Python/oracle_text.py into the oracle, once, so tests can call it through Oracle.Call.</summary>
internal static class OracleAdapters
{
	public const string Module = "oracle_text";

	private static readonly Lazy<bool> loaded = new(() =>
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Python", Module + ".py");
		Oracle.Call("imp", "load_source", Module, path);
		return true;
	});

	public static object? Call(string function, params object?[] args)
	{
		_ = loaded.Value;
		return Oracle.Call(Module, function, args);
	}
}
