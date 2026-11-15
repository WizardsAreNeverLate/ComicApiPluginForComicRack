using System;
using System.Net.Http;
using System.Threading;
using ComicApiPlugin.Matching;
using ComicApiPlugin.Service;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// The cover comparison, done by the local API service, which holds the OpenCV library. A service that cannot be started
/// or reached, or that cannot load OpenCV, is an OpenCvUnavailableException, so the engine asks the user as it does
/// without OpenCV.
/// </summary>
internal sealed class ServiceCoverComparer(ServiceConnection service) : ICoverComparer
{
	/// <summary>A comparison takes about 100 ms. The limit only keeps a service that hangs from holding a run that cannot cancel this call.</summary>
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

	/// <summary>Blocks on the request. The engine calls it on a pool thread, as ICoverComparer asks.</summary>
	public CoverSimilarity? Compare(byte[] bookCover, byte[] candidateCover)
	{
		using var limit = new CancellationTokenSource(Limit);
		try
		{
			return service.CallAsync(
				() => new HttpRequestMessage(HttpMethod.Post, ServiceProtocol.CoverComparePath)
				{
					Content = ServiceConnection.Multipart((ServiceProtocol.BookCoverPart, bookCover), (ServiceProtocol.CandidateCoverPart, candidateCover)),
				},
				async response => VerseDbWire.ToDomain(await ServiceConnection.ReadJsonAsync<CompareResponseDto>(response).ConfigureAwait(false)),
				error => error.OpenCvUnavailable is { } reason ? new OpenCvUnavailableException(reason) : null,
				limit.Token).GetAwaiter().GetResult();
		}
		catch (ServiceUnavailableException error)
		{
			throw new OpenCvUnavailableException(error.Message, error);
		}
		catch (OperationCanceledException error)
		{
			throw new OpenCvUnavailableException($"The local API service did not compare the covers within {Limit.TotalSeconds:0} seconds.", error);
		}
	}
}
