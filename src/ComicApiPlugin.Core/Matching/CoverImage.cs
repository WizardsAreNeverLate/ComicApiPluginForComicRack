using System.Drawing;

namespace ComicApiPlugin.Matching;

/// <summary>utils.strip_back_cover: a scan of the back and front covers side by side becomes the front cover only.</summary>
public static class CoverImage
{
	/// <summary>
	/// When 1.2 &lt; width/height &lt; 1.5, returns a new bitmap of the right half (Width/2 pixels wide). Otherwise returns
	/// the original unchanged. The argument is never disposed: the caller owns it, and owns the result when it is a new bitmap.
	/// </summary>
	/// <remarks>
	/// Python drew the whole image at (-Width/2, 0) with DrawImage(image, x, y), which scales by the image's DPI, so a
	/// scan that is not 96 DPI gave a wrongly scaled half (deviations.md #17). This copies the right half pixel for pixel.
	/// </remarks>
	public static Bitmap StripBackCover(Bitmap image)
	{
		double ratio = image.Width / (double)image.Height;
		if (ratio is >= 1.5 or <= 1.2)
		{
			return image;
		}
		int half = image.Width / 2;
		var front = new Bitmap(half, image.Height);
		try
		{
			using Graphics g = Graphics.FromImage(front);
			g.DrawImage(image, new Rectangle(0, 0, half, image.Height), new Rectangle(image.Width - half, 0, half, image.Height), GraphicsUnit.Pixel);
			return front;
		}
		catch
		{
			front.Dispose();
			throw;
		}
	}
}
