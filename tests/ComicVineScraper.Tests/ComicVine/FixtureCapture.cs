using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.ComicVine;

/// <summary>
/// Records live Comic Vine responses into tests/fixtures/comicvine. Explicit: it runs only when selected, for example
/// ComicVineScraper.Tests.exe --treenode-filter "/*/*/FixtureCapture/*". It reads the key from the file named by
/// CVS_API_KEY_FILE, captures only fixtures whose file is missing, and never writes or prints the key.
/// </summary>
public sealed class FixtureCapture
{
	private const int MaxRequestsPerRun = 40;
	private const string InvalidKey = "not-a-valid-key";

	private static readonly (string Name, ApiRequest Request, bool UseInvalidKey)[] Catalog =
	[
		("search-batman-p1", ApiRequest.SeriesSearch("batman", 1), false),
		("search-batman-p2", ApiRequest.SeriesSearch("batman", 2), false),
		("search-bprd-vampire", ApiRequest.SeriesSearch("bprd vampire", 1), false),
		("search-no-results", ApiRequest.SeriesSearch("zzqxv qqzzx", 1), false),
		("series-796", ApiRequest.SeriesDetail(new SeriesId(796)), false),
		("issue-number-796-497", ApiRequest.IssueByNumber(new SeriesId(796), "497"), false),
		("issue-not-found", ApiRequest.IssueDetail(new IssueId(999999999)), false),
		("invalid-key", ApiRequest.SeriesSearch("invalid key", 1), true),
		("issue-number-91273-1", ApiRequest.IssueByNumber(new SeriesId(91273), "1"), false),
		("issues-51658-p1", ApiRequest.IssueList(new SeriesId(51658), 1), false),
		("search-preacher", ApiRequest.SeriesSearch("preacher", 1), false),
		("issue-37566", ApiRequest.IssueDetail(new IssueId(37566)), false),
		("series-91273", ApiRequest.SeriesDetail(new SeriesId(91273)), false),
		("issue-535328", ApiRequest.IssueDetail(new IssueId(535328)), false),
		("issues-91273-p1", ApiRequest.IssueList(new SeriesId(91273), 1), false),
		("issues-91273-p2", ApiRequest.IssueList(new SeriesId(91273), 2), false),
		("series-00796-padded", new ApiRequest("volume/4050-00796/", "field_list=name,start_year,publisher,image,count_of_issues,id"), false),
	];

	[Test]
	[Explicit]
	public async Task Capture_missing_fixtures()
	{
		string apiKey = ReadApiKey();
		string folder = SourceFixtureFolder();
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
		client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ComicVineScraper.NET/fixture-capture");

		int sent = 0;
		foreach ((string name, ApiRequest request, bool useInvalidKey) in Catalog)
		{
			string path = Path.Combine(folder, name + ".txt");
			if (File.Exists(path) || sent == MaxRequestsPerRun)
			{
				continue;
			}
			if (sent++ > 0)
			{
				await Task.Delay(1100);
			}
			string key = useInvalidKey ? InvalidKey : apiKey;
			using HttpResponseMessage response = await client.GetAsync(request.ToUrl(key));
			byte[] bytes = await response.Content.ReadAsByteArrayAsync();
			string body = new StreamReader(new MemoryStream(bytes), Encoding.UTF8).ReadToEnd();
			string url = ApiKeyRedaction.Redact(request.ToUrl(key), key).Replace("api_key=...", "api_key=" + FixtureFiles.RedactedKey);
			FixtureFiles.Write(path, new RecordedResponse(url, response.StatusCode, body.Replace(apiKey, FixtureFiles.RedactedKey)));
			Console.WriteLine($"{name}: HTTP {(int)response.StatusCode}, {body.Length} chars");
		}
		Console.WriteLine($"{sent} requests sent.");
		await Assert.That(sent).IsLessThanOrEqualTo(MaxRequestsPerRun);
	}

	private static string ReadApiKey()
	{
		string? file = Environment.GetEnvironmentVariable("CVS_API_KEY_FILE");
		if (string.IsNullOrEmpty(file) || !File.Exists(file))
		{
			throw new InvalidOperationException("Set CVS_API_KEY_FILE to the file that holds the Comic Vine API key.");
		}
		Match key = Regex.Match(File.ReadAllText(file), @"\b[0-9a-fA-F]{40}\b");
		return key.Success ? key.Value : throw new InvalidOperationException("CVS_API_KEY_FILE holds no 40-character hex key.");
	}

	private static string SourceFixtureFolder()
	{
		DirectoryInfo? folder = new(AppContext.BaseDirectory);
		while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ComicVineScraper.sln")))
		{
			folder = folder.Parent;
		}
		return folder is null
			? throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory)
			: Path.Combine(folder.FullName, "tests", "fixtures", "comicvine");
	}
}
