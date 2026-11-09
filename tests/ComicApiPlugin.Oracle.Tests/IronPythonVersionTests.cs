using System.Diagnostics;
using System.Threading.Tasks;

namespace ComicApiPlugin.Oracle.Tests;

public sealed class IronPythonVersionTests
{
	[Test]
	public async Task Oracle_runs_the_IronPython_that_ComicRack_ships()
	{
		FileVersionInfo version = FileVersionInfo.GetVersionInfo(typeof(IronPython.Hosting.Python).Assembly.Location);

		await Assert.That(version.ProductVersion).StartsWith("IronPython 2.7.4 ");
	}
}
