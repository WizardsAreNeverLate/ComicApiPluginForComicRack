using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ComicApiPlugin.Tests.ComicVine;

/// <summary>The Comic Vine key for the explicit live tests, from the file named by CVS_API_KEY_FILE. Never print it.</summary>
internal static class LiveApiKey
{
	public static string Read()
	{
		string? file = Environment.GetEnvironmentVariable("CVS_API_KEY_FILE");
		if (string.IsNullOrEmpty(file) || !File.Exists(file))
		{
			throw new InvalidOperationException("Set CVS_API_KEY_FILE to the file that holds the Comic Vine API key.");
		}
		Match key = Regex.Match(File.ReadAllText(file), @"\b[0-9a-fA-F]{40}\b");
		return key.Success ? key.Value : throw new InvalidOperationException("CVS_API_KEY_FILE holds no 40-character hex key.");
	}
}
