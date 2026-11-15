using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ComicApiPlugin.Model;
using ComicApiPlugin.VerseDb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog;

namespace ComicApiPlugin.Service.VerseDb;

/// <summary>
/// The three IVerseDb calls as POST endpoints. Each request names its API token and its run in headers. The cover of a
/// lookup is a multipart/form-data part, and a cover picture is answered as the bytes VerseDB served. A VerseDB failure
/// becomes an error body with a status code.
/// </summary>
internal sealed class VerseDbEndpoints(VerseDbClients clients)
{
	public void Map(IEndpointRouteBuilder app)
	{
		app.MapPost(ServiceProtocol.VerseDbLookupCoverPath, (HttpContext context) => Answer(context, async verseDb =>
		{
			IReadOnlyDictionary<string, byte[]> parts = await EndpointIo.ReadParts(context);
			CoverLookup lookup = await verseDb.LookUpCoverAsync(EndpointIo.Part(parts, ServiceProtocol.ImagePart), context.RequestAborted);
			await EndpointIo.WriteJson(context, StatusCodes.Status200OK, VerseDbWire.ToDto(lookup));
		}));
		app.MapPost(ServiceProtocol.VerseDbIssuePath, (HttpContext context) => Answer(context, async verseDb =>
		{
			VerseDbIssueRequestDto request = await EndpointIo.ReadJson<VerseDbIssueRequestDto>(context);
			IssueMetadata issue = await verseDb.GetIssueAsync(new VerseDbIssueId(request.Issue), context.RequestAborted);
			await EndpointIo.WriteJson(context, StatusCodes.Status200OK, VerseDbWire.ToDto(issue));
		}));
		app.MapPost(ServiceProtocol.VerseDbImagePath, (HttpContext context) => Answer(context, async verseDb =>
		{
			VerseDbImageRequestDto request = await EndpointIo.ReadJson<VerseDbImageRequestDto>(context);
			Uri url = WireJson.Url(WireJson.Need(request.Url, "url"), "url");
			byte[] image = await verseDb.GetImageAsync(url, context.RequestAborted);
			context.Response.ContentType = VerseDbWire.ImageContentType;
			await context.Response.Body.WriteAsync(image, context.RequestAborted);
		}));
	}

	/// <summary>The HTTP status of each VerseDB failure. The body holds VerseDB's own status, so the plugin does not read this one.</summary>
	internal static int StatusOf(VerseDbException error) => error.Status switch
	{
		HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
		HttpStatusCode.TooManyRequests => StatusCodes.Status429TooManyRequests,
		_ => StatusCodes.Status502BadGateway,
	};

	private async Task Answer(HttpContext context, Func<VerseDbHttp, Task> answer)
	{
		try
		{
			string token = EndpointIo.Header(context, ServiceProtocol.VerseDbTokenHeader);
			RunScope.Current = EndpointIo.Header(context, ServiceProtocol.RunIdHeader);
			await answer(clients.For(token));
		}
		catch (WireFormatException error)
		{
			await EndpointIo.WriteJson(context, StatusCodes.Status400BadRequest, new ErrorResponseDto { Message = error.Message });
		}
		catch (VerseDbException error)
		{
			Log.Warning("VerseDB request failed (status {Status}): {Message:l} (run {RunId:l})", (int?)error.Status, error.Message, RunScope.Current);
			await EndpointIo.WriteJson(context, StatusOf(error), new ErrorResponseDto { VerseDb = VerseDbWire.ToDto(error) });
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			// The plugin cancelled the run and closed the connection, so there is no one to answer.
		}
	}
}
