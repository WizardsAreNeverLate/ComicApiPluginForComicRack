using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ComicApiPlugin.Diagnostics;

/// <summary>
/// The in-memory debug log (log.py). Thread-safe append; nothing goes to disk unless the user saves it.
/// Every line is also written to Trace, and to Console so it appears in ComicRack's script console when that is open.
/// </summary>
public sealed class DebugLog
{
	/// <summary>A Comic Vine API key is 40 hex digits. Redacting any such run is a second line of defence, behind never logging the key.</summary>
	private static readonly Regex KeyLike = new("[0-9a-fA-F]{40,}", RegexOptions.CultureInvariant);

	private readonly List<string> lines = [];
	private readonly object gate = new();

	public void Debug(string message)
	{
		message = KeyLike.Replace(message, "<redacted>");
		lock (gate)
		{
			lines.Add(message);
		}
		Trace.WriteLine(message, "ComicApiPlugin");
		Console.WriteLine("ComicApiPlugin: " + message);
	}

	/// <summary>Records the exception type, message, and stack trace, as log.handle_error and debug_exc did.</summary>
	public void Error(Exception error)
	{
		Debug("-------------------- .NET ERROR -------------------------");
		Debug("Caught " + error.GetType().Name + ": " + error.Message);
		Debug(error.ToString().Replace("\r", ""));
		Debug("");
	}

	public IReadOnlyList<string> Snapshot()
	{
		lock (gate)
		{
			return lines.ToArray();
		}
	}

	/// <summary>Writes every line, each ended with a newline.</summary>
	public void SaveTo(TextWriter writer)
	{
		foreach (string line in Snapshot())
		{
			writer.Write(line);
			writer.Write('\n');
		}
	}
}
