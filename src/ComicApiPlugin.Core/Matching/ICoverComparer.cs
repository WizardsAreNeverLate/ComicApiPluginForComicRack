namespace ComicApiPlugin.Matching;

/// <summary>Compares two cover images. Implementations are pure and thread-safe, and take about 100 ms, so callers run them off the UI thread.</summary>
public interface ICoverComparer
{
	/// <summary>
	/// Both arguments are encoded images (JPEG, PNG or WebP). Null when either cannot be decoded.
	/// Throws <see cref="OpenCvUnavailableException"/> when the image library cannot be loaded.
	/// </summary>
	CoverSimilarity? Compare(byte[] bookCover, byte[] candidateCover);
}
