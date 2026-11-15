using System.Net;
using System.Net.Http;
using System.Text;

namespace ComicApiPlugin.Fixtures;

/// <summary>Responses shaped like the VerseDB OpenAPI spec and the live API. Never a real token.</summary>
public static class VerseDbFixtures
{
	public const string Token = "vdb_test_token_do_not_leak_0123456789";

	public const string ClearCoverLookup = """
		{
		  "matched_by": "image",
		  "clear_match": true,
		  "issue_id": 5432,
		  "variant_id": 789,
		  "candidates": [
		    {
		      "issue_id": 5432,
		      "variant_id": 789,
		      "issue": {
		        "id": 5432,
		        "slug": "batman-12",
		        "issue_number": "12",
		        "name": "The Joker",
		        "cover_date": "1990-06-01",
		        "release_date": "1990-04-17",
		        "cover_url": "https://img.versedb.com/full/12.jpg",
		        "images": { "thumb": "https://img.versedb.com/t/12.jpg", "cover_sm": "https://img.versedb.com/s/12.jpg", "cover_md": "https://img.versedb.com/m/12.jpg", "cover_lg": "https://img.versedb.com/l/12.jpg" },
		        "series": { "id": 77, "name": "Batman", "start_year": 1940, "volume_number": 1, "publisher_name": "DC Comics" }
		      }
		    }
		  ]
		}
		""";

	public const string UnclearCoverLookup = """
		{
		  "matched_by": "image",
		  "clear_match": false,
		  "issue_id": null,
		  "variant_id": null,
		  "candidates": [
		    { "issue_id": 11, "variant_id": null, "issue": { "id": 11, "issue_number": "3", "name": null, "cover_date": null, "cover_url": "https://img.versedb.com/full/11.jpg",
		      "images": { "thumb": "https://img.versedb.com/t/11.jpg" }, "series": { "id": 1, "name": "Saga", "start_year": null, "volume_number": null, "publisher_name": null } } },
		    { "issue_id": 12, "variant_id": 4, "issue": { "id": 12, "issue_number": "4", "name": "Four", "cover_date": "2012-09", "cover_url": null,
		      "images": null, "series": { "id": 1, "name": "Saga", "start_year": 2012, "volume_number": 1, "publisher_name": "Image" } } }
		  ]
		}
		""";

	public const string Issue = """
		{
		  "data": {
		    "id": 5432, "slug": "batman-12", "issue_number": "12", "name": "The Joker",
		    "solicitation": "<p>The Joker returns &amp; he is <b>angry</b>.</p><p>Second paragraph.</p>",
		    "release_date": "1990-04-17", "cover_date": "1990-06-01", "page_count": 32, "average_rating": 4.5,
		    "locations": null,
		    "series": { "id": 77, "name": "Batman", "start_year": 1940, "volume_number": 1, "imprint": null },
		    "publisher": { "id": 2, "name": "DC Comics", "slug": "dc-comics" },
		    "imprint": { "id": 7, "name": "Vertigo" },
		    "creators": [
		      { "id": 1, "name": "Grant Morrison", "role": "Writer" },
		      { "id": 2, "name": "Jim Lee", "role": "Artist" },
		      { "id": 3, "name": "Scott Williams", "role": "inker" },
		      { "id": 4, "name": "Alex Sinclair", "role": "Colorist" },
		      { "id": 5, "name": "Richard Starkings", "role": "Letterer" },
		      { "id": 6, "name": "Bob Schreck", "role": "Editor" },
		      { "id": 7, "name": "Pat Penciller", "role": "Penciller" },
		      { "id": 8, "name": "Cover Person", "role": "Cover Artist" },
		      { "id": 9, "name": "Nobody", "role": "Production" },
		      { "id": 10, "name": null, "role": "Writer" }
		    ],
		    "characters": [ { "id": 1, "name": "Batman" }, { "id": 2, "name": " " }, { "id": 3, "name": "Joker" } ],
		    "teams": [ { "id": 1, "name": "Bat Family" } ],
		    "story_arcs": [ { "id": 1, "name": "Knightfall" } ]
		  }
		}
		""";

	public const string SparseIssue = """{ "data": { "id": 9, "issue_number": 7, "name": null, "solicitation": null, "average_rating": 9.5, "locations": ["Gotham", " ", "Metropolis"], "series": null, "publisher": null, "imprint": null, "creators": null, "characters": null, "teams": null, "story_arcs": null } }""";

	public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
		new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
