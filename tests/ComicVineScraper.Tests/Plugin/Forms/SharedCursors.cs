using System.Windows.Forms;
using static TUnit.Core.HookType;

namespace ComicVineScraper.Tests.Plugin.Forms;

/// <summary>
/// WinForms creates each Cursors member on first use with no lock. Tests run on many STA threads at once, so two threads
/// that meet a cursor first can each create one, and a panel then holds a Hand cursor with another handle than the one
/// Cursors.Hand keeps. The plugin's UI has one thread and cannot race. Creating the cursors once, before any test, keeps
/// every test on the same objects.
/// </summary>
public static class SharedCursors
{
	[Before(Assembly)]
	public static void CreateBeforeAnyTest()
	{
		_ = Cursors.Default;
		_ = Cursors.Hand;
	}
}
