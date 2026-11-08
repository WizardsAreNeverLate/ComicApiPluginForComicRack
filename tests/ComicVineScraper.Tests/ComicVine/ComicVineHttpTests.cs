using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ComicVineScraper.ComicVine;
using ComicVineScraper.Fixtures;
using ComicVineScraper.Model;

namespace ComicVineScraper.Tests.ComicVine;

public sealed class ComicVineHttpTests
{
	private const string Key = "test-key-1234";
	private static readonly ApiRequest Request = ApiRequest.SeriesDetail(new SeriesId(796));

	public enum Failure
	{
		ServerError,
		EmptyBody,
		InvalidXml,
		NoStatusCode,
		NetworkError,
		Timeout,
		ObjectNotFound,
	}

	[Test]
	public async Task A_good_response_is_parsed_and_the_request_carries_key_client_format_and_user_agent()
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Ok("<results><id>796</id></results>"));
		using var http = new ComicVineHttp(handler, Key, "ComicVineScraper.NET/9.9", clock, () => clock.Elapsed);

		XDocument doc = await http.GetAsync(Request, CancellationToken.None);

		await Assert.That(doc.Root!.Element("results")!.Element("id")!.Value).IsEqualTo("796");
		HttpRequestMessage sent = handler.Sent.Single().Request;
		await Assert.That(sent.RequestUri!.AbsoluteUri).IsEqualTo(
			"https://comicvine.gamespot.com/api/volume/4050-796/?api_key=test-key-1234&client=cvscraper&format=xml"
			+ "&field_list=name,start_year,publisher,image,count_of_issues,id");
		await Assert.That(string.Join(" ", sent.Headers.GetValues("User-Agent"))).IsEqualTo("ComicVineScraper.NET/9.9");
	}

	[Test]
	public async Task Http_401_with_status_code_100_is_an_invalid_key_and_is_not_retried()
	{
		RecordedResponse invalidKey = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, "invalid-key.txt"));
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Reply(invalidKey.Status, invalidKey.Body));
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(() => http.GetAsync(Request, CancellationToken.None)))!;

		await Assert.That(invalidKey.Status).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(error.Error).IsEqualTo(ComicVineError.InvalidApiKey);
		await Assert.That(error.ApiStatus).IsEqualTo(100);
		await Assert.That(error.IsRecoverable).IsFalse();
		await Assert.That(handler.Sent.Count).IsEqualTo(1);
		await Assert.That(clock.Delays).IsEmpty();
	}

	[Test]
	public async Task Status_code_107_is_a_rate_limit_and_is_not_retried()
	{
		RecordedResponse rateLimit = FixtureFiles.Read(Path.Combine(FixtureFiles.OutputFolder, "synthetic-rate-limit.txt"));
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Reply(rateLimit.Status, rateLimit.Body));
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(() => http.GetAsync(Request, CancellationToken.None)))!;

		await Assert.That(error.Error).IsEqualTo(ComicVineError.RateLimited);
		await Assert.That(error.ApiStatus).IsEqualTo(107);
		await Assert.That(handler.Sent.Count).IsEqualTo(1);
	}

	[Test]
	[MethodDataSource(nameof(Failures))]
	public async Task A_failure_is_retried_once_after_2500_ms(Failure failure)
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, Reply(failure), ScriptedHandler.Ok());
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		await http.GetAsync(Request, CancellationToken.None);

		await Assert.That(handler.Sent.Count).IsEqualTo(2);
		await Assert.That(clock.Delays).Contains(TimeSpan.FromMilliseconds(2500));
		await Assert.That(handler.Sent[1].At - handler.Sent[0].At).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(2500));
	}

	[Test]
	[MethodDataSource(nameof(Failures))]
	public async Task A_second_failure_throws_a_redacted_ComicVineException(Failure failure)
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, Reply(failure), Reply(failure));
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(() => http.GetAsync(Request, CancellationToken.None)))!;

		await Assert.That(error.Error).IsEqualTo(failure == Failure.ObjectNotFound ? ComicVineError.Api : ComicVineError.Network);
		await Assert.That(error.ApiStatus).IsEqualTo(failure == Failure.ObjectNotFound ? 101 : null);
		await Assert.That(error.IsRecoverable).IsTrue();
		await Assert.That(error.Url).IsEqualTo(
			"https://comicvine.gamespot.com/api/volume/4050-796/?api_key=...&client=cvscraper&format=xml&field_list=name,start_year,publisher,image,count_of_issues,id");
		await Assert.That(error.ToString()).DoesNotContain(Key);
		await Assert.That(handler.Sent.Count).IsEqualTo(2);
	}

	[Test]
	public async Task A_transport_error_that_quotes_the_url_is_redacted_too()
	{
		var clock = new ManualClock();
		Func<HttpRequestMessage, HttpResponseMessage> quoting = request => throw new HttpRequestException("failed: " + request.RequestUri);
		var handler = new ScriptedHandler(clock, quoting, quoting);
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		ComicVineException error = (await Assert.ThrowsAsync<ComicVineException>(() => http.GetAsync(Request, CancellationToken.None)))!;

		await Assert.That(error.ToString()).DoesNotContain(Key);
		await Assert.That(error.InnerException!.Message).Contains("api_key=...");
	}

	[Test]
	public async Task Cancellation_throws_OperationCanceledException_and_sends_nothing()
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Ok());
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(() => http.GetAsync(Request, cancelled.Token));

		await Assert.That(handler.Sent).IsEmpty();
	}

	[Test]
	public async Task Requests_start_at_least_1100_ms_apart_and_the_first_does_not_wait()
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Ok(), ScriptedHandler.Ok(), ScriptedHandler.Ok());
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);
		DateTime start = clock.Now;

		for (int i = 0; i < 3; i++)
		{
			await http.GetAsync(Request, CancellationToken.None);
		}

		DateTime[] at = handler.Sent.Select(s => s.At).ToArray();
		await Assert.That(at[0]).IsEqualTo(start);
		await Assert.That(at[1] - at[0]).IsEqualTo(TimeSpan.FromMilliseconds(1100));
		await Assert.That(at[2] - at[1]).IsEqualTo(TimeSpan.FromMilliseconds(1100));
	}

	[Test]
	public async Task A_wall_clock_that_jumps_back_an_hour_does_not_delay_the_next_request()
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Ok(), ScriptedHandler.Ok(), ScriptedHandler.Ok());
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		await http.GetAsync(Request, CancellationToken.None);
		clock.JumpWallClock(TimeSpan.FromHours(-1));
		await http.GetAsync(Request, CancellationToken.None);
		await http.GetAsync(Request, CancellationToken.None);

		await Assert.That(clock.Delays.Max()).IsLessThanOrEqualTo(TimeSpan.FromMilliseconds(1100));
		await Assert.That(clock.Delays.Count).IsEqualTo(2);
	}

	[Test]
	public async Task The_gap_holds_for_requests_from_many_threads_at_once()
	{
		const int Count = 12;
		var clock = new ManualClock { RealPause = TimeSpan.FromMilliseconds(15) };
		var handler = new ScriptedHandler(clock, Enumerable.Range(0, Count).Select(_ => ScriptedHandler.Ok()).ToArray());
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);
		using var start = new ManualResetEventSlim();

		Task[] requests = Enumerable.Range(0, Count)
			.Select(_ => Task.Run(async () =>
			{
				start.Wait();
				await http.GetAsync(Request, CancellationToken.None);
			}))
			.ToArray();
		start.Set();
		await Task.WhenAll(requests);

		DateTime[] at = handler.Sent.Select(s => s.At).OrderBy(t => t).ToArray();
		TimeSpan smallestGap = at.Zip(at.Skip(1), (a, b) => b - a).Min();
		await Assert.That(smallestGap).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1100));
	}

	[Test]
	public async Task Characters_XML_forbids_are_removed_and_emoji_are_kept()
	{
		var clock = new ManualClock();
		// UTF-8 cannot carry a lone surrogate, and the HTML decode leaves a surrogate entity as text.
		var handler = new ScriptedHandler(clock, ScriptedHandler.Ok("<results><name><![CDATA[a\u0001b￾ c \U0001F600 &#xD800;d &#x1F600;]]></name></results>"));
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		XDocument doc = await http.GetAsync(Request, CancellationToken.None);

		await Assert.That(doc.Root!.Element("results")!.Element("name")!.Value).IsEqualTo("ab c \U0001F600 &#xD800;d \U0001F600");
	}

	[Test]
	public async Task An_image_is_retried_once_without_the_api_delay_and_is_null_after_two_failures()
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock,
			ScriptedHandler.Reply(HttpStatusCode.NotFound, ""),
			ScriptedHandler.Reply(HttpStatusCode.OK, "not an image"),
			ScriptedHandler.Throw(new HttpRequestException("down")),
			_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png(30, 20)) });
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);
		var url = new Uri("https://comicvine.gamespot.com/a/uploads/scale_small/1.jpg");

		Bitmap? failed = await http.GetImageAsync(url, CancellationToken.None);
		using Bitmap? loaded = await http.GetImageAsync(url, CancellationToken.None);

		await Assert.That(failed).IsNull();
		await Assert.That(loaded!.Width).IsEqualTo(30);
		await Assert.That(clock.Delays.All(d => d == TimeSpan.FromMilliseconds(1100))).IsTrue();
		await Assert.That(handler.Sent[0].Request.RequestUri).IsEqualTo(url);
	}

	[Test]
	[Arguments("jpeg header only")]
	[Arguments("webp")]
	[Arguments("html body")]
	public async Task An_undecodable_image_body_is_null_not_an_exception(string kind)
	{
		byte[] body = kind switch
		{
			"html body" => Encoding.UTF8.GetBytes("<html><body>Gateway timeout</body></html>"),
			"jpeg header only" => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)74, (byte)70, (byte)73, (byte)70, 0, 1, 1 },
			"webp" => Encoding.ASCII.GetBytes("RIFF\u001a\0\0\0WEBPVP8 \u000e\0\0\0").Concat(new byte[14]).ToArray(),
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};
		var clock = new ManualClock();
		Func<HttpRequestMessage, HttpResponseMessage> reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
		var handler = new ScriptedHandler(clock, reply, reply);
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed);

		Bitmap? image = await http.GetImageAsync(new Uri("https://comicvine.gamespot.com/a/uploads/scale_small/1.jpg"), CancellationToken.None);

		await Assert.That(image).IsNull();
		await Assert.That(handler.Sent.Count).IsEqualTo(2);
	}

	[Test]
	[Arguments(typeof(OutOfMemoryException))]
	[Arguments(typeof(ExternalException))]
	[Arguments(typeof(ArgumentException))]
	public async Task Every_exception_GDI_plus_uses_for_a_bad_image_gives_null(Type thrown)
	{
		var clock = new ManualClock();
		Func<HttpRequestMessage, HttpResponseMessage> reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png(5, 5)) };
		var handler = new ScriptedHandler(clock, reply, reply);
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed) { DecodeImage = _ => throw (Exception)Activator.CreateInstance(thrown)! };

		Bitmap? image = await http.GetImageAsync(new Uri("https://comicvine.gamespot.com/a/uploads/scale_small/1.jpg"), CancellationToken.None);

		await Assert.That(image).IsNull();
		await Assert.That(handler.Sent.Count).IsEqualTo(2);
	}

	[Test]
	public async Task A_failure_in_the_decoder_that_is_not_a_bad_image_is_not_swallowed()
	{
		var clock = new ManualClock();
		var handler = new ScriptedHandler(clock, ScriptedHandler.Reply(HttpStatusCode.OK, "x"));
		using var http = new ComicVineHttp(handler, Key, "ua", clock, () => clock.Elapsed) { DecodeImage = _ => throw new InvalidOperationException("bug") };

		await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetImageAsync(new Uri("https://comicvine.gamespot.com/a/1.jpg"), CancellationToken.None));
	}


	public static IEnumerable<Func<Failure>> Failures() => Enum.GetValues(typeof(Failure)).Cast<Failure>().Select(f => (Func<Failure>)(() => f));

	internal static byte[] Png(int width, int height)
	{
		using var image = new Bitmap(width, height);
		using var stream = new MemoryStream();
		image.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	private static Func<HttpRequestMessage, HttpResponseMessage> Reply(Failure failure) => failure switch
	{
		Failure.ServerError => ScriptedHandler.Reply(HttpStatusCode.ServiceUnavailable, "<html>down</html>"),
		Failure.EmptyBody => ScriptedHandler.Reply(HttpStatusCode.OK, "  \n"),
		Failure.InvalidXml => ScriptedHandler.Reply(HttpStatusCode.OK, "<response><status_code>1</status_code>"),
		Failure.NoStatusCode => ScriptedHandler.Reply(HttpStatusCode.OK, "<response><results/></response>"),
		Failure.NetworkError => ScriptedHandler.Throw(new HttpRequestException("The remote name could not be resolved")),
		Failure.Timeout => ScriptedHandler.Throw(new TaskCanceledException("timed out")),
		Failure.ObjectNotFound => ScriptedHandler.Reply(HttpStatusCode.OK, ScriptedHandler.Body(101)),
		_ => throw new ArgumentOutOfRangeException(nameof(failure)),
	};
}
