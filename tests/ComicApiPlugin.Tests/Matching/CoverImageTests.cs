using System.Drawing;
using System.Threading.Tasks;
using ComicApiPlugin.Matching;

namespace ComicApiPlugin.Tests.Matching;

public sealed class CoverImageTests
{
	[Test]
	[Arguments(120, 100, false)]
	[Arguments(121, 100, true)]
	[Arguments(149, 100, true)]
	[Arguments(150, 100, false)]
	[Arguments(100, 150, false)]
	public async Task Strips_only_a_two_page_spread(int width, int height, bool strips)
	{
		var image = new Bitmap(width, height);
		using Bitmap result = CoverImage.StripBackCover(image);

		await Assert.That(ReferenceEquals(result, image)).IsEqualTo(!strips);
		await Assert.That(result.Width).IsEqualTo(strips ? width / 2 : width);
	}

	[Test]
	public async Task Leaves_the_argument_open_for_its_owner()
	{
		using var image = new Bitmap(130, 100);
		using Bitmap front = CoverImage.StripBackCover(image);

		await Assert.That(image.Width).IsEqualTo(130);
	}

	[Test]
	public async Task Keeps_the_right_half_whatever_the_image_resolution()
	{
		using var source = new Bitmap(131, 100);
		using (Graphics g = Graphics.FromImage(source))
		{
			g.Clear(Color.Red);
			g.FillRectangle(Brushes.Blue, 66, 0, 65, 100);
		}
		var image = new Bitmap(source);
		image.SetResolution(300, 300);

		using Bitmap front = CoverImage.StripBackCover(image);

		await Assert.That(front.GetPixel(0, 0).ToArgb()).IsEqualTo(Color.Blue.ToArgb());
		await Assert.That(front.GetPixel(64, 99).ToArgb()).IsEqualTo(Color.Blue.ToArgb());
	}
}
