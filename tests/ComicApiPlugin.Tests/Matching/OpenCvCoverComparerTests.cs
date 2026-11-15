using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using ComicApiPlugin.Matching;

namespace ComicApiPlugin.Tests.Matching;

public sealed class OpenCvCoverComparerTests
{
	private readonly OpenCvCoverComparer comparer = new();

	[Test]
	public async Task The_same_cover_re_encoded_at_a_smaller_size_matches()
	{
		using Bitmap cover = SyntheticCover.Make(1);
		using Bitmap thumbnail = SyntheticCover.Scaled(cover, 0.4);

		CoverSimilarity? score = comparer.Compare(SyntheticCover.Encode(cover, ImageFormat.Png), SyntheticCover.Encode(thumbnail, ImageFormat.Jpeg, 50));

		await Assert.That(score!.Value.IsMatch).IsTrue();
	}

	[Test]
	public async Task The_same_cover_with_five_percent_cropped_off_matches()
	{
		using Bitmap cover = SyntheticCover.Make(2);
		using Bitmap cropped = SyntheticCover.Cropped(cover, 0.05);

		CoverSimilarity? score = comparer.Compare(SyntheticCover.Encode(cover, ImageFormat.Png), SyntheticCover.Encode(cropped, ImageFormat.Jpeg));

		await Assert.That(score!.Value.IsMatch).IsTrue();
	}

	[Test]
	public async Task Another_issue_with_the_same_title_banner_does_not_match()
	{
		using Bitmap first = SyntheticCover.Make(3);
		using Bitmap sibling = SyntheticCover.Make(4);

		CoverSimilarity? score = comparer.Compare(SyntheticCover.Encode(first, ImageFormat.Png), SyntheticCover.Encode(sibling, ImageFormat.Png));

		await Assert.That(score!.Value.IsMatch).IsFalse();
	}

	[Test]
	public async Task A_flat_page_matches_nothing_and_is_not_an_error()
	{
		using Bitmap cover = SyntheticCover.Make(5);
		using var flat = new Bitmap(SyntheticCover.Width, SyntheticCover.Height);
		using (Graphics g = Graphics.FromImage(flat))
		{
			g.Clear(Color.Black);
		}

		CoverSimilarity? score = comparer.Compare(SyntheticCover.Encode(cover, ImageFormat.Png), SyntheticCover.Encode(flat, ImageFormat.Png));

		await Assert.That(score!.Value.IsMatch).IsFalse();
	}

	[Test]
	public async Task Bytes_that_are_not_an_image_give_no_score()
	{
		using Bitmap cover = SyntheticCover.Make(6);
		byte[] image = SyntheticCover.Encode(cover, ImageFormat.Png);
		byte[] garbage = [1, 2, 3, 4, 5];

		await Assert.That(comparer.Compare(image, garbage)).IsNull();
		await Assert.That(comparer.Compare(garbage, image)).IsNull();
	}
}
