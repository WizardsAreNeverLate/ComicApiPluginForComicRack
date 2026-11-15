using System.Globalization;

namespace ComicApiPlugin.Matching;

/// <summary>
/// How alike two cover images are, by two independent measures. Correlation is a whole-image grayscale
/// correlation (-1 to 1) and survives low resolution. FeatureMatch is the share of ORB keypoints that agree on one
/// geometric transform (0 to 1) and survives crops and rotations. docs/port/architecture.md has the evidence for the thresholds.
/// </summary>
public readonly record struct CoverSimilarity(double Correlation, double FeatureMatch)
{
	/// <summary>Lowest correlation that counts: real covers scored 0.82 or more, and no other cover, siblings of a series included, scored above 0.28.</summary>
	public const double CorrelationThreshold = 0.70;

	/// <summary>Lowest feature agreement that counts: real covers scored 0.36 or more at full size (0.22 from a 160 px thumbnail, where correlation decides), and no other cover scored above 0.14.</summary>
	public const double FeatureThreshold = 0.25;

	public bool IsMatch => Correlation >= CorrelationThreshold || FeatureMatch >= FeatureThreshold;

	public override string ToString() =>
		string.Format(CultureInfo.InvariantCulture, "correlation {0:0.00}, features {1:0.00}", Correlation, FeatureMatch);
}
