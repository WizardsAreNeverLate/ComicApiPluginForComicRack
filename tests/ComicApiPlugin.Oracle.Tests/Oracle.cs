using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting.Hosting;

namespace ComicApiPlugin.Oracle.Tests;

/// <summary>
/// Calls the original Python modules in process, under the IronPython 2.7.4 that ComicRack ships.
/// Arguments and results cross as plain .NET values: lists and tuples come back as object[],
/// dicts as Dictionary&lt;object, object?&gt;, and .NET arrays and dictionaries go in as Python lists and dicts.
/// </summary>
public static class Oracle
{
	public const string SourceVariable = "CVS_PYTHON_SRC";
	private const string DefaultSource = @"d:\Data\_Code\comic-vine-scraper\src";

	// build.xml's include.paths, without tools/comicrack: that folder holds an old ComicRack.Engine.dll that no oracle call needs.
	private static readonly string[] SourceFolders =
	[
		"py", "py/book", "py/database", "py/database/comicvine", "py/gui", "py/gui/forms", "py/tests", "py/utils",
		"resources", "resources/languages",
	];

	// Python helpers that ship with the tests (py/*.py), such as the Comic Vine replay in cvoracle.py.
	private static readonly string HelperFolder = Path.Combine(AppContext.BaseDirectory, "py");

	private static readonly Lazy<ScriptEngine> engine = new(CreateEngine);
	private static readonly object gate = new();

	public static string SourceRoot => Environment.GetEnvironmentVariable(SourceVariable) is { Length: > 0 } root ? root : DefaultSource;

	/// <summary>Imports module and calls its function. Calls are serialised, because the Python modules keep global state.</summary>
	public static object? Call(string module, string function, params object?[] args)
	{
		ScriptEngine python = engine.Value;
		lock (gate)
		{
			ScriptScope scope = python.ImportModule(module);
			object callable = scope.GetVariable(function);
			return ToNet(python.Operations.Invoke(callable, args.Select(ToPython).ToArray()));
		}
	}

	private static ScriptEngine CreateEngine()
	{
		string root = SourceRoot;
		if (!File.Exists(Path.Combine(root, "py", "utils", "utils.py")))
		{
			throw new InvalidOperationException($"{SourceVariable} is '{root}', which is not the src folder of comic-vine-scraper.");
		}
		ScriptEngine python = Python.CreateEngine();
		// The python folder beside the tests holds helper modules that call the originals the way the plugin does.
		List<string> paths =
		[
			.. python.GetSearchPaths(),
			.. SourceFolders.Select(folder => Path.Combine(root, folder)),
			Path.Combine(AppContext.BaseDirectory, "python"),
			HelperFolder,
		];
		python.SetSearchPaths(paths);
		return python;
	}

	private static object? ToPython(object? value) => value switch
	{
		null or string => value,
		IDictionary map => ToPythonDict(map),
		IEnumerable items => ToPythonList(items),
		_ => value,
	};

	private static List ToPythonList(IEnumerable items)
	{
		var list = new List();
		foreach (object? item in items)
		{
			list.append(ToPython(item));
		}
		return list;
	}

	private static PythonDictionary ToPythonDict(IDictionary map)
	{
		var dict = new PythonDictionary();
		foreach (DictionaryEntry entry in map)
		{
			dict[ToPython(entry.Key)] = ToPython(entry.Value);
		}
		return dict;
	}

	private static object? ToNet(object? value) => value switch
	{
		null or string => value,
		PythonDictionary dict => dict.ToDictionary(pair => ToNet(pair.Key)!, pair => ToNet(pair.Value)),
		List list => list.Select(ToNet).ToArray(),
		PythonTuple tuple => tuple.Select(ToNet).ToArray(),
		_ => value,
	};
}
