<#
.SYNOPSIS
Runs the in-app smoke test: ComicRack drives the .NET plugin over recorded Comic Vine responses.

.DESCRIPTION
Run Build.ps1 first, so the packages are restored. This script then builds the plugin with -p:CvsRecordedResponses=true (into
src\ComicVineScraper\bin\smoke, never the shipping output) and the probe (tools\smoke\CvsNetSmoke.cs), installs both under
ComicRack's Scripts folder, starts ComicRack with -ac CvsNetSmoke, and waits for the probe to close it. It never touches the
real %APPDATA%\Comic Vine Scraper folder: the plugin keeps its settings in the run folder.

Everything lands in tools\smoke\runs\<stamp>\out\report.txt. The probe writes the "forms share one log" assertion. This script
adds "user settings unchanged" and the process checks. The exit code is 0 only when every assertion passed.

.PARAMETER Bin
The output folder of a ComicRack CE build of branch feat/dotnet-plugins. Defaults to COMICRACK_DIR.
#>
param(
	[string]$Bin = $env:COMICRACK_DIR
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $here "..\..")).Path
if (!$Bin) { throw "Set COMICRACK_DIR, or pass -Bin, to the output folder of a ComicRack CE build of branch feat/dotnet-plugins." }
if (Get-Process ComicRack -ErrorAction SilentlyContinue) { Write-Output "BLOCKED: a ComicRack process is already running"; exit 2 }

$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
foreach ($project in "$Repo\src\ComicVineScraper\ComicVineScraper.csproj", "$here\CvsNetSmoke.csproj") {
	# The switch only matters to the plugin; the probe ignores it.
	& $msbuild $project -t:Rebuild -p:Configuration=Release -p:CvsRecordedResponses=true "-p:ComicRackDir=$Bin" -nologo -v:minimal "-clp:ErrorsOnly;Summary" | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}
$crplugin = "$Repo\src\ComicVineScraper\bin\smoke\Release\net48\ComicVineScraper.crplugin"
$probeDll = "$here\bin\Release\net48\CvsNetSmoke.dll"
foreach ($built in $crplugin, $probeDll) {
	if (!(Test-Path $built)) { throw "the build reported success, but $built does not exist" }
}

# Only file names, sizes and times are listed, never the contents of the user's real settings.
function Get-UserSettings {
	$folder = Join-Path $env:APPDATA "Comic Vine Scraper"
	if (Test-Path $folder) { Get-ChildItem $folder -File | ForEach-Object { "$($_.Name) $($_.Length) $($_.LastWriteTimeUtc.Ticks)" } } else { @("(absent)") }
}
$before = @(Get-UserSettings)

$run = Join-Path $here ("runs\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$out = Join-Path $run "out"
New-Item -ItemType Directory -Force (Join-Path $run "fixtures"), (Join-Path $run "settings"), $out | Out-Null
Copy-Item "$Repo\tests\fixtures\comicvine\*.txt" (Join-Path $run "fixtures")
$report = Join-Path $out "report.txt"

$plugin = Join-Path $Bin "Scripts\ComicVineScraperNet"
$probe = Join-Path $Bin "Scripts\CvsNetSmoke"
if ((Test-Path $plugin) -or (Test-Path $probe)) { throw "an earlier install is still in Scripts; remove it first" }
New-Item -ItemType Directory -Force $plugin, $probe | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($crplugin, $plugin)
Copy-Item $probeDll $probe

$failures = @()
function Assert-Smoke([string]$name, [bool]$ok, [string]$detail) {
	$line = "ASSERT $(if ($ok) { 'PASS' } else { 'FAIL' }) ${name}: $detail"
	Add-Content -Path $report -Value $line
	if (-not $ok) { $script:failures += $name }
}

$env:CVS_NET_RECORDED_RESPONSES = $run
$env:CVS_NET_SMOKE_OUT = $out
try {
	# Start-Process -PassThru gives this run's own process. It is the only one waited on and the only one ever killed.
	$p = Start-Process (Join-Path $Bin "ComicRack.exe") -ArgumentList "-ac", "CvsNetSmoke" -PassThru
	Remove-Item Env:CVS_NET_RECORDED_RESPONSES, Env:CVS_NET_SMOKE_OUT
	$closedItself = $p.WaitForExit(240000)
	if (-not $closedItself) { $p.Kill(); $p.WaitForExit() }
	Add-Content -Path $report -Value "ComicRack pid $($p.Id) exited with $($p.ExitCode)"
	Assert-Smoke "ComicRack closed itself" $closedItself "the probe calls MainForm.MenuClose; the script had to kill pid $($p.Id) only if this fails"
}
finally {
	Remove-Item Env:CVS_NET_RECORDED_RESPONSES, Env:CVS_NET_SMOKE_OUT -ErrorAction SilentlyContinue
	Remove-Item -Recurse -Force $plugin, $probe -ErrorAction SilentlyContinue
	foreach ($root in $env:APPDATA, $env:LOCALAPPDATA) {
		Remove-Item -Recurse -Force (Join-Path $root "cYo\ComicRack Community Edition\Configurations\CvsNetSmoke") -ErrorAction SilentlyContinue
	}
}

$text = if (Test-Path $report) { Get-Content $report -Raw } else { "" }
Assert-Smoke "probe ran without an exception" ($text -notmatch "(?m)^EXCEPTION") "no EXCEPTION line in the report"
Assert-Smoke "probe asserted the shared log" ($text -match "(?m)^ASSERT (PASS|FAIL) forms share one log:") "the report has the probe's 'forms share one log' line"
$probeFailures = [regex]::Matches($text, "(?m)^ASSERT FAIL (.+?):") | ForEach-Object { $_.Groups[1].Value }
foreach ($name in $probeFailures) { $failures += $name }

$after = @(Get-UserSettings)
$unchanged = $null -eq (Compare-Object $before $after)
Assert-Smoke "user settings unchanged" $unchanged "$($before.Count) entries before, $($after.Count) after, in %APPDATA%\Comic Vine Scraper"
$left = (Test-Path $plugin) -or (Test-Path $probe) -or (Test-Path (Join-Path $env:APPDATA "cYo\ComicRack Community Edition\Configurations\CvsNetSmoke")) -or (Test-Path (Join-Path $env:LOCALAPPDATA "cYo\ComicRack Community Edition\Configurations\CvsNetSmoke"))
Assert-Smoke "nothing left behind" (-not $left) "the Scripts folders and the probe's configuration are removed"

Get-Content $report
Write-Output "--- unrecorded requests"
Get-Content (Join-Path $run "unrecorded.txt") -ErrorAction SilentlyContinue
Write-Output "--- settings folder"
Get-ChildItem (Join-Path $run "settings") -Name
Write-Output "--- report: $report"
if ($failures) {
	Write-Output ("SMOKE FAILED: " + (($failures | Select-Object -Unique) -join "; "))
	exit 1
}
Write-Output "SMOKE PASSED"
