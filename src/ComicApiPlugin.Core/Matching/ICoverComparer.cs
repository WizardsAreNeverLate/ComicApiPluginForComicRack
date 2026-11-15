using System;

namespace ComicApiPlugin.Matching;

/// <summary>Compares two cover images. Implementations are thread-safe and may block for a moment, so callers run them off the UI thread.</summary>
public interface ICoverComparer
{
	/// <summary>
	/// Both arguments are encoded images (JPEG, PNG or WebP). Null when either cannot be decoded.
	/// Throws <see cref="OpenCvUnavailableException"/> when the comparison cannot be done at all.
	/// </summary>
	CoverSimilarity? Compare(byte[] bookCover, byte[] candidateCover);
}

/// <summary>
/// The cover comparison is unavailable: the OpenCV native library cannot be loaded, or the local API service that holds
/// it cannot be reached. Nothing else is affected.
/// </summary>
public sealed class OpenCvUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
