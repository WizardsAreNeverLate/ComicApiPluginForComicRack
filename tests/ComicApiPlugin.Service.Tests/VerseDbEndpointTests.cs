using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ComicApiPlugin.Fixtures;

namespace ComicApiPlugin.Service.Tests;

public sealed class VerseDbEndpointTests
{
	private static readonly byte[] Jpeg = Encoding.ASCII.GetBytes("JPEG-BYTES-OF-A-COVER");

	[Test]
	public async Task A_cover_lookup_that_VerseDB_is_sure_of_gives_its_one_candidate()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync();

		using HttpResponseMessage response = await world.LookUpCoverAsync(Jpeg);
		string body = await response.Content.ReadAsStringAsync();

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");
		await Assert.That(body).IsEqualTo(
			$$"""{"candidates":[{"coverDate":"1990-06-01","coverUrl":"http:\/\/127.0.0.1:{{world.VerseDb.Port}}\/covers\/5432.webp","issueId":5432,"issueNumber":"12","publisher":"DC Comics","seriesName":"Batman","startYear":1940,"title":"The Joker","volume":1}],"clearMatch":true}""");
	}

	[Test]
	public async Task A_cover_lookup_that_VerseDB_is_not_sure_of_gives_every_candidate_best_first()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync(request => request.Method == "POST" ? FakeAnswer.Json(VerseDbFixtures.UnclearCoverLookup) : null);

		using HttpResponseMessage response = await world.LookUpCoverAsync(Jpeg);
		JsonElement lookup = await JsonAsync(response);

		await Assert.That(lookup.GetProperty("clearMatch").GetBoolean()).IsFalse();
		await Assert.That(lookup.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("issueId").GetInt32())).IsEquivalentTo(
			[11, 12], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lookup.GetProperty("candidates")[0].GetProperty("startYear").ValueKind).IsEqualTo(JsonValueKind.Null);
		await Assert.That(lookup.GetProperty("candidates")[1].GetProperty("coverUrl").ValueKind).IsEqualTo(JsonValueKind.Null);
	}

	[Test]
	public async Task A_cover_lookup_reaches_VerseDB_as_the_multipart_form_VerseDB_accepts_with_the_token_as_a_bearer()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync();

		using HttpResponseMessage response = await world.LookUpCoverAsync(Jpeg);

		FakeRequest sent = world.VerseDb.Requests.Single();
		string body = Encoding.ASCII.GetString(sent.Body);
		await Assert.That(sent.Method).IsEqualTo("POST");
		await Assert.That(sent.Target).IsEqualTo(FakeVerseDbServer.LookupPath);
		await Assert.That(sent.Headers["Authorization"]).IsEqualTo("Bearer " + VerseDbFixtures.Token);
		await Assert.That(sent.Headers["Content-Type"]).StartsWith("multipart/form-data; boundary=");
		// VerseDB answers 404 unless name and filename are quoted and filename* is absent.
		await Assert.That(body).Contains("Content-Disposition: form-data; name=\"image\"; filename=\"cover.jpg\"\r\n");
		await Assert.That(body).DoesNotContain("filename*");
		await Assert.That(body).Contains("Content-Type: image/jpeg\r\n");
		await Assert.That(body).Contains("\r\n\r\nJPEG-BYTES-OF-A-COVER\r\n--");
	}

	[Test]
	public async Task An_issue_request_gives_the_metadata_a_scrape_writes()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync();

		using HttpResponseMessage response = await world.PostJsonAsync(ServiceProtocol.VerseDbIssuePath, """{"issue":5432}""");
		JsonElement issue = await JsonAsync(response);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(issue.GetProperty("seriesName").GetString()).IsEqualTo("Batman");
		await Assert.That(issue.GetProperty("volumeYear").GetInt32()).IsEqualTo(1940);
		await Assert.That(issue.GetProperty("issueNumber").GetString()).IsEqualTo("12");
		await Assert.That(issue.GetProperty("title").GetString()).IsEqualTo("The Joker");
		await Assert.That(issue.GetProperty("publisher").GetString()).IsEqualTo("DC Comics");
		await Assert.That(issue.GetProperty("imprint").GetString()).IsEqualTo("Vertigo");
		await Assert.That(issue.GetProperty("published").GetRawText()).IsEqualTo("""{"day":1,"month":6,"year":1990}""");
		await Assert.That(issue.GetProperty("writers").GetRawText()).IsEqualTo("""["Grant Morrison"]""");
		await Assert.That(issue.GetProperty("characters").GetRawText()).IsEqualTo("""["Batman","Joker"]""");
		await Assert.That(issue.GetProperty("rating").GetDouble()).IsEqualTo(4.5);
		FakeRequest sent = world.VerseDb.Requests.Single();
		await Assert.That(sent.Method + " " + sent.Target).IsEqualTo("GET " + FakeVerseDbServer.IssuePath);
		await Assert.That(sent.Headers["Authorization"]).IsEqualTo("Bearer " + VerseDbFixtures.Token);
	}

	[Test]
	public async Task A_cover_picture_comes_back_as_the_bytes_VerseDB_served_and_the_download_carries_no_token()
	{
		byte[] webp = [.. Enumerable.Range(0, 70_000).Select(i => (byte)(i * 31))];
		using VerseDbWorld world = await VerseDbWorld.StartAsync(cover: webp);

		using HttpResponseMessage response = await world.PostJsonAsync(ServiceProtocol.VerseDbImagePath, $$"""{"url":"{{world.VerseDb.CoverUrl}}"}""");
		byte[] bytes = await response.Content.ReadAsByteArrayAsync();

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/octet-stream");
		await Assert.That(bytes.SequenceEqual(webp)).IsTrue();
		await Assert.That(world.VerseDb.Requests.Single().Headers.ContainsKey("Authorization")).IsFalse();
	}

	[Test]
	[Arguments(401, 401, null)]
	[Arguments(422, 502, null)]
	[Arguments(429, 429, 42.0)]
	[Arguments(503, 502, null)]
	public async Task A_VerseDB_failure_is_an_error_body_with_VerseDBs_status_and_message_and_the_wait_it_asked_for(int verseDbStatus, int status, double? retryAfter)
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync(request => FakeAnswer.Json(
			"{ \"message\": \"Refused for " + VerseDbFixtures.Token + ".\" }", (HttpStatusCode)verseDbStatus) with
		{
			Headers = retryAfter is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["Retry-After"] = "42" },
		});

		using HttpResponseMessage response = await world.LookUpCoverAsync(Jpeg);
		string body = await response.Content.ReadAsStringAsync();
		JsonElement error = JsonDocument.Parse(body).RootElement.GetProperty("verseDb");

		await Assert.That((int)response.StatusCode).IsEqualTo(status);
		await Assert.That(error.GetProperty("status").GetInt32()).IsEqualTo(verseDbStatus);
		await Assert.That(error.GetProperty("message").GetString()).IsEqualTo($"VerseDB refused the request ({verseDbStatus}): Refused for ....");
		await Assert.That(error.GetProperty("retryAfterSeconds").ValueKind == JsonValueKind.Null ? (double?)null : error.GetProperty("retryAfterSeconds").GetDouble()).IsEqualTo(retryAfter);
		await Assert.That(body).DoesNotContain(VerseDbFixtures.Token);
	}

	[Test]
	public async Task A_VerseDB_that_cannot_be_reached_is_a_502_without_a_VerseDB_status()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync(baseUrl: "http://127.0.0.1:1");

		using HttpResponseMessage response = await world.PostJsonAsync(ServiceProtocol.VerseDbIssuePath, """{"issue":5432}""");
		JsonElement error = (await JsonAsync(response)).GetProperty("verseDb");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
		await Assert.That(error.GetProperty("status").ValueKind).IsEqualTo(JsonValueKind.Null);
		await Assert.That(error.GetProperty("message").GetString()).StartsWith("Could not reach VerseDB: ");
	}

	[Test]
	public async Task A_cover_picture_that_VerseDB_does_not_have_is_a_502_with_the_status_of_the_download()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync();

		using HttpResponseMessage response = await world.PostJsonAsync(ServiceProtocol.VerseDbImagePath, $$"""{"url":"{{world.VerseDb.BaseUrl}}/covers/none.webp"}""");
		JsonElement error = (await JsonAsync(response)).GetProperty("verseDb");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
		await Assert.That(error.GetProperty("status").GetInt32()).IsEqualTo(404);
		await Assert.That(error.GetProperty("message").GetString()).IsEqualTo("Could not download the cover picture (HTTP 404).");
	}

	[Test]
	public async Task A_request_without_its_token_its_run_or_its_image_is_a_400_that_says_what_is_missing_and_VerseDB_is_not_asked()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync();

		using HttpResponseMessage noToken = await world.LookUpCoverAsync(Jpeg, token: null);
		using HttpResponseMessage noRun = await world.LookUpCoverAsync(Jpeg, run: null);
		using HttpResponseMessage notMultipart = await world.PostJsonAsync(ServiceProtocol.VerseDbLookupCoverPath, "{}");
		using HttpResponseMessage noImage = await world.SendAsync(ServiceProtocol.VerseDbLookupCoverPath, RunningService.Multipart(("picture", Jpeg)));
		using HttpResponseMessage noUrl = await world.PostJsonAsync(ServiceProtocol.VerseDbImagePath, "{}");
		using HttpResponseMessage relativeUrl = await world.PostJsonAsync(ServiceProtocol.VerseDbImagePath, """{"url":"covers/1.webp"}""");

		await Assert.That(await MessageOf(noToken)).IsEqualTo("The header X-VerseDb-Token is required.");
		await Assert.That(await MessageOf(noRun)).IsEqualTo("The header X-Run-Id is required.");
		await Assert.That(await MessageOf(notMultipart)).IsEqualTo("The body must be multipart/form-data.");
		await Assert.That(await MessageOf(noImage)).IsEqualTo("The multipart/form-data part 'image' is required.");
		await Assert.That(await MessageOf(noUrl)).IsEqualTo("'url' is required.");
		await Assert.That(await MessageOf(relativeUrl)).IsEqualTo("'url' is not an absolute URL.");
		await Assert.That(world.VerseDb.Requests).IsEmpty();
	}

	[Test]
	public async Task Every_call_to_VerseDB_is_logged_with_its_run_and_the_token_is_not_in_the_log()
	{
		using VerseDbWorld world = await VerseDbWorld.StartAsync(request => request.Target == FakeVerseDbServer.IssuePath
			? FakeAnswer.Json("""{ "message": "Unauthenticated." }""", HttpStatusCode.Unauthorized)
			: null);

		using HttpResponseMessage lookup = await world.LookUpCoverAsync(Jpeg);
		using HttpResponseMessage image = await world.PostJsonAsync(ServiceProtocol.VerseDbImagePath, $$"""{"url":"{{world.VerseDb.CoverUrl}}?size=md"}""");
		using HttpResponseMessage issue = await world.PostJsonAsync(ServiceProtocol.VerseDbIssuePath, """{"issue":5432}""");
		string log = await world.Service.StopAndReadLogAsync();

		string host = "127.0.0.1:" + world.VerseDb.Port;
		await Assert.That(log).Contains($"[INF] VerseDB POST {host}{FakeVerseDbServer.LookupPath} responded 200 in ");
		await Assert.That(log).Contains($"[INF] VerseDB GET {host}{FakeVerseDbServer.CoverPath} responded 200 in ");
		await Assert.That(log).Contains($"[WRN] VerseDB GET {host}{FakeVerseDbServer.IssuePath} responded 401 in ");
		await Assert.That(log).Contains("[WRN] VerseDB request failed (status 401): VerseDB refused the request (401): Unauthenticated. (run run-7)");
		await Assert.That(log.Split('\n').Where(line => line.Contains("] VerseDB ")).All(line => line.TrimEnd().EndsWith("(run run-7)", StringComparison.Ordinal))).IsTrue();
		await Assert.That(log).DoesNotContain(VerseDbFixtures.Token);
		await Assert.That(log).DoesNotContain("size=md");
	}

	private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
	{
		using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return json.RootElement.Clone();
	}

	/// <summary>The message of a 400, or what came instead.</summary>
	private static async Task<string> MessageOf(HttpResponseMessage response)
	{
		string body = await response.Content.ReadAsStringAsync();
		return response.StatusCode == HttpStatusCode.BadRequest ? JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()! : $"{(int)response.StatusCode} {body}";
	}
}
