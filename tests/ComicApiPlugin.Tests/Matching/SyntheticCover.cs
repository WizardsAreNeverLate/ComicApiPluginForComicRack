using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace ComicApiPlugin.Tests.Matching;

/// <summary>A made-up comic cover: a coloured body of shapes and captions under a title banner that every series issue shares.</summary>
internal static class SyntheticCover
{
	public const int Width = 400;
	public const int Height = 600;

	public static Bitmap Make(int seed, string banner = "AMAZING TALES")
	{
		var random = new Random(seed);
		var cover = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
		using Graphics g = Graphics.FromImage(cover);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		using (var sky = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), RandomColor(random), RandomColor(random), 90f))
		{
			g.FillRectangle(sky, 0, 0, Width, Height);
		}
		for (int i = 0; i < 14; i++)
		{
			using var brush = new SolidBrush(Color.FromArgb(200, RandomColor(random)));
			Rectangle box = new(random.Next(Width - 80), 100 + random.Next(Height - 200), 40 + random.Next(160), 40 + random.Next(200));
			if (i % 2 == 0)
			{
				g.FillEllipse(brush, box);
			}
			else
			{
				g.FillPolygon(brush, [new Point(box.Left, box.Bottom), new Point(box.Right, box.Bottom), new Point(box.Left + box.Width / 2, box.Top)]);
			}
		}
		for (int i = 0; i < 20; i++)
		{
			using var pen = new Pen(RandomColor(random), 1 + random.Next(5));
			g.DrawLine(pen, random.Next(Width), 100 + random.Next(Height - 100), random.Next(Width), 100 + random.Next(Height - 100));
		}
		using var font = new Font("Arial", 18, FontStyle.Bold);
		for (int i = 0; i < 3; i++)
		{
			string caption = string.Concat(Enumerable.Range(0, 8).Select(_ => (char)('A' + random.Next(26))));
			g.DrawString(caption, font, Brushes.White, random.Next(Width - 160), 150 + random.Next(Height - 250));
		}
		using var title = new Font("Impact", 34, FontStyle.Regular);
		g.DrawString(banner, title, Brushes.Gold, 10, 18);
		return cover;
	}

	public static Bitmap Scaled(Bitmap source, double factor)
	{
		var scaled = new Bitmap((int)(source.Width * factor), (int)(source.Height * factor));
		using Graphics g = Graphics.FromImage(scaled);
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
		return scaled;
	}

	/// <summary>Drops the given fraction of the width and height, split evenly across the edges, as a scan with different margins would.</summary>
	public static Bitmap Cropped(Bitmap source, double fraction)
	{
		int dx = (int)(source.Width * fraction / 2);
		int dy = (int)(source.Height * fraction / 2);
		return source.Clone(new Rectangle(dx, dy, source.Width - 2 * dx, source.Height - 2 * dy), source.PixelFormat);
	}

	public static byte[] Encode(Bitmap image, ImageFormat format, long jpegQuality = 85)
	{
		using var stream = new MemoryStream();
		if (format.Equals(ImageFormat.Jpeg))
		{
			ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
			using var parameters = new EncoderParameters(1);
			parameters.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
			image.Save(stream, codec, parameters);
		}
		else
		{
			image.Save(stream, format);
		}
		return stream.ToArray();
	}

	private static Color RandomColor(Random random) => Color.FromArgb(random.Next(256), random.Next(256), random.Next(256));
}
