using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ComicVineScraper.Model;

namespace ComicVineScraper.ComicVine;

/// <summary>One Comic Vine API request, without the key. Query holds the parameters that follow format=xml, in cvconnection.py's order.</summary>
internal readonly record struct ApiRequest(string Path, string Query)
{
	public const string BaseUrl = "https://comicvine.gamespot.com/api/";
	public const int PageSize = 100;

	private const string IssueFields = "field_list=name,issue_number,id,image";

	public string ToUrl(string apiKey) =>
		BaseUrl + Path + "?api_key=" + apiKey + "&client=cvscraper&format=xml" + (Query.Length > 0 ? "&" + Query : "");

	/// <summary>Page 1 sends no page parameter: Comic Vine returns other results for "&amp;page=1" (the "bprd vampire" bug).</summary>
	public static ApiRequest SeriesSearch(string terms, int page)
	{
		string collapsed = string.Join(" ", Regex.Split(terms, @"\s+"));
		return new("search/", "limit=100&resources=volume&field_list=name,start_year,publisher,id,image,count_of_issues&query="
			+ Uri.EscapeDataString(collapsed) + (page == 1 ? "" : "&page=" + Text(page)));
	}

	public static ApiRequest SeriesDetail(SeriesId series) =>
		new("volume/4050-" + series + "/", "field_list=name,start_year,publisher,image,count_of_issues,id");

	public static ApiRequest IssueList(SeriesId series, int page) =>
		new("issues/", IssueFields + "&filter=volume:" + series
			+ (page == 1 ? "" : "&page=" + Text(page) + "&offset=" + Text((page - 1) * PageSize)));

	/// <param name="number">Already in query form; see IssueNumbers.ForQuery.</param>
	public static ApiRequest IssueByNumber(SeriesId series, string number) =>
		new("issues/", IssueFields + "&filter=volume:" + series + ",issue_number:" + Uri.EscapeDataString(number));

	public static ApiRequest IssueDetail(IssueId issue) => new("issue/4000-" + issue + "/", "");

	private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
