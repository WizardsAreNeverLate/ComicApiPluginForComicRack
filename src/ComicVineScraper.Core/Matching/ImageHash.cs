using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ComicVineScraper.Matching;

/// <summary>imagehash.py: an 8x8 average hash, used to tell whether two covers show the same image.</summary>
public static class ImageHash
{
	private const int Size = 8;

	/// <summary>
	/// Draws the image in greyscale into an 8x8 64bppArgb bitmap. Bit x*8+y is set when the red channel of pixel
	/// (x, y) is strictly above the average. Null for no image. The image is not modified or disposed.
	/// </summary>
	public static ulong? Compute(Image? image)
	{
		if (image is null)
		{
			return null;
		}
		using var attributes = new ImageAttributes();
		attributes.SetColorMatrix(new ColorMatrix(
		[
			[0.3f, 0.3f, 0.3f, 0, 0],
			[.59f, .59f, .59f, 0, 0],
			[.11f, .11f, .11f, 0, 0],
			[0, 0, 0, 1, 0],
			[0, 0, 0, 0, 1],
		]));
		using var small = new Bitmap(Size, Size, PixelFormat.Format64bppArgb);
		using (Graphics g = Graphics.FromImage(small))
		{
			g.CompositingQuality = CompositingQuality.HighQuality;
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.DrawImage(image, new Rectangle(0, 0, Size, Size), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
		}

		var pixels = new int[Size * Size];
		int sum = 0;
		for (int x = 0; x < Size; x++)
		{
			for (int y = 0; y < Size; y++)
			{
				pixels[x * Size + y] = small.GetPixel(x, y).R;
				sum += pixels[x * Size + y];
			}
		}
		double average = sum / (double)pixels.Length;
		ulong hash = 0;
		for (int i = 0; i < pixels.Length; i++)
		{
			if (pixels[i] > average)
			{
				hash |= 1UL << i;
			}
		}
		return hash;
	}

	/// <summary>1 minus the fraction of differing bits: 1.0 is identical. 0.0 when either hash is missing.</summary>
	public static double Similarity(ulong? a, ulong? b)
	{
		if (a is not { } x || b is not { } y)
		{
			return 0.0;
		}
		int distance = 0;
		for (ulong bits = x ^ y; bits != 0; bits &= bits - 1)
		{
			distance++;
		}
		return 1.0 - distance / 64.0;
	}
}
