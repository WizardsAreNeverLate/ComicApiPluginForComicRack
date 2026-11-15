using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Fixtures;

namespace ComicApiPlugin.Service.Tests;

public sealed class ComicVineEndpointTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

	[Test]
	public async Task Search_sends_a_progress_line_for_each_page_while_it_pages_and_the_series_last()
	{
		var secondPage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using ComicVineWorld world = await ComicVineWorld.StartAsync(async request =>
		{
			if (request.Target.EndsWith("&page=2", StringComparison.Ordinal))
			{
				await secondPage.Task;
			}
			return null;
		});
		using HttpRequestMessage request = world.Request(ServiceProtocol.ComicVineSearchPath, ComicVineWorld.Body("\"terms\":\"batman\""));

		using HttpResponseMessage response = await world.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
		using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
		// Comic Vine has not answered the second page yet, so this line can only have come while the query runs.
		string? first = await reader.ReadLineAsync().WaitAsync(Patience);
		secondPage.SetResult(true);
		string? second = await reader.ReadLineAsync().WaitAsync(Patience);
		using JsonDocument last = JsonDocument.Parse((await reader.ReadLineAsync().WaitAsync(Patience))!);
		string? end = await reader.ReadLineAsync().WaitAsync(Patience);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/x-ndjson");
		await Assert.That(first).IsEqualTo("""{"progress":{"done":100,"total":22}}""");
		await Assert.That(second).IsEqualTo("""{"progress":{"done":200,"total":22}}""");
		await Assert.That(last.RootElement.GetProperty("series").GetArrayLength()).IsEqualTo(200);
		await Assert.That(last.RootElement.GetProperty("series")[1].GetRawText()).IsEqualTo(
			"""{"id":796,"issueCount":716,"name":"Batman","publisher":"DC Comics","startYear":1940,"thumbUrl":"https:\/\/comicvine.gamespot.com\/a\/uploads\/scale_small\/10\/103530\/3421824-2.png"}""");
		await Assert.That(end).IsNull();
		await Assert.That(world.ComicVine.RequestKeys.Select(key => key[key.IndexOf("&query=", StringComparison.Ordinal)..])).IsEquivalentTo(
			["&query=batman", "&query=batman&page=2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Issues_sends_its_progress_lines_and_then_every_issue_of_the_series()
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync();

		using HttpResponseMessage response = await world.PostAsync(ServiceProtocol.ComicVineIssuesPath, ComicVineWorld.Body("\"series\":91273"));
		List<JsonElement> lines = await ComicVineWorld.LinesAsync(response);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(lines.Take(2).Select(line => line.GetRawText())).IsEquivalentTo(
			["""{"progress":{"done":100,"total":163}}""", """{"progress":{"done":200,"total":163}}"""], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(lines.Count).IsEqualTo(3);
		await Assert.That(lines[2].GetProperty("issues").GetArrayLength()).IsEqualTo(163);
	}

	[Test]
	public async Task FindIssue_answers_the_one_issue_with_that_number_or_null()
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync();

		using HttpResponseMessage found = await world.PostAsync(ServiceProtocol.ComicVineFindIssuePath, ComicVineWorld.Body("\"series\":796,\"issueNumber\":\" 00497 \""));
		using HttpResponseMessage none = await world.PostAsync(ServiceProtocol.ComicVineFindIssuePath, ComicVineWorld.Body("\"series\":9999,\"issueNumber\":\"15\""));
		JsonElement issue = (await ComicVineWorld.JsonAsync(found)).GetProperty("issue");

		await Assert.That(found.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(issue.GetProperty("id").GetInt32()).IsEqualTo(37566);
		await Assert.That(issue.GetProperty("issueNumber").GetString()).IsEqualTo("497");
		await Assert.That(issue.GetProperty("title").GetString()).IsEqualTo("The Broken Bat");
		await Assert.That(issue.GetProperty("thumbUrl").GetString()).IsEqualTo("https://comicvine.gamespot.com/a/uploads/scale_small/11112/111120209/5511529-batman%20%23497b.jpg");
		await Assert.That(none.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await ComicVineWorld.JsonAsync(none)).GetProperty("issue").ValueKind).IsEqualTo(JsonValueKind.Null);
	}

	[Test]
	public async Task Issue_answers_the_cleaned_details_with_the_series_start_year_and_publisher()
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync();

		using HttpResponseMessage response = await world.PostAsync(ServiceProtocol.ComicVineIssuePath, ComicVineWorld.Body("\"issue\":37566,\"includeRating\":false"));
		JsonElement issue = await ComicVineWorld.JsonAsync(response);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(issue.GetProperty("id").GetInt32()).IsEqualTo(37566);
		await Assert.That(issue.GetProperty("seriesId").GetInt32()).IsEqualTo(796);
		await Assert.That(issue.GetProperty("seriesName").GetString()).IsEqualTo("Batman");
		await Assert.That(issue.GetProperty("volumeYear").GetInt32()).IsEqualTo(1940);
		await Assert.That(issue.GetProperty("publisher").GetString()).IsEqualTo("DC Comics");
		await Assert.That(issue.GetProperty("title").GetString()).IsEqualTo("The Broken Bat");
		await Assert.That(issue.GetProperty("published").GetRawText()).IsEqualTo("""{"day":31,"month":7,"year":1993}""");
		await Assert.That(issue.GetProperty("released").GetRawText()).IsEqualTo("""{"day":null,"month":null,"year":null}""");
		await Assert.That(issue.GetProperty("pencillers").EnumerateArray().Select(name => name.GetString()!)).IsEquivalentTo(
			["Jim Aparo", "Kelley Jones"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(issue.GetProperty("rating").ValueKind).IsEqualTo(JsonValueKind.Null);
		await Assert.That(issue.GetProperty("coverUrls").GetArrayLength()).IsEqualTo(1);
	}

	[Test]
	public async Task ResolveSeries_answers_a_series_by_its_id_or_by_one_of_its_issues_and_null_for_an_unknown_id()
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync();

		using HttpResponseMessage bySeries = await world.PostAsync(ServiceProtocol.ComicVineResolveSeriesPath, ComicVineWorld.Body("\"series\":796"));
		using HttpResponseMessage byIssue = await world.PostAsync(ServiceProtocol.ComicVineResolveSeriesPath, ComicVineWorld.Body("\"issue\":37566"));
		using HttpResponseMessage missing = await world.PostAsync(ServiceProtocol.ComicVineResolveSeriesPath, ComicVineWorld.Body("\"issue\":999999999"));

		await Assert.That((await ComicVineWorld.JsonAsync(bySeries)).GetProperty("series").GetProperty("name").GetString()).IsEqualTo("Batman");
		await Assert.That((await ComicVineWorld.JsonAsync(byIssue)).GetProperty("series").GetProperty("id").GetInt32()).IsEqualTo(796);
		await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await ComicVineWorld.JsonAsync(missing)).GetProperty("series").ValueKind).IsEqualTo(JsonValueKind.Null);
	}

	[Test]
	public async Task Image_answers_the_front_cover_as_a_png_with_the_same_pixels_and_204_for_an_image_it_cannot_fetch()
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync(request => Task.FromResult(request.Target switch
		{
			"/img/spread.png" => new FakeAnswer(HttpStatusCode.OK, TwoPages(140, 100), "image/png"),
			"/img/single.png" => new FakeAnswer(HttpStatusCode.OK, TwoPages(60, 100), "image/png"),
			_ => null,
		}));
		string root = $"http://127.0.0.1:{world.ComicVine.Port}/img/";

		using HttpResponseMessage spread = await world.PostAsync(ServiceProtocol.ComicVineImagePath, ComicVineWorld.Body($"\"url\":\"{root}spread.png\""));
		using HttpResponseMessage single = await world.PostAsync(ServiceProtocol.ComicVineImagePath, ComicVineWorld.Body($"\"url\":\"{root}single.png\""));
		using HttpResponseMessage missing = await world.PostAsync(ServiceProtocol.ComicVineImagePath, ComicVineWorld.Body($"\"url\":\"{root}missing.png\""));
		using var front = new Bitmap(new MemoryStream(await spread.Content.ReadAsByteArrayAsync()));
		using var whole = new Bitmap(new MemoryStream(await single.Content.ReadAsByteArrayAsync()));

		await Assert.That(spread.Content.Headers.ContentType!.MediaType).IsEqualTo("image/png");
		await Assert.That(front.Size).IsEqualTo(new Size(70, 100));
		await Assert.That(front.GetPixel(0, 50).ToArgb()).IsEqualTo(Color.Blue.ToArgb());
		await Assert.That(whole.Size).IsEqualTo(new Size(60, 100));
		await Assert.That(whole.GetPixel(0, 50).ToArgb()).IsEqualTo(Color.Red.ToArgb());
		await Assert.That(whole.GetPixel(59, 50).ToArgb()).IsEqualTo(Color.Blue.ToArgb());
		await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	[Arguments("invalid key", HttpStatusCode.Unauthorized, "InvalidApiKey", 100)]
	[Arguments("rate limited", (HttpStatusCode)429, "RateLimited", 107)]
	public async Task A_Comic_Vine_failure_is_a_status_code_and_an_error_body_without_the_key(string terms, HttpStatusCode status, string kind, int apiStatus)
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync();

		using HttpResponseMessage response = await world.PostAsync(ServiceProtocol.ComicVineSearchPath, ComicVineWorld.Body($"\"terms\":\"{terms}\""));
		string body = await response.Content.ReadAsStringAsync();
		using JsonDocument json = JsonDocument.Parse(body);
		JsonElement error = json.RootElement.GetProperty("comicVine");

		await Assert.That(response.StatusCode).IsEqualTo(status);
		await Assert.That(error.GetProperty("kind").GetString()).IsEqualTo(kind);
		await Assert.That(error.GetProperty("apiStatus").GetInt32()).IsEqualTo(apiStatus);
		await Assert.That(error.GetProperty("url").GetString()).StartsWith(world.ComicVine.BaseUrl + "search/?api_key=...&client=cvscraper");
		await Assert.That(body).DoesNotContain(ComicVineWorld.Key);
		await Assert.That(world.ComicVine.Requests.Count).IsEqualTo(1);
	}

	[Test]
	public async Task A_Comic_Vine_that_keeps_failing_is_tried_twice_and_answers_502_as_a_network_error()
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync(request =>
			Task.FromResult<FakeAnswer?>(FakeAnswer.Xml("<html>down</html>", HttpStatusCode.ServiceUnavailable)));

		using HttpResponseMessage response = await world.PostAsync(ServiceProtocol.ComicVineFindIssuePath, ComicVineWorld.Body("\"series\":796,\"issueNumber\":\"497\""));
		JsonElement error = (await ComicVineWorld.JsonAsync(response)).GetProperty("comicVine");
		string log = await world.StopAndReadLogAsync();

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
		await Assert.That(error.GetProperty("kind").GetString()).IsEqualTo("Network");
		await Assert.That(error.GetProperty("apiStatus").ValueKind).IsEqualTo(JsonValueKind.Null);
		await Assert.That(world.ComicVine.Requests.Count).IsEqualTo(2);
		await Assert.That(log).Contains("[WRN] Comic Vine request failed (Network, status null) and is tried again in 2500 ms: ");
		await Assert.That(log).DoesNotContain(ComicVineWorld.Key);
	}

	[Test]
	public async Task A_failure_after_the_first_progress_line_is_the_last_line_of_the_stream()
	{
		RecordedResponse rateLimit = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, "synthetic-rate-limit.txt"));
		using ComicVineWorld world = await ComicVineWorld.StartAsync(request => Task.FromResult(
			request.Target.EndsWith("&page=2", StringComparison.Ordinal) ? FakeAnswer.Xml(rateLimit.Body, rateLimit.Status) : null));

		using HttpResponseMessage response = await world.PostAsync(ServiceProtocol.ComicVineSearchPath, ComicVineWorld.Body("\"terms\":\"batman\""));
		List<JsonElement> lines = await ComicVineWorld.LinesAsync(response);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(lines.Count).IsEqualTo(2);
		await Assert.That(lines[0].GetRawText()).IsEqualTo("""{"progress":{"done":100,"total":22}}""");
		await Assert.That(lines[1].GetProperty("error").GetProperty("kind").GetString()).IsEqualTo("RateLimited");
		await Assert.That(lines[1].GetProperty("error").GetProperty("apiStatus").GetInt32()).IsEqualTo(107);
	}

	[Test]
	public async Task A_run_keeps_its_search_cache_and_another_run_starts_with_none()
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync();
		string body = ComicVineWorld.Body("\"terms\":\"preacher\"");

		using HttpResponseMessage first = await world.PostAsync(ServiceProtocol.ComicVineSearchPath, body, run: "run-a");
		using HttpResponseMessage again = await world.PostAsync(ServiceProtocol.ComicVineSearchPath, body, run: "run-a");
		int afterOneRun = world.ComicVine.Requests.Count;
		using HttpResponseMessage other = await world.PostAsync(ServiceProtocol.ComicVineSearchPath, body, run: "run-b");
		using HttpResponseMessage otherSettings = await world.PostAsync(ServiceProtocol.ComicVineSearchPath,
			ComicVineWorld.Body("\"terms\":\"preacher\"", ComicVineWorld.Settings.Replace("\"ignoreBeforeYear\":0", "\"ignoreBeforeYear\":1990")), run: "run-a");

		await Assert.That(await again.Content.ReadAsStringAsync()).IsEqualTo(await first.Content.ReadAsStringAsync());
		await Assert.That(afterOneRun).IsEqualTo(1);
		await Assert.That(await other.Content.ReadAsStringAsync()).IsEqualTo(await first.Content.ReadAsStringAsync());
		await Assert.That(world.ComicVine.Requests.Count).IsEqualTo(3);
	}

	[Test]
	public async Task Two_runs_with_one_key_share_its_throttle_and_another_key_does_not_wait_for_them()
	{
		const string otherKey = "cv-key-other-4Tn8";
		using ComicVineWorld world = await ComicVineWorld.StartAsync();
		string body = ComicVineWorld.Body("\"terms\":\"preacher\"");

		Task<HttpResponseMessage> first = world.PostAsync(ServiceProtocol.ComicVineSearchPath, body, run: "run-a");
		Task<HttpResponseMessage> second = world.PostAsync(ServiceProtocol.ComicVineSearchPath, body, run: "run-b");
		Task<HttpResponseMessage> third = world.PostAsync(ServiceProtocol.ComicVineSearchPath, body, key: otherKey, run: "run-c");
		foreach (HttpResponseMessage response in await Task.WhenAll(first, second, third))
		{
			response.Dispose();
		}
		List<FakeRequest> sameKey = [.. world.ComicVine.Requests.Where(request => request.Target.Contains("api_key=" + ComicVineWorld.Key + "&"))];
		FakeRequest other = world.ComicVine.Requests.Single(request => request.Target.Contains("api_key=" + otherKey + "&"));

		await Assert.That(sameKey.Count).IsEqualTo(2);
		await Assert.That(sameKey[1].ReceivedAt - sameKey[0].ReceivedAt).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1000));
		await Assert.That(other.ReceivedAt).IsLessThan(sameKey[1].ReceivedAt);
	}

	[Test]
	public async Task Cancelling_the_request_cancels_the_call_to_Comic_Vine()
	{
		var received = new TaskCompletionSource<FakeRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using ComicVineWorld world = await ComicVineWorld.StartAsync(async request =>
		{
			received.TrySetResult(request);
			await release.Task;
			return null;
		});
		using var cancel = new CancellationTokenSource();

		Task<HttpResponseMessage> call = world.PostAsync(ServiceProtocol.ComicVineSearchPath, ComicVineWorld.Body("\"terms\":\"preacher\""), ct: cancel.Token);
		FakeRequest atComicVine = await received.Task.WaitAsync(Patience);
		cancel.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(() => call);
		await atComicVine.ClientGone.WaitAsync(Patience);
		release.SetResult(true);
		using HttpResponseMessage health = await world.Client.GetAsync(ServiceProtocol.HealthPath);

		await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	[Arguments(null, "run-1", """{"settings":{},"terms":"x"}""", "The header X-ComicVine-Key is required.")]
	[Arguments("key", null, """{"settings":{},"terms":"x"}""", "The header X-Run-Id is required.")]
	[Arguments("key", "run-1", """{"terms":"x"}""", "'settings' is required.")]
	[Arguments("key", "run-1", """{"settings":{"ignoredSearchTerms":[],"ignoredPublishers":[]}}""", "'terms' is required.")]
	public async Task A_request_that_lacks_a_header_or_a_member_is_answered_400_with_the_reason(string? key, string? run, string body, string reason)
	{
		using ComicVineWorld world = await ComicVineWorld.StartAsync();

		using HttpResponseMessage response = await world.PostAsync(ServiceProtocol.ComicVineSearchPath, body, key, run);
		using HttpResponseMessage notJson = await world.PostAsync(ServiceProtocol.ComicVineSearchPath, "terms=x");
		using HttpResponseMessage twoTargets = await world.PostAsync(ServiceProtocol.ComicVineResolveSeriesPath, ComicVineWorld.Body("\"series\":1,\"issue\":2"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That((await ComicVineWorld.JsonAsync(response)).GetProperty("message").GetString()).IsEqualTo(reason);
		await Assert.That(notJson.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(twoTargets.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(world.ComicVine.Requests).IsEmpty();
	}

	/// <summary>A PNG whose left half is red and whose right half is blue.</summary>
	private static byte[] TwoPages(int width, int height)
	{
		using var image = new Bitmap(width, height);
		using (Graphics g = Graphics.FromImage(image))
		{
			g.FillRectangle(Brushes.Red, 0, 0, width / 2, height);
			g.FillRectangle(Brushes.Blue, width / 2, 0, width - width / 2, height);
		}
		using var stream = new MemoryStream();
		image.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}
}
