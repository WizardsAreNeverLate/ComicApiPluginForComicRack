<#
.SYNOPSIS
Runs the in-app smoke test: ComicRack drives the .NET plugin over recorded Comic Vine responses.

.DESCRIPTION
Run Build.ps1 first, so the packages are restored. This script then builds the plugin with -p:CvsRecordedResponses=true (into
src\ComicApiPlugin\bin\smoke, never the shipping output) and the probe (tools\smoke\CvsNetSmoke.cs), installs both under
ComicRack's Scripts folder, starts ComicRack with -ac CvsNetSmoke, and waits for the probe to close it. It never touches the
real %APPDATA%\Comic Vine Scraper folder: the plugin keeps its settings in the run folder.

The switch build starts a fake Comic Vine on a loopback port, which answers from the run's copy of tests\fixtures\comicvine, and
starts the packaged local API service with that fake as its Comic Vine and its VerseDB. The service is unpacked into the run
folder and writes its log there, so the run uses neither the network nor %LOCALAPPDATA%\ComicApiPlugin.

Everything lands in tools\smoke\runs\<stamp>\out\report.txt. The probe writes the "forms share one log" assertion. This script
adds "user settings unchanged", the process checks, and the checks of the service's log. The exit code is 0 only when every assertion passed.

.PARAMETER Bin
The output folder of a ComicRack CE build of WizardsAreNeverLate/ComicRackCE branch integration. Defaults to COMICRACK_DIR.
#>
param(
	[string]$Bin = $env:COMICRACK_DIR
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $here "..\..")).Path
if (!$Bin) { throw "Set COMICRACK_DIR, or pass -Bin, to the output folder of a ComicRack CE build of WizardsAreNeverLate/ComicRackCE branch integration." }
if (Get-Process ComicRack -ErrorAction SilentlyContinue) { Write-Output "BLOCKED: a ComicRack process is already running"; exit 2 }

$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
foreach ($project in "$Repo\src\ComicApiPlugin\ComicApiPlugin.csproj", "$here\CvsNetSmoke.csproj") {
	# The switch only matters to the plugin; the probe ignores it.
	& $msbuild $project -t:Rebuild -p:Configuration=Release -p:CvsRecordedResponses=true "-p:ComicRackDir=$Bin" -nologo -v:minimal "-clp:ErrorsOnly;Summary" | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}
$crplugin = "$Repo\src\ComicApiPlugin\bin\smoke\Release\net48\ComicApiPlugin.crplugin"
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

$plugin = Join-Path $Bin "Scripts\ComicApiPluginNet"
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
# Each call the service made to a provider is one line: "Comic Vine GET <host and path> responded <status> ...".
$serviceLog = @(Get-ChildItem (Join-Path $run "service-logs") -Filter "service-*.log" -ErrorAction SilentlyContinue | Get-Content)
$calls = @($serviceLog | ForEach-Object { if ($_ -match '\] (Comic Vine|VerseDB) [A-Z]+ (\S+) (responded|failed)') { $Matches[2] } })
$remote = @($calls | Where-Object { $_ -notlike '127.0.0.1:*' })
Assert-Smoke "the service answered from recorded responses" (@($calls | Where-Object { $_ -like '127.0.0.1:*/api/*' }).Count -gt 0) "$($calls.Count) provider calls in the service's log"
Assert-Smoke "no request left the machine" ($remote.Count -eq 0) "$($remote.Count) provider calls to a host other than 127.0.0.1: $(($remote | Select-Object -Unique) -join ', ')"
$serviceLeft = @(Get-Process ComicApiPlugin.Service -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$run\*" })
Assert-Smoke "no service process left" ($serviceLeft.Count -eq 0) "no ComicApiPlugin.Service.exe from the run folder is running"
# The unpacked service is about 72 MB and is the build's own output, so a kept run does not need it.
Remove-Item -Recurse -Force (Join-Path $run "service") -ErrorAction SilentlyContinue
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
