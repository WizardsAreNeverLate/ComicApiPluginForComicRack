using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using ComicVineScraper.Engine;

namespace ComicVineScraper.Plugin;

/// <summary>Where a click on the page goes: the first page, the previous one, the next one, or the last.</summary>
internal enum PageZone
{
	First,
	Previous,
	Next,
	Last,
}

/// <summary>
/// comicform._PictureBoxPanel: a book's page, as large as fits with its aspect ratio kept. While the mouse is over it,
/// arrows show where a click goes; the outer columns go to the first and last page, the inner ones turn one page.
/// </summary>
internal sealed class BookPagePanel : Panel
{
	private const double FallbackRatio = 1.55;

	private readonly PictureBox picture = new() { Name = "page", SizeMode = PictureBoxSizeMode.StretchImage, Location = Point.Empty, Enabled = false };
	private readonly Bitmap logo = Images.Logo();
	private readonly Bitmap first = Images.Arrow(left: true, full: true);
	private readonly Bitmap previous = Images.Arrow(left: true, full: false);
	private readonly Bitmap next = Images.Arrow(left: false, full: false);
	private readonly Bitmap last = Images.Arrow(left: false, full: true);
	private readonly ImageAttributes dimmed = new();
	private readonly ImageAttributes plain = new();
	private IScrapeBook? book;
	private int page;
	private int pageCount;
	private double ratio = FallbackRatio;
	private PageZone? hovered;

	public BookPagePanel()
	{
		dimmed.SetRemapTable([new ColorMap { OldColor = Color.White, NewColor = Color.Gainsboro }]);
		picture.Paint += (_, e) => PaintArrows(e.Graphics);
		Controls.Add(picture);
		SetImage(null);
	}

	public void ShowBook(IScrapeBook shown, int pages)
	{
		book = shown;
		pageCount = pages;
		page = 0;
		SetImage(shown.GetPage(0));
	}

	protected override void OnResize(EventArgs eventargs)
	{
		base.OnResize(eventargs);
		FitPicture();
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		PageZone zone = ZoneAt(e.X);
		if (hovered != zone)
		{
			hovered = zone;
			picture.Invalidate();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		if (hovered is not null)
		{
			hovered = null;
			picture.Invalidate();
		}
	}

	/// <summary>Each release of the left button over the page turns it, so a double click turns two pages, as in Python.</summary>
	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (ClientRectangle.Contains(e.Location))
		{
			TurnPage(e);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Image? shown = picture.Image;
			picture.Image = null;
			if (!ReferenceEquals(shown, logo))
			{
				shown?.Dispose();
			}
			foreach (IDisposable owned in new IDisposable[] { logo, first, previous, next, last, dimmed, plain })
			{
				owned.Dispose();
			}
		}
		base.Dispose(disposing);
	}

	private bool CanGo(bool forward) => forward ? page < pageCount - 1 : page > 0;

	private void TurnPage(MouseEventArgs e)
	{
		if (book is null || e.Button != MouseButtons.Left || hovered is not { } zone || pageCount <= 0)
		{
			return;
		}
		int target = zone switch
		{
			PageZone.First => 0,
			PageZone.Previous => page - 1,
			PageZone.Next => page + 1,
			_ => pageCount - 1,
		};
		target = Math.Min(pageCount - 1, Math.Max(0, target));
		if (target != page)
		{
			page = target;
			SetImage(book.GetPage(page));
		}
	}

	/// <summary>Takes ownership of image; null shows the logo.</summary>
	private void SetImage(Bitmap? image)
	{
		Image? old = picture.Image;
		Image shown = image ?? logo;
		ratio = shown.Height > 0 && shown.Width > 0 ? shown.Width / (double)shown.Height : FallbackRatio;
		picture.Image = shown;
		if (old is not null && !ReferenceEquals(old, logo) && !ReferenceEquals(old, shown))
		{
			old.Dispose();
		}
		FitPicture();
		Cursor = CanGo(forward: true) || CanGo(forward: false) ? Cursors.Hand : Cursors.Default;
		picture.Invalidate();
	}

	private void FitPicture()
	{
		int widthLimited = (int)(Width / ratio);
		if (widthLimited <= Height)
		{
			picture.Size = new Size(Width, widthLimited);
			picture.Location = new Point(0, (Height - widthLimited) / 2);
		}
		else
		{
			int heightLimited = (int)(Height * ratio);
			picture.Size = new Size(heightLimited, Height);
			picture.Location = new Point((Width - heightLimited) / 2, 0);
		}
	}

	/// <summary>The arrow images are square and shrink with the page: 15% of its width at most, 1% in from each side.</summary>
	private (float Size, float Margin) ArrowLayout()
	{
		float size = Math.Min(picture.Width * 0.15f, next.Width);
		return (size, picture.Width * 0.01f);
	}

	private PageZone ZoneAt(int x)
	{
		(float size, float margin) = ArrowLayout();
		float onPicture = x - (Width - picture.Width) / 2f;
		return x < Width / 2
			? onPicture < margin + size ? PageZone.First : PageZone.Previous
			: onPicture > picture.Width - margin - size ? PageZone.Last : PageZone.Next;
	}

	private void PaintArrows(Graphics g)
	{
		if (hovered is not { } zone)
		{
			return;
		}
		(float size, float margin) = ArrowLayout();
		float y = picture.Height / 2f - size / 2f;
		float rightmost = picture.Width - margin - size;
		if (CanGo(forward: false))
		{
			Draw(g, first, margin, y, size, zone == PageZone.First);
			Draw(g, previous, margin + size, y, size, zone == PageZone.Previous);
		}
		if (CanGo(forward: true))
		{
			Draw(g, next, rightmost - size, y, size, zone == PageZone.Next);
			Draw(g, last, rightmost, y, size, zone == PageZone.Last);
		}
	}

	private void Draw(Graphics g, Bitmap arrow, float x, float y, float size, bool active) =>
		g.DrawImage(arrow, Rectangle.Round(new RectangleF(x, y, size, size)), 0, 0, arrow.Width, arrow.Height, GraphicsUnit.Pixel, active ? plain : dimmed);
}
