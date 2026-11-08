using ComicVineScraper.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ComicVineScraper.Books;

/// <summary>
/// comicbook.py's unique_series_s: one key for every book that appears to belong to the same series. The series name and
/// format, without non-word characters and lowercased, then the first 5 bytes of the MD5 of the folder and volume.
/// </summary>
public static class UniqueSeriesKey
{
	private static readonly Regex NonWord = new(@"\W+");

	// IronPython 2.7.4's str.lower() is ToLower(new CultureInfo("en")), whatever the user's culture.

	/// <param name="index">The book's place in the run. A nameless book's key uses it where Python used id(self).</param>
	public static string Compute(string series, string format, int? volume, string filePath, bool ignoreFolders, int index)
	{
		string name = series.Length > 0 ? series + format : "";
		name = PyStr.Lower(NonWord.Replace(name, ""));
		string volumeText = "";
		if (name.Length == 0)
		{
			name = "uniqueid-" + index.ToString(CultureInfo.InvariantCulture);
		}
		else if (volume is > 0)
		{
			volumeText = volume.Value.ToString(CultureInfo.InvariantCulture);
		}
		string folder = filePath.Length > 0 ? Path.GetDirectoryName(filePath) ?? "" : "";
		string hashed = ignoreFolders ? volumeText : folder + volumeText;
		return hashed.Length == 0 ? name : name + Md5Prefix(hashed);
	}

	private static string Md5Prefix(string text)
	{
		using var md5 = MD5.Create();
		byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
		return string.Concat(hash.Take(5).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
	}
}
