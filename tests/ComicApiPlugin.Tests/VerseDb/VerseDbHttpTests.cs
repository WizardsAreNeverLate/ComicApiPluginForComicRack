using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.VerseDb;

namespace ComicApiPlugin.Tests.VerseDb;

public sealed class VerseDbHttpTests
{
	private static VerseDbHttp Client(VerseDbHandler handler) => new(handler, VerseDbFixtures.Token, "ComicApiPlugin/test");

	[Test]
	public async Task A_cover_lookup_posts_the_image_field_with_the_bearer_token()
	{
		var handler = new VerseDbHandler(_ => VerseDbFixtures.Json(HttpStatusCode.OK, VerseDbFixtures.UnclearCoverLookup));
		using VerseDbHttp client = Client(handler);

		await client.LookUpCoverAsync(Encoding.ASCII.GetBytes("JPEGBYTES"), CancellationToken.None);

		(HttpRequestMessage request, byte[]? body) = handler.Sent.Single();
		await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
		await Assert.That(request.RequestUri!.AbsoluteUri).IsEqualTo("https://versedb.com/api/v1/lookup/cover");
		await Assert.That(request.Headers.Authorization!.ToString()).IsEqualTo("Bearer " + VerseDbFixtures.Token);
		await Assert.That(request.Content!.Headers.ContentType!.MediaType).IsEqualTo("multipart/form-data");
		string text = Encoding.ASCII.GetString(body!);
		await Assert.That(text).Contains("name=image");
		await Assert.That(text).Contains("JPEGBYTES");
	}

	[Test]
	public async Task An_issue_request_asks_for_that_issue()
	{
		var handler = new VerseDbHandler(_ => VerseDbFixtures.Json(HttpStatusCode.OK, VerseDbFixtures.Issue));
		using VerseDbHttp client = Client(handler);

		await client.GetIssueAsync(new VerseDbIssueId(5432), CancellationToken.None);

		await Assert.That(handler.Sent.Single().Request.RequestUri!.AbsoluteUri).IsEqualTo("https://versedb.com/api/v1/issues/5432");
	}

	[Test]
	public async Task A_picture_download_does_not_carry_the_token()
	{
		var handler = new VerseDbHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
		using VerseDbHttp client = Client(handler);

		byte[] bytes = await client.GetImageAsync(new Uri("https://img.versedb.com/m/1.jpg"), CancellationToken.None);

		await Assert.That(bytes).IsEquivalentTo(new byte[] { 1, 2, 3 });
		await Assert.That(handler.Sent.Single().Request.Headers.Authorization).IsNull();
	}

	[Test]
	public async Task A_429_carries_Retry_After()
	{
		var handler = new VerseDbHandler(_ =>
		{
			HttpResponseMessage response = VerseDbFixtures.Json((HttpStatusCode)429, """{ "message": "Too many requests." }""");
			response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
			return response;
		});
		using VerseDbHttp client = Client(handler);

		VerseDbException? error = await Assert.That(async () => await client.LookUpCoverAsync([1], CancellationToken.None)).Throws<VerseDbException>();

		await Assert.That(error!.IsRateLimited).IsTrue();
		await Assert.That(error.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(42));
		await Assert.That(error.Message).Contains("Too many requests.");
	}

	[Test]
	public async Task A_401_message_surfaces_without_the_token()
	{
		var handler = new VerseDbHandler(_ => VerseDbFixtures.Json(HttpStatusCode.Unauthorized, "{ \"message\": \"Bad token " + VerseDbFixtures.Token + "\" }"));
		using VerseDbHttp client = Client(handler);

		VerseDbException? error = await Assert.That(async () => await client.GetIssueAsync(new VerseDbIssueId(1), CancellationToken.None)).Throws<VerseDbException>();

		await Assert.That(error!.Status).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(error.Message).Contains("Bad token");
		await Assert.That(error.Message).DoesNotContain(VerseDbFixtures.Token);
		await Assert.That(error.ToString()).DoesNotContain(VerseDbFixtures.Token);
	}

	[Test]
	public async Task A_network_failure_is_a_VerseDbException_without_a_status()
	{
		var handler = new VerseDbHandler(_ => throw new HttpRequestException("connection refused"));
		using VerseDbHttp client = Client(handler);

		VerseDbException? error = await Assert.That(async () => await client.GetIssueAsync(new VerseDbIssueId(1), CancellationToken.None)).Throws<VerseDbException>();

		await Assert.That(error!.Status).IsNull();
		await Assert.That(error.Message).Contains("connection refused");
	}

	[Test]
	public async Task A_blank_token_is_refused()
	{
		await Assert.That(() => new VerseDbHttp(new VerseDbHandler(), " ", "x")).Throws<ArgumentException>();
	}
}
