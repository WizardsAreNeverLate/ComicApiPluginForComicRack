using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ComicVineScraper.Diagnostics;

/// <summary>
/// The in-memory debug log (log.py). Thread-safe append; nothing goes to disk unless the user saves it.
/// Every line is also written to Trace so ComicRack's own log listeners see it.
/// </summary>
public sealed class DebugLog
{
	private readonly List<string> lines = [];
	private readonly object gate = new();

	public void Debug(string message)
	{
		lock (gate)
		{
			lines.Add(message);
		}
		Trace.WriteLine(message, "ComicVineScraper");
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
