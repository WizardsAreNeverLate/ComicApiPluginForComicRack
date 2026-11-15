using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace ComicApiPlugin.Matching;

/// <summary>
/// ICoverComparer on OpenCV. Two measures, as docs/port/architecture.md describes: a correlation of 64x96 blurred
/// grayscale copies, and the share of ORB keypoint matches that agree on one homography.
/// </summary>
public sealed class OpenCvCoverComparer : ICoverComparer
{
	private const int ThumbWidth = 64;
	private const int ThumbHeight = 96;
	private const int FeatureHeight = 800;
	private const int MaxKeypoints = 1500;
	private const int MinKeypoints = 10;
	private const int MinGoodMatches = 8;
	private const double LoweRatio = 0.75;
	private const double RansacReprojectionError = 5.0;
	private const double FlatDeviation = 1.0;

	public CoverSimilarity? Compare(byte[] bookCover, byte[] candidateCover)
	{
		OpenCvNative.EnsureLoaded();
		try
		{
			using Mat? book = Gray(bookCover);
			using Mat? candidate = Gray(candidateCover);
			if (book is null || candidate is null)
			{
				return null;
			}
			return new CoverSimilarity(Correlation(book, candidate), FeatureMatch(book, candidate));
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			throw new OpenCvUnavailableException("The OpenCV library could not be used: " + e.Message, e);
		}
	}

	private static Mat? Gray(byte[] encoded)
	{
		using Mat color = Cv2.ImDecode(encoded, ImreadModes.Color);
		if (color.Empty())
		{
			return null;
		}
		var gray = new Mat();
		Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
		return gray;
	}

	private static double Correlation(Mat a, Mat b)
	{
		using Mat smallA = Small(a);
		using Mat smallB = Small(b);
		// A page of one flat colour has no variance to correlate. OpenCV reports 1 or NaN for it, and either would be a false answer.
		if (IsFlat(smallA) || IsFlat(smallB))
		{
			return 0;
		}
		using var result = new Mat();
		Cv2.MatchTemplate(smallA, smallB, result, TemplateMatchModes.CCoeffNormed);
		float score = result.At<float>(0, 0);
		return float.IsNaN(score) ? 0 : score;
	}

	private static bool IsFlat(Mat gray)
	{
		Cv2.MeanStdDev(gray, out Scalar _, out Scalar deviation);
		return deviation.Val0 < FlatDeviation;
	}

	private static Mat Small(Mat gray)
	{
		using var resized = new Mat();
		Cv2.Resize(gray, resized, new Size(ThumbWidth, ThumbHeight), 0, 0, InterpolationFlags.Area);
		var blurred = new Mat();
		Cv2.GaussianBlur(resized, blurred, new Size(3, 3), 0);
		return blurred;
	}

	private static double FeatureMatch(Mat a, Mat b)
	{
		using Mat scaledA = ToFeatureHeight(a);
		using Mat scaledB = ToFeatureHeight(b);
		using var orb = ORB.Create(MaxKeypoints);
		using var descriptorsA = new Mat();
		using var descriptorsB = new Mat();
		orb.DetectAndCompute(scaledA, null, out KeyPoint[] pointsA, descriptorsA);
		orb.DetectAndCompute(scaledB, null, out KeyPoint[] pointsB, descriptorsB);
		if (pointsA.Length < MinKeypoints || pointsB.Length < MinKeypoints)
		{
			return 0;
		}

		using var matcher = new BFMatcher(NormTypes.Hamming);
		DMatch[][] pairs = matcher.KnnMatch(descriptorsA, descriptorsB, 2);
		List<DMatch> good = [.. pairs.Where(p => p.Length == 2 && p[0].Distance < LoweRatio * p[1].Distance).Select(p => p[0])];
		if (good.Count < MinGoodMatches)
		{
			return 0;
		}

		Point2d[] from = [.. good.Select(m => new Point2d(pointsA[m.QueryIdx].Pt.X, pointsA[m.QueryIdx].Pt.Y))];
		Point2d[] to = [.. good.Select(m => new Point2d(pointsB[m.TrainIdx].Pt.X, pointsB[m.TrainIdx].Pt.Y))];
		using var mask = new Mat();
		using Mat homography = Cv2.FindHomography(from, to, HomographyMethods.Ransac, RansacReprojectionError, mask);
		if (homography.Empty() || mask.Empty())
		{
			return 0;
		}
		return Cv2.CountNonZero(mask) / (double)Math.Min(pointsA.Length, pointsB.Length);
	}

	private static Mat ToFeatureHeight(Mat gray)
	{
		var resized = new Mat();
		double scale = FeatureHeight / (double)gray.Height;
		Cv2.Resize(gray, resized, new Size(Math.Max(1, (int)Math.Round(gray.Width * scale)), FeatureHeight), 0, 0, InterpolationFlags.Area);
		return resized;
	}
}
