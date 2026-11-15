using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ComicApiPlugin.Matching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog;

namespace ComicApiPlugin.Service.Covers;

/// <summary>
/// ICoverComparer as a POST endpoint. The two covers are the parts of a multipart/form-data body. The comparison takes
/// about 100 ms of processor time, so it runs on the thread pool.
/// </summary>
internal sealed class CoverEndpoints(ICoverComparer comparer)
{
	public void Map(IEndpointRouteBuilder app) => app.MapPost(ServiceProtocol.CoverComparePath, async (HttpContext context) =>
	{
		try
		{
			IReadOnlyDictionary<string, byte[]> parts = await EndpointIo.ReadParts(context);
			byte[] book = EndpointIo.Part(parts, ServiceProtocol.BookCoverPart);
			byte[] candidate = EndpointIo.Part(parts, ServiceProtocol.CandidateCoverPart);
			CoverSimilarity? similarity = await Task.Run(() => comparer.Compare(book, candidate), context.RequestAborted);
			await EndpointIo.WriteJson(context, StatusCodes.Status200OK, VerseDbWire.ToDto(similarity));
		}
		catch (WireFormatException error)
		{
			await EndpointIo.WriteJson(context, StatusCodes.Status400BadRequest, new ErrorResponseDto { Message = error.Message });
		}
		catch (OpenCvUnavailableException error)
		{
			Log.Warning("The covers cannot be compared: {Message:l}", error.Message);
			await EndpointIo.WriteJson(context, StatusCodes.Status503ServiceUnavailable, new ErrorResponseDto { OpenCvUnavailable = error.Message });
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			// The plugin cancelled the run and closed the connection, so there is no one to answer.
		}
	});
}
