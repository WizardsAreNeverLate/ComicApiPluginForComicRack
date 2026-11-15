using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ComicApiPlugin.ComicVine;
using ComicApiPlugin.Engine;
using ComicApiPlugin.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog;

namespace ComicApiPlugin.Service.ComicVine;

/// <summary>
/// The six IComicVine calls as POST endpoints. Each request names its API key and its run in headers and carries the
/// search settings in its JSON body. A Comic Vine failure becomes an error body with a status code. Search and issues
/// answer in JSON Lines, so the paging progress reaches the plugin while the query runs.
/// </summary>
internal sealed class ComicVineEndpoints(ComicVineSessions sessions)
{
	public void Map(IEndpointRouteBuilder app)
	{
		app.MapPost(ServiceProtocol.ComicVineSearchPath, (HttpContext context) => Answer<SearchRequestDto>(context, (catalog, request) =>
		{
			string terms = WireJson.Need(request.Terms, "terms");
			return Stream(context, (progress, ct) => catalog.SearchSeriesAsync(terms, progress, ct),
				found => new StreamLineDto { Series = [.. found.Select(ComicVineWire.ToDto)] });
		}));
		app.MapPost(ServiceProtocol.ComicVineIssuesPath, (HttpContext context) => Answer<IssuesRequestDto>(context, (catalog, request) =>
			Stream(context, (progress, ct) => catalog.GetIssuesAsync(new SeriesId(request.Series), progress, ct),
				found => new StreamLineDto { Issues = [.. found.Select(ComicVineWire.ToDto)] })));
		app.MapPost(ServiceProtocol.ComicVineFindIssuePath, (HttpContext context) => Answer<FindIssueRequestDto>(context, async (catalog, request) =>
		{
			string number = WireJson.Need(request.IssueNumber, "issueNumber");
			IssueRef? found = await catalog.FindIssueAsync(new SeriesId(request.Series), number, context.RequestAborted);
			await EndpointIo.WriteJson(context, StatusCodes.Status200OK, new FindIssueResponseDto { Issue = found is null ? null : ComicVineWire.ToDto(found) });
		}));
		app.MapPost(ServiceProtocol.ComicVineIssuePath, (HttpContext context) => Answer<IssueRequestDto>(context, async (catalog, request) =>
		{
			Issue issue = await catalog.GetIssueAsync(new IssueId(request.Issue), request.IncludeRating, context.RequestAborted);
			await EndpointIo.WriteJson(context, StatusCodes.Status200OK, ComicVineWire.ToDto(issue));
		}));
		app.MapPost(ServiceProtocol.ComicVineResolveSeriesPath, (HttpContext context) => Answer<ResolveSeriesRequestDto>(context, async (catalog, request) =>
		{
			SeriesRef? series = await catalog.ResolveSeriesAsync(ComicVineWire.ToDomain(request), context.RequestAborted);
			await EndpointIo.WriteJson(context, StatusCodes.Status200OK, new ResolveSeriesResponseDto { Series = series is null ? null : ComicVineWire.ToDto(series) });
		}));
		app.MapPost(ServiceProtocol.ComicVineImagePath, (HttpContext context) => Answer<ImageRequestDto>(context, async (catalog, request) =>
		{
			Uri url = WireJson.Url(WireJson.Need(request.Url, "url"), "url");
			using Bitmap? image = await catalog.GetImageAsync(url, context.RequestAborted);
			if (image is null)
			{
				context.Response.StatusCode = StatusCodes.Status204NoContent;
				return;
			}
			// PNG keeps every pixel of the decoded image, so the plugin gets the bitmap the catalog made.
			using var png = new MemoryStream();
			image.Save(png, ImageFormat.Png);
			context.Response.ContentType = "image/png";
			await context.Response.Body.WriteAsync(png.GetBuffer().AsMemory(0, (int)png.Length), context.RequestAborted);
		}));
	}

	/// <summary>The HTTP status of each Comic Vine failure. The body names the kind, so the plugin does not read the status.</summary>
	internal static int StatusOf(ComicVineError error) => error switch
	{
		ComicVineError.InvalidApiKey => StatusCodes.Status401Unauthorized,
		ComicVineError.RateLimited => StatusCodes.Status429TooManyRequests,
		_ => StatusCodes.Status502BadGateway,
	};

	private async Task Answer<TRequest>(HttpContext context, Func<ComicVineCatalog, TRequest, Task> answer) where TRequest : ComicVineRequestDto
	{
		try
		{
			string apiKey = EndpointIo.Header(context, ServiceProtocol.ComicVineKeyHeader);
			string runId = EndpointIo.Header(context, ServiceProtocol.RunIdHeader);
			RunScope.Current = runId;
			TRequest request = await EndpointIo.ReadJson<TRequest>(context);
			ComicVineCatalog catalog = sessions.Catalog(apiKey, runId, WireJson.Need(request.Settings, "settings"));
			await answer(catalog, request);
		}
		catch (WireFormatException error)
		{
			await EndpointIo.WriteJson(context, StatusCodes.Status400BadRequest, new ErrorResponseDto { Message = error.Message });
		}
		catch (ComicVineException error)
		{
			Log.Warning("Comic Vine request failed ({Kind:l}, status {ApiStatus}): {Url:l} (run {RunId:l})", error.Error.ToString(), error.ApiStatus, error.Url, RunScope.Current);
			await EndpointIo.WriteJson(context, StatusOf(error.Error), new ErrorResponseDto { ComicVine = ComicVineWire.ToDto(error) });
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			// The plugin cancelled the scrape and closed the connection, so there is no one to answer.
		}
	}

	/// <summary>
	/// Writes each progress report as a line while the query runs, then the result as the last line. A failure before the
	/// first line leaves the response unstarted, and the caller answers it with a status code. A later one is the last line.
	/// </summary>
	private static async Task Stream<T>(
		HttpContext context, Func<IProgress<QueryProgress>, CancellationToken, Task<T>> query, Func<T, StreamLineDto> result)
	{
		CancellationToken ct = context.RequestAborted;
		Channel<QueryProgress> reports = Channel.CreateUnbounded<QueryProgress>(new UnboundedChannelOptions { SingleReader = true });
		Task writing = WriteProgress(context, reports.Reader);
		StreamLineDto last;
		try
		{
			last = result(await query(new ChannelProgress(reports.Writer), ct));
		}
		catch (Exception error) when (!ct.IsCancellationRequested)
		{
			reports.Writer.Complete();
			await writing;
			if (!context.Response.HasStarted)
			{
				throw;
			}
			if (error is ComicVineException comicVine)
			{
				Log.Warning("Comic Vine request failed ({Kind:l}, status {ApiStatus}): {Url:l} (run {RunId:l})", comicVine.Error.ToString(), comicVine.ApiStatus, comicVine.Url, RunScope.Current);
				last = new StreamLineDto { Error = ComicVineWire.ToDto(comicVine) };
			}
			else
			{
				Log.Error(error, "{Path:l} failed inside the service (run {RunId:l})", context.Request.Path.Value, RunScope.Current);
				last = new StreamLineDto { ServiceError = ServiceErrors.Describe(error) };
			}
		}
		finally
		{
			reports.Writer.TryComplete();
			await writing;
		}
		await WriteLine(context, last);
	}

	private static async Task WriteProgress(HttpContext context, ChannelReader<QueryProgress> reports)
	{
		try
		{
			await foreach (QueryProgress report in reports.ReadAllAsync())
			{
				await WriteLine(context, new StreamLineDto { Progress = ComicVineWire.ToDto(report) });
			}
		}
		catch (Exception error) when (error is OperationCanceledException or IOException)
		{
			// The plugin closed the connection. The query sees the same cancellation and ends.
		}
	}

	private static async Task WriteLine(HttpContext context, StreamLineDto line)
	{
		if (!context.Response.HasStarted)
		{
			context.Response.ContentType = ComicVineWire.LinesContentType;
		}
		await context.Response.WriteAsync(WireJson.ToJson(line) + "\n", EndpointIo.Utf8, context.RequestAborted);
		await context.Response.Body.FlushAsync(context.RequestAborted);
	}

	private sealed class ChannelProgress(ChannelWriter<QueryProgress> writer) : IProgress<QueryProgress>
	{
		public void Report(QueryProgress value) => writer.TryWrite(value);
	}
}
