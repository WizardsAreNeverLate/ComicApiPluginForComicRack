using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ComicApiPlugin.Plugin;

/// <summary>The plugin's own pictures, embedded in the assembly. Each call returns a new bitmap that the caller disposes.</summary>
internal static class Images
{
	/// <summary>The placeholder shown when there is no cover.</summary>
	public static Bitmap Logo() => Load("comicvinelogo.png");

	/// <summary>The logo at 30% opacity, shown while a cover loads.</summary>
	public static Bitmap Loading()
	{
		using Bitmap logo = Logo();
		var faded = new Bitmap(logo.Width, logo.Height);
		using Graphics g = Graphics.FromImage(faded);
		using var attributes = new ImageAttributes();
		attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.3f });
		g.DrawImage(logo, new Rectangle(0, 0, logo.Width, logo.Height), 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attributes);
		return faded;
	}

	/// <param name="full">The arrow to the first or last page, rather than the previous or next one.</param>
	public static Bitmap Arrow(bool left, bool full) => Load((full ? "full" : "") + (left ? "left" : "right") + "arrow.png");

	private static Bitmap Load(string name)
	{
		using Stream stream = typeof(Images).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException("The plugin assembly has no embedded " + name + ".");
		using var image = new Bitmap(stream);
		// A copy, because GDI+ reads a stream-backed bitmap lazily and the stream closes here.
		return new Bitmap(image);
	}
}
