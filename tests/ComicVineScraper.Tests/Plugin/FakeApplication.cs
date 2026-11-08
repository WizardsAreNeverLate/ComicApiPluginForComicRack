using System;
using System.Collections.Generic;
using System.Drawing;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Plugins.Automation;

namespace ComicVineScraper.Tests.Plugin;

/// <summary>Records SetCustomBookThumbnail calls. Every other member is outside what ComicRackBook may use.</summary>
internal sealed class FakeApplication : IApplication
{
	public bool Accepts { get; init; } = true;

	public List<(ComicBook Book, Bitmap Image)> Thumbnails { get; } = [];

	public string? SeriesWhenThumbnailSet { get; private set; }

	/// <summary>What GetComicPage returns or throws. Unset, it throws, as ComicRack can for a book whose page count was cleared.</summary>
	public Func<ComicBook, int, Bitmap?> Pages { get; init; } = (_, _) => throw new InvalidOperationException("no pages");

	public Bitmap GetComicPage(ComicBook cb, int page) => Pages(cb, page)!;

	public bool SetCustomBookThumbnail(ComicBook cb, Bitmap bitmap)
	{
		Thumbnails.Add((cb, bitmap));
		SeriesWhenThumbnailSet = cb.Series;
		return Accepts;
	}

	public string ProductVersion => throw new NotSupportedException();
	public void Restart() => throw new NotSupportedException();
	public void SynchronizeDevices() => throw new NotSupportedException();
	public void ScanFolders() => throw new NotSupportedException();
	public IEnumerable<ComicBook> ReadDatabaseBooks(string file) => throw new NotSupportedException();
	public IEnumerable<ComicBook> GetLibraryBooks() => throw new NotSupportedException();
	public ComicBook AddNewBook(bool showDialog) => throw new NotSupportedException();
	public bool RemoveBook(ComicBook cb) => throw new NotSupportedException();
	public ComicBook GetBook(string file) => throw new NotSupportedException();
	public Bitmap GetComicThumbnail(ComicBook cb, int page) => throw new NotSupportedException();
	public IDictionary<string, string> GetComicFields() => throw new NotSupportedException();
	public Bitmap GetComicPublisherIcon(ComicBook cb) => throw new NotSupportedException();
	public Bitmap GetComicImprintIcon(ComicBook cb) => throw new NotSupportedException();
	public Bitmap GetComicAgeRatingIcon(ComicBook cb) => throw new NotSupportedException();
	public Bitmap GetComicFormatIcon(ComicBook cb) => throw new NotSupportedException();
	public string ReadInternet(string url) => throw new NotSupportedException();
	public int AskQuestion(string question, string buttonText, string optionText) => throw new NotSupportedException();
	public void ShowComicInfo(IEnumerable<ComicBook> books) => throw new NotSupportedException();
}
