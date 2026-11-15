using System;
using System.Diagnostics;
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
	public async Task A_second_unpack_keeps_the_folder_and_removes_other_versions_and_partial_folders_older_than_ten_minutes()
	{
		string root = BuiltService.NewFolder("root");
		string exe = ServicePackage.Unpack(BuiltService.Zip, root);
		string written = Path.Combine(Path.GetDirectoryName(exe)!, "written-after-the-first-unpack.txt");
		File.WriteAllText(written, "");
		string older = Path.Combine(root, "0123456789abcdef");
		string leftover = Path.Combine(root, "fedcba9876543210.0a1b.partial");
		string extracting = Path.Combine(root, "fedcba9876543210.2c3d.partial");
		Directory.CreateDirectory(older);
		File.WriteAllText(Path.Combine(older, "ComicApiPlugin.Service.exe"), "");
		Directory.CreateDirectory(leftover);
		Directory.SetCreationTimeUtc(leftover, DateTime.UtcNow.AddMinutes(-11));
		Directory.CreateDirectory(extracting);

		string again = ServicePackage.Unpack(BuiltService.Zip, root);

		await Assert.That(again).IsEqualTo(exe);
		await Assert.That(File.Exists(written)).IsTrue();
		await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo([Path.GetDirectoryName(exe)!, extracting]);
	}

	[Test]
	public async Task A_folder_that_an_interrupted_unpack_left_under_the_final_name_is_unpacked_again()
	{
		string name = Path.GetFileName(Path.GetDirectoryName(ServicePackage.Unpack(BuiltService.Zip, BuiltService.NewFolder("root")))!);
		string root = BuiltService.NewFolder("root");
		string interrupted = Path.Combine(root, name);
		Directory.CreateDirectory(interrupted);
		File.WriteAllText(Path.Combine(interrupted, "ComicApiPlugin.Service.exe"), "");

		string exe = ServicePackage.Unpack(BuiltService.Zip, root);

		await Assert.That(exe).IsEqualTo(Path.Combine(interrupted, "ComicApiPlugin.Service.exe"));
		await Assert.That(new FileInfo(exe).Length).IsGreaterThan(0);
		await Assert.That(File.Exists(Path.Combine(interrupted, "ComicApiPlugin.Service.deps.json"))).IsTrue();
	}

	[Test]
	public async Task Another_versions_folder_whose_service_is_running_keeps_every_file()
	{
		string root = BuiltService.NewFolder("root");
		string running = Path.Combine(root, "0123456789abcdef");
		Directory.CreateDirectory(running);
		string exe = Path.Combine(running, "ComicApiPlugin.Service.exe");
		File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exe);
		File.WriteAllText(Path.Combine(running, "ComicApiPlugin.Service.runtimeconfig.json"), "{}");
		// /k reads commands from stdin, a pipe this test never writes, and starts no child process that could outlive the kill.
		using Process service = Process.Start(new ProcessStartInfo(exe, "/k") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })!;
		try
		{
			ServicePackage.Unpack(BuiltService.Zip, root);

			await Assert.That(File.Exists(Path.Combine(running, "ComicApiPlugin.Service.runtimeconfig.json"))).IsTrue();
			await Assert.That(File.Exists(exe)).IsTrue();
		}
		finally
		{
			service.Kill();
			service.WaitForExit();
		}
	}
}
