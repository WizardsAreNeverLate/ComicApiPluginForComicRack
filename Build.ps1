param(
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Debug",
	[string]$ComicRackDir = $env:COMICRACK_DIR,
	[switch]$Rebuild,
	[switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "ComicVineScraper.sln"

if (!$ComicRackDir) {
	Write-Error "Set COMICRACK_DIR, or pass -ComicRackDir, to the output folder of a ComicRack CE build of branch feat/dotnet-plugins."
	exit 1
}

# dotnet build cannot compile WinForms .resx resources here, so use Visual Studio's MSBuild
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) {
	Write-Error "vswhere.exe was not found at $vswhere. Install Visual Studio with the .NET desktop workload."
	exit 1
}
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (!$msbuild) {
	Write-Error "MSBuild was not found. Install Visual Studio with the .NET desktop workload."
	exit 1
}
Write-Output "MSBuild: $msbuild"
Write-Output "ComicRackDir: $ComicRackDir"

# Restore from nuget.org only, so a machine-wide NuGet config with unreachable sources cannot fail the build
$nugetConfig = Join-Path ([System.IO.Path]::GetTempPath()) "ComicVineScraper.nuget.config"
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<clear />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
	</packageSources>
</configuration>
"@ | Set-Content -Path $nugetConfig -Encoding UTF8

Write-Output "Restoring packages..."
& $msbuild $solution -t:Restore "-p:RestoreConfigFile=$nugetConfig" -nologo -v:minimal
if ($LASTEXITCODE -ne 0) {
	Write-Error "Package restore failed."
	exit 1
}

$target = if ($Rebuild) { "Rebuild" } else { "Build" }
Write-Output "Building ($Configuration, $target)..."
& $msbuild $solution "-t:$target" "-p:Configuration=$Configuration" "-p:ComicRackDir=$ComicRackDir" -m -nologo -v:minimal "-clp:ErrorsOnly;Summary"
if ($LASTEXITCODE -ne 0) {
	Write-Error "Build failed."
	exit 1
}

if ($SkipTests) {
	exit 0
}

$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = "1"
$failed = @()
foreach ($name in "ComicVineScraper.Tests", "ComicVineScraper.Oracle.Tests") {
	$exe = Join-Path $PSScriptRoot "tests\$name\bin\$Configuration\net48\$name.exe"
	if (!(Test-Path $exe)) {
		Write-Error "The build reported success, but $exe does not exist."
		exit 1
	}
	Write-Output "Running $name..."
	& $exe --progress off
	if ($LASTEXITCODE -ne 0) {
		$failed += $name
	}
}
if ($failed) {
	Write-Error "Tests failed: $($failed -join ', ')"
	exit 1
}
Write-Output "All test suites passed."
