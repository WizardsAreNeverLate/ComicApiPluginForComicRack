using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ComicApiPlugin.Fixtures;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Service.Covers;

namespace ComicApiPlugin.Service.Tests;

public sealed class CoverEndpointTests
{
	[Test]
	public async Task The_same_cover_with_five_percent_cropped_off_matches_with_the_scores_the_comparer_gives()
	{
		using RunningService service = await RunningService.StartAsync();
		using Bitmap cover = SyntheticCover.Make(2);
		using Bitmap cropped = SyntheticCover.Cropped(cover, 0.05);
		byte[] book = SyntheticCover.Encode(cover, ImageFormat.Png);
		byte[] candidate = SyntheticCover.Encode(cropped, ImageFormat.Jpeg);

		(HttpStatusCode status, CoverSimilarity? similarity) = await CompareAsync(service, book, candidate);

		await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(similarity!.Value.IsMatch).IsTrue();
		await Assert.That(similarity).IsEqualTo(new OpenCvCoverComparer().Compare(book, candidate));
	}

	[Test]
	public async Task Another_issue_with_the_same_title_banner_does_not_match()
	{
		using RunningService service = await RunningService.StartAsync();
		using Bitmap first = SyntheticCover.Make(3);
		using Bitmap sibling = SyntheticCover.Make(4);
		byte[] book = SyntheticCover.Encode(first, ImageFormat.Png);
		byte[] candidate = SyntheticCover.Encode(sibling, ImageFormat.Png);

		(HttpStatusCode status, CoverSimilarity? similarity) = await CompareAsync(service, book, candidate);

		await Assert.That(status).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(similarity!.Value.IsMatch).IsFalse();
		await Assert.That(similarity).IsEqualTo(new OpenCvCoverComparer().Compare(book, candidate));
	}

	[Test]
	public async Task Bytes_that_are_not_an_image_give_no_similarity()
	{
		using RunningService service = await RunningService.StartAsync();
		using HttpClient client = service.Client();
		using Bitmap cover = SyntheticCover.Make(6);
		byte[] image = SyntheticCover.Encode(cover, ImageFormat.Png);

		using HttpResponseMessage response = await client.PostAsync(ServiceProtocol.CoverComparePath,
			RunningService.Multipart((ServiceProtocol.BookCoverPart, image), (ServiceProtocol.CandidateCoverPart, [1, 2, 3, 4, 5])));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("""{"similarity":null}""");
	}

	[Test]
	public async Task A_comparison_that_OpenCV_fails_is_a_500_that_names_the_service_error_and_the_service_goes_on_serving()
	{
		using RunningService service = await RunningService.StartAsync();
		using HttpClient client = service.Client();
		using Bitmap cover = SyntheticCover.Make(6);
		byte[] image = SyntheticCover.Encode(cover, ImageFormat.Png);

		using HttpResponseMessage response = await client.PostAsync(ServiceProtocol.CoverComparePath,
			RunningService.Multipart((ServiceProtocol.BookCoverPart, image), (ServiceProtocol.CandidateCoverPart, SyntheticCover.OversizedPng())));
		using JsonDocument error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		using HttpResponseMessage health = await client.GetAsync(ServiceProtocol.HealthPath);

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
		await Assert.That(error.RootElement.GetProperty("serviceError").GetString()).IsNotNull().And.IsNotEmpty();
		await Assert.That(error.RootElement.TryGetProperty("openCvUnavailable", out _)).IsFalse();
		await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task A_request_without_both_covers_is_a_400_that_says_what_is_missing()
	{
		using RunningService service = await RunningService.StartAsync();
		using HttpClient client = service.Client();

		using HttpResponseMessage oneCover = await client.PostAsync(ServiceProtocol.CoverComparePath, RunningService.Multipart((ServiceProtocol.BookCoverPart, [1])));
		using HttpResponseMessage notMultipart = await client.PostAsync(ServiceProtocol.CoverComparePath, new ByteArrayContent([1]));

		await Assert.That(oneCover.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(await oneCover.Content.ReadAsStringAsync()).IsEqualTo("""{"comicVine":null,"message":"The multipart\/form-data part 'candidate' is required."}""");
		await Assert.That(notMultipart.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
		await Assert.That(await notMultipart.Content.ReadAsStringAsync()).IsEqualTo("""{"comicVine":null,"message":"The body must be multipart\/form-data."}""");
	}

	[Test]
	public async Task A_service_without_its_OpenCV_library_answers_503_with_the_reason_and_goes_on_serving()
	{
		string folder = CopyServiceWithout("OpenCvSharpExtern.dll");
		try
		{
			using RunningService service = await RunningService.StartAsync(folder: folder);
			using HttpClient client = service.Client();
			using Bitmap cover = SyntheticCover.Make(1);
			byte[] image = SyntheticCover.Encode(cover, ImageFormat.Png);

			using HttpResponseMessage response = await client.PostAsync(ServiceProtocol.CoverComparePath,
				RunningService.Multipart((ServiceProtocol.BookCoverPart, image), (ServiceProtocol.CandidateCoverPart, image)));
			using JsonDocument error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			using HttpResponseMessage health = await client.GetAsync(ServiceProtocol.HealthPath);

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
			await Assert.That(error.RootElement.GetProperty("openCvUnavailable").GetString()).Contains("OpenCvSharpExtern");
			await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	private static async Task<(HttpStatusCode Status, CoverSimilarity? Similarity)> CompareAsync(RunningService service, byte[] book, byte[] candidate)
	{
		using HttpClient client = service.Client();
		using HttpResponseMessage response = await client.PostAsync(ServiceProtocol.CoverComparePath,
			RunningService.Multipart((ServiceProtocol.BookCoverPart, book), (ServiceProtocol.CandidateCoverPart, candidate)));
		return (response.StatusCode, VerseDbWire.ToDomain(WireJson.FromJson<CompareResponseDto>(await response.Content.ReadAsStringAsync())));
	}

	/// <summary>A copy of the built service's top-level files, which is all it needs to start, except the named one.</summary>
	private static string CopyServiceWithout(string fileName)
	{
		string folder = Path.Combine(Path.GetTempPath(), "comicapi-service-tests", "service-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		foreach (string file in Directory.GetFiles(AppContext.BaseDirectory))
		{
			if (!Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase))
			{
				File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
			}
		}
		return folder;
	}
}
