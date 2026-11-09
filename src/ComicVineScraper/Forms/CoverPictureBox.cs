using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ComicVineScraper.ComicVine;

namespace ComicVineScraper.Plugin;

/// <summary>
/// dbpicturebox.DBPictureBox: shows the image at a URL, downloaded off the UI thread, with the logo while it loads and
/// when there is none. Images are kept for the life of the control, so going back to a cover does not download it again.
/// Nothing is downloaded while the control is hidden.
/// </summary>
internal sealed class CoverPictureBox : PictureBox
{
	private readonly IComicVine comicVine;
	private readonly LatestLoader loader;
	private readonly Dictionary<Uri, Bitmap> cache = [];
	private readonly Bitmap unknown = Images.Logo();
	private readonly Bitmap loading = Images.Loading();
	private Uri? wanted;

	public CoverPictureBox(IComicVine comicVine, PluginContext ctx)
	{
		this.comicVine = comicVine;
		loader = new LatestLoader(ctx.Log);
		SizeMode = PictureBoxSizeMode.StretchImage;
		Image = unknown;
	}

	/// <summary>Shows the cover at url, or the logo for null.</summary>
	public void ShowCover(Uri? url)
	{
		wanted = url;
		if (Visible)
		{
			ShowWanted();
		}
	}

	protected override void OnVisibleChanged(EventArgs e)
	{
		base.OnVisibleChanged(e);
		if (Visible)
		{
			ShowWanted();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			loader.Cancel();
			Image = null;
			foreach (Bitmap image in cache.Values)
			{
				image.Dispose();
			}
			cache.Clear();
			unknown.Dispose();
			loading.Dispose();
		}
		base.Dispose(disposing);
	}

	private void ShowWanted()
	{
		if (wanted is not { } url)
		{
			loader.Cancel();
			Image = unknown;
		}
		else if (cache.TryGetValue(url, out Bitmap? cached))
		{
			loader.Cancel();
			Image = cached;
		}
		else
		{
			Image = loading;
			_ = loader.Load(ct => comicVine.GetImageAsync(url, ct), image =>
			{
				if (IsDisposed)
				{
					image?.Dispose();
					return;
				}
				if (image is not null)
				{
					cache[url] = image;
				}
				if (url == wanted)
				{
					Image = image ?? unknown;
				}
			});
		}
	}
}
