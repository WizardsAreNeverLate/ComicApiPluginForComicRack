using System;
using System.Threading;
using System.Threading.Tasks;
using ComicApiPlugin.Model;

namespace ComicApiPlugin.VerseDb;

/// <summary>What the cover search asks VerseDB. Throws OperationCanceledException on ct and VerseDbException on failure.</summary>
public interface IVerseDb
{
	/// <summary>Looks up an issue by the photo of its cover. The bytes are a JPEG or PNG under 5 MB.</summary>
	Task<CoverLookup> LookUpCoverAsync(byte[] image, CancellationToken ct);

	Task<IssueMetadata> GetIssueAsync(VerseDbIssueId issue, CancellationToken ct);

	/// <summary>The raw bytes of a cover picture; decoding is the caller's job.</summary>
	Task<byte[]> GetImageAsync(Uri url, CancellationToken ct);
}
