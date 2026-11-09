using System.Linq;
using System.Threading.Tasks;

namespace ComicApiPlugin.Tests.Architecture;

public sealed class CoreBoundaryTests
{
	[Test]
	public async Task Core_references_neither_ComicRack_nor_WinForms()
	{
		string[] references = System.Reflection.Assembly.Load("ComicApiPlugin.Core").GetReferencedAssemblies().Select(a => a.Name).ToArray();

		await Assert.That(references).DoesNotContain(name => name.StartsWith("ComicRack") || name.StartsWith("cYo.") || name == "System.Windows.Forms");
	}
}
