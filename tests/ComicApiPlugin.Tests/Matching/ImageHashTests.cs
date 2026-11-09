using System.Drawing;
using System.Threading.Tasks;
using ComicApiPlugin.Matching;

namespace ComicApiPlugin.Tests.Matching;

public sealed class ImageHashTests
{
	[Test]
	public async Task Similarity_is_the_share_of_equal_bits()
	{
		await Assert.That(ImageHash.Similarity(0b1011, 0b1011)).IsEqualTo(1.0);
		await Assert.That(ImageHash.Similarity(0, ulong.MaxValue)).IsEqualTo(0.0);
		await Assert.That(ImageHash.Similarity(0, 0b111)).IsEqualTo(1 - 3 / 64.0);
		await Assert.That(ImageHash.Similarity(null, 0)).IsEqualTo(0.0);
	}

	[Test]
	public async Task A_black_image_hashes_to_zero_and_a_half_dark_image_does_not()
	{
		using var flat = new Bitmap(40, 40);
		using (Graphics g = Graphics.FromImage(flat))
		{
			g.Clear(Color.Black);
		}
		using var split = new Bitmap(40, 40);
		using (Graphics g = Graphics.FromImage(split))
		{
			g.Clear(Color.Black);
			g.FillRectangle(Brushes.White, 20, 0, 20, 40);
		}

		await Assert.That(ImageHash.Compute(flat)).IsEqualTo(0UL);
		await Assert.That(ImageHash.Compute(split)).IsEqualTo(0xFFFFFFFF00000000UL);
		await Assert.That(ImageHash.Compute(null)).IsNull();
	}
}
