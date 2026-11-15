using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace ComicApiPlugin.Matching;

/// <summary>A page as the JPEG VerseDB's cover lookup takes, which is also what the cover comparer is fed: longest side at most 1200 pixels, quality 85, under 5 MB.</summary>
public static class CoverJpeg
{
	public const int MaxSide = 1200;
	public const int Quality = 85;
	public const int MaxBytes = 5 * 1024 * 1024 - 1;

	public static byte[] Encode(Bitmap page)
	{
		double scale = Math.Min(1.0, MaxSide / (double)Math.Max(page.Width, page.Height));
		for (int attempt = 0; attempt < 6; attempt++)
		{
			byte[] bytes = EncodeScaled(page, scale);
			if (bytes.Length <= MaxBytes)
			{
				return bytes;
			}
			scale *= 0.75;
		}
		throw new InvalidOperationException("The cover cannot be made smaller than 5 MB.");
	}

	private static byte[] EncodeScaled(Bitmap page, double scale)
	{
		int width = Math.Max(1, (int)Math.Round(page.Width * scale));
		int height = Math.Max(1, (int)Math.Round(page.Height * scale));
		using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
		using (Graphics g = Graphics.FromImage(scaled))
		{
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.DrawImage(page, new Rectangle(0, 0, width, height));
		}
		ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
		using var parameters = new EncoderParameters(1);
		parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Quality);
		using var stream = new MemoryStream();
		scaled.Save(stream, jpeg, parameters);
		return stream.ToArray();
	}
}
