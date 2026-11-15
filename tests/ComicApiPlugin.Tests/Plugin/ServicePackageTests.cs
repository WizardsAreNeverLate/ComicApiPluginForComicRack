using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ComicApiPlugin.Plugin;

namespace ComicApiPlugin.Tests.Plugin;

public sealed class ServicePackageTests
{
	[Test]
	public async Task The_packaged_service_unpacks_with_its_subfolders_and_without_debug_symbols()
	{
		string root = BuiltService.NewFolder("root");

		string exe = ServicePackage.Unpack(BuiltService.Zip, root);
		string folder = Path.GetDirectoryName(exe)!;
		string[] files = [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(file => file.Substring(folder.Length + 1))];

		await Assert.That(Path.GetFileName(exe)).IsEqualTo("ComicApiPlugin.Service.exe");
		await Assert.That(Path.GetDirectoryName(folder)).IsEqualTo(root);
		await Assert.That(files).Contains("ComicApiPlugin.Service.exe");
		await Assert.That(files).Contains("ComicApiPlugin.Service.dll");
		await Assert.That(files).Contains("ComicApiPlugin.Service.runtimeconfig.json");
		await Assert.That(files).Contains("ComicApiPlugin.Service.deps.json");
		await Assert.That(files).Contains("ComicApiPlugin.Core.dll");
		await Assert.That(files.Any(file => file.Contains(Path.DirectorySeparatorChar))).IsTrue();
		await Assert.That(files.Where(file => file.EndsWith(".pdb"))).IsEmpty();
	}

	[Test]
	public async Task A_second_unpack_keeps_the_folder_and_removes_other_versions_and_leftovers()
	{
		string root = BuiltService.NewFolder("root");
		string exe = ServicePackage.Unpack(BuiltService.Zip, root);
		string marker = Path.Combine(Path.GetDirectoryName(exe)!, "written-after-the-first-unpack.txt");
		File.WriteAllText(marker, "");
		string older = Path.Combine(root, "0123456789abcdef");
		string leftover = Path.Combine(root, "fedcba9876543210.partial");
		Directory.CreateDirectory(older);
		File.WriteAllText(Path.Combine(older, "ComicApiPlugin.Service.exe"), "");
		Directory.CreateDirectory(leftover);

		string again = ServicePackage.Unpack(BuiltService.Zip, root);

		await Assert.That(again).IsEqualTo(exe);
		await Assert.That(File.Exists(marker)).IsTrue();
		await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo([Path.GetDirectoryName(exe)!]);
	}
}
