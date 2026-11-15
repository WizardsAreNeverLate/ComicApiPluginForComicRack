using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace ComicApiPlugin.Fixtures;

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

	/// <summary>
	/// A PNG whose header claims 100000 x 100000 pixels and that holds no pixel data. OpenCV reads the header and then
	/// refuses the size with an error, where bytes that are no image at all only decode to nothing.
	/// </summary>
	public static byte[] OversizedPng()
	{
		using var png = new MemoryStream();
		png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], 0, 8);
		WriteChunk(png, "IHDR", [0, 1, 0x86, 0xA0, 0, 1, 0x86, 0xA0, 8, 2, 0, 0, 0]);
		WriteChunk(png, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
		WriteChunk(png, "IEND", []);
		return png.ToArray();
	}

	private static void WriteChunk(Stream png, string type, byte[] data)
	{
		byte[] typeAndData = [.. type.Select(c => (byte)c), .. data];
		WriteBigEndian(png, (uint)data.Length);
		png.Write(typeAndData, 0, typeAndData.Length);
		WriteBigEndian(png, Crc32(typeAndData));
	}

	private static void WriteBigEndian(Stream stream, uint value) =>
		stream.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value], 0, 4);

	private static uint Crc32(byte[] bytes)
	{
		uint crc = 0xFFFFFFFF;
		foreach (byte b in bytes)
		{
			crc ^= b;
			for (int bit = 0; bit < 8; bit++)
			{
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
			}
		}
		return ~crc;
	}

	private static Color RandomColor(Random random) => Color.FromArgb(random.Next(256), random.Next(256), random.Next(256));
}
