<#
.SYNOPSIS
Runs the live differential test: the original Python plugin and the C# plugin scrape identical books in ComicRack, and every field each one wrote is compared.

.DESCRIPTION
Run Build.ps1 first, so the packages are restored. This script then:
  - builds the plugin twice (a normal Release build, and a switch build with -p:CvsRecordedResponses=true into bin\smoke), and the probe;
  - proves the normal build holds no code that reads CVS_NET_LIVE_FOLDER or CVS_NET_RECORDED_RESPONSES;
  - installs a copy of the original Python plugin (from -PythonSource), the switch build of the C# plugin and the probe under ComicRack's Scripts folder;
  - writes two settings folders with identical settings.dat and advanced.dat, each holding the Comic Vine API key;
  - starts ComicRack with -ac CvsDiff, waits for the probe to close it, and writes runs\<stamp>\out\report.txt.

The installed Python copy is the only change to the original: its settings directory (resources.py, the profile_dir statement) points at the run folder.
Neither plugin reads or writes the real %APPDATA%\Comic Vine Scraper, and the script asserts that. The key is read from -KeyFile, written only to the two
settings.dat files, and those folders are deleted before the script ends. Nothing prints the key.

The exit code is 0 only when every safety assertion passed. Differences between the plugins are not failures here: they are classified in docs/port/architecture.md.

.PARAMETER Bin
The output folder of a ComicRack CE build of branch feat/dotnet-plugins. Defaults to COMICRACK_DIR.
.PARAMETER PythonSource
The original plugin's src folder.
.PARAMETER KeyFile
A file that contains the Comic Vine API key (40 hex characters).
#>
param(
	[string]$Bin = $env:COMICRACK_DIR,
	[string]$PythonSource = "D:\Data\_Code\comic-vine-scraper\src",
	[string]$KeyFile = "c:\data\ai\ComicRackCe\comic-vine.md",
	[int]$TimeoutMinutes = 25
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $here "..\..")).Path
if (!$Bin) { throw "Set COMICRACK_DIR, or pass -Bin, to the output folder of a ComicRack CE build of branch feat/dotnet-plugins." }
if (Get-Process ComicRack -ErrorAction SilentlyContinue) { Write-Output "BLOCKED: a ComicRack process is already running"; exit 2 }

$keyMatch = [regex]::Match((Get-Content $KeyFile -Raw), '(?<![0-9a-fA-F])[0-9a-f]{40}(?![0-9a-fA-F])')
if (!$keyMatch.Success) { throw "no 40-hex API key found in $KeyFile" }
$apiKey = $keyMatch.Value

$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
function Invoke-Build([string]$project, [string[]]$extra) {
	& $msbuild $project -restore -t:Rebuild -p:Configuration=Release @extra "-p:ComicRackDir=$Bin" -nologo -v:minimal "-clp:ErrorsOnly;Summary" | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}
$pluginProject = "$Repo\src\ComicVineScraper\ComicVineScraper.csproj"
Invoke-Build $pluginProject @()
Invoke-Build $pluginProject @("-p:CvsRecordedResponses=true")
Invoke-Build "$here\CvsDiffProbe.csproj" @()
$normalDll = "$Repo\src\ComicVineScraper\bin\Release\net48\ComicVineScraper.dll"
$crplugin = "$Repo\src\ComicVineScraper\bin\smoke\Release\net48\ComicVineScraper.crplugin"
$switchDll = "$Repo\src\ComicVineScraper\bin\smoke\Release\net48\ComicVineScraper.dll"
$probeDll = "$here\bin\Release\net48\CvsDiffProbe.dll"
foreach ($built in $normalDll, $switchDll, $crplugin, $probeDll) {
	if (!(Test-Path $built)) { throw "the build reported success, but $built does not exist" }
}

function Test-DllContains([string]$path, [string]$text) {
	$bytes = [System.IO.File]::ReadAllBytes($path)
	foreach ($encoding in [System.Text.Encoding]::ASCII, [System.Text.Encoding]::Unicode) {
		$needle = $encoding.GetBytes($text)
		for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++) {
			if ($bytes[$i] -ne $needle[0]) { continue }
			$j = 1
			while ($j -lt $needle.Length -and $bytes[$i + $j] -eq $needle[$j]) { $j++ }
			if ($j -eq $needle.Length) { return $true }
		}
	}
	return $false
}

# Only file names, sizes and times are listed, never the contents of the user's real settings.
function Get-UserSettings {
	$folder = Join-Path $env:APPDATA "Comic Vine Scraper"
	if (Test-Path $folder) { Get-ChildItem $folder -File -Recurse | ForEach-Object { "$($_.FullName) $($_.Length) $($_.LastWriteTimeUtc.Ticks)" } } else { @("(absent)") }
}
$before = @(Get-UserSettings)

$run = Join-Path $here ("runs\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$out = Join-Path $run "out"
$settingsPy = Join-Path $run "settings-py"
$settingsNet = Join-Path $run "settings-net"
New-Item -ItemType Directory -Force $settingsPy, $settingsNet, $out | Out-Null
$report = Join-Path $out "report.txt"

$settings = @(":: This file was generated for the differential test", "",
	"apiKey : $apiKey", "autochooseSeries : True", "confirmIssue : False", "summaryDialog : True")
$advanced = "WELCOME_DIALOG = false"
foreach ($folder in $settingsPy, $settingsNet) {
	[System.IO.File]::WriteAllText((Join-Path $folder "settings.dat"), ($settings -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
	[System.IO.File]::WriteAllText((Join-Path $folder "advanced.dat"), $advanced, (New-Object System.Text.UTF8Encoding($false)))
}

$inputs = Join-Path $run "inputs"
$covers = Join-Path $run "covers"
New-Item -ItemType Directory -Force $inputs, $covers | Out-Null

# Comic Vine lookups for the book table: each volume id is checked once, and a cover book's issue cover becomes page 1 of a .cbz in the run's covers folder.
# An error from Invoke-RestMethod can quote the request URL, which carries the key, so it is rethrown without it.
$apiHeaders = @{ 'User-Agent' = 'cvs-net-differential-test' }
function Invoke-ComicVine([string]$resource, [string]$query) {
	Start-Sleep -Milliseconds 1200
	try { Invoke-RestMethod -Uri "https://comicvine.gamespot.com/api/$resource/?api_key=$apiKey&format=json&$query" -Headers $apiHeaders }
	catch { throw ("Comic Vine lookup of $resource failed: " + $_.Exception.Message.Replace($apiKey, "<key>")) }
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
$lookupLines = New-Object System.Collections.Generic.List[string]
$lookupFailures = 0
$bookRows = @()
$index = 0
foreach ($b in (Import-Csv (Join-Path $here "books.tsv") -Delimiter "`t")) {
	$file = ""
	if ($b.VolumeId) {
		$volume = (Invoke-ComicVine "volume/4050-$($b.VolumeId)" "field_list=id,name,start_year,count_of_issues,publisher").results
		$ok = ($volume.name -eq $b.ExpectName) -and ([string]$volume.start_year -eq $b.ExpectYear)
		if (-not $ok) { $lookupFailures++ }
		$lookupLines.Add("VOLUME $($b.VolumeId) '$($volume.name)' $($volume.start_year), $($volume.count_of_issues) issues, $($volume.publisher.name): $(if ($ok) { 'matches the table' } else { 'DOES NOT MATCH the table' })")
	}
	if ($b.Cover -eq "y") {
		$issue = (Invoke-ComicVine "issues" "filter=volume:$($b.VolumeId),issue_number:$($b.Number)&field_list=id,issue_number,image").results | Where-Object { $_.issue_number -eq $b.Number } | Select-Object -First 1
		if (-not $issue) { $lookupFailures++; $lookupLines.Add("COVER $($b.Series) #$($b.Number): no such issue in volume $($b.VolumeId)"); $bookRows += $b; $index++; continue }
		$jpg = Join-Path $covers "cover-$index.jpg"
		Invoke-WebRequest -Uri $issue.image.medium_url -Headers $apiHeaders -OutFile $jpg
		$name = if ($b.FromName -eq "y") { "{0} {1:000} ({2}).cbz" -f $b.Series, [int]$b.Number, $b.Year } else { "cover-$index.cbz" }
		$file = Join-Path $covers $name
		$zip = [System.IO.Compression.ZipFile]::Open($file, 'Create')
		[void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $jpg, "001.jpg")
		$zip.Dispose()
		Remove-Item $jpg
		$lookupLines.Add("COVER $($b.Series) #$($b.Number): issue $($issue.id), cover packed as $name ($((Get-Item $file).Length) bytes)")
	}
	$b | Add-Member -NotePropertyName File -NotePropertyValue $file -Force
	$bookRows += $b
	$index++
}
$bookRows | Export-Csv (Join-Path $inputs "books.tsv") -Delimiter "`t" -NoTypeInformation -UseQuotes Never
}
catch {
	Remove-Item -Recurse -Force $covers, $settingsPy, $settingsNet -ErrorAction SilentlyContinue
	throw
}

$net = Join-Path $Bin "Scripts\ComicVineScraperNet"
$probe = Join-Path $Bin "Scripts\CvsDiffProbe"
$py = Join-Path $Bin "Scripts\ComicVineScraperPy"
if ((Test-Path $net) -or (Test-Path $probe) -or (Test-Path $py)) { throw "an earlier install is still in Scripts; remove it first" }

$failures = @()
function Assert-Run([string]$name, [bool]$ok, [string]$detail) {
	$line = "ASSERT $(if ($ok) { 'PASS' } else { 'FAIL' }) ${name}: $detail"
	Add-Content -Path $report -Value $line
	if (-not $ok) { $script:failures += $name }
}

Assert-Run "normal build has no run switches" (-not (Test-DllContains $normalDll "CVS_NET_LIVE_FOLDER") -and -not (Test-DllContains $normalDll "CVS_NET_RECORDED_RESPONSES")) "neither variable name appears in $normalDll"
Assert-Run "switch build detection works" ((Test-DllContains $switchDll "CVS_NET_LIVE_FOLDER") -and (Test-DllContains $switchDll "CVS_NET_RECORDED_RESPONSES")) "both variable names appear in the switch build, so the check above can find them"

try {
	New-Item -ItemType Directory -Force $net, $probe, $py | Out-Null
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	[System.IO.Compression.ZipFile]::ExtractToDirectory($crplugin, $net)
	Copy-Item $probeDll $probe

	# The same flattening build.xml's dist target does, so the copy is what a user installs.
	Get-ChildItem $PythonSource -Recurse -File |
		Where-Object { $_.Name -ne "__init__.py" -and $_.FullName -notmatch '[\\/]tests[\\/]' } |
		ForEach-Object { Copy-Item $_.FullName $py }
	$version = [regex]::Match((Get-Content (Join-Path $PythonSource "resources\Package.ini") -Raw), '(?m)^Version\s*=\s*(\S+)').Groups[1].Value
	$resources = Join-Path $py "resources.py"
	$text = [System.IO.File]::ReadAllText($resources).Replace("!DEV!", $version)
	$pattern = 'profile_dir\s*=\s*Environment\.GetFolderPath\(\s*Environment\.SpecialFolder\.ApplicationData\)\s*\+\s*\\\s*r"\\Comic Vine Scraper"'
	$patched = [regex]::Replace($text, $pattern, { param($m) 'profile_dir = r"' + $settingsPy + '"' })
	if ($patched -eq $text) { throw "resources.py no longer has the profile_dir statement the script patches" }
	[System.IO.File]::WriteAllText($resources, $patched)
	Add-Content -Path $report -Value "Python plugin $version installed as a copy; its profile_dir is the run's settings-py folder"

	# Neither plugin logs its requests, so both are counted at the HTTP seam: the switch build's handler for C#, and, in the installed Python copy
	# only, one line before each request that appends the host and path (never the query, which carries the key) to requests.txt.
	$counter = 'from System.IO import File as CvsDiffFile; CvsDiffFile.AppendAllText(r"' + $settingsPy + '\requests.txt", {0}.split("?")[0].split("//")[-1] + "\n")'
	$connection = Join-Path $py "cvconnection.py"
	$cText = [System.IO.File]::ReadAllText($connection)
	$cPatched = [regex]::Replace($cText, '(?m)^(   )(wait_until_ready\(\) # throttle request speed to make ComicVine happy)', { param($m) $m.Groups[1].Value + ($counter -f 'url') + "`n" + $m.Value })
	if ($cPatched -eq $cText) { throw "cvconnection.py no longer has the request line the script instruments" }
	[System.IO.File]::WriteAllText($connection, $cPatched)
	$database = Join-Path $py "cvdb.py"
	$dText = [System.IO.File]::ReadAllText($database)
	$dPatched = [regex]::Replace($dText, '(?m)^(         )(request = WebRequest\.Create\(image_url_s\))', { param($m) $m.Groups[1].Value + ($counter -f 'image_url_s') + "`n" + $m.Value })
	if ($dPatched -eq $dText) { throw "cvdb.py no longer has the image request line the script instruments" }
	[System.IO.File]::WriteAllText($database, $dPatched)

	$env:CVS_DIFF_OUT = $out
	$env:CVS_DIFF_INPUT = $inputs
	$env:CVS_NET_LIVE_FOLDER = $settingsNet
	# Start-Process -PassThru gives this run's own process. It is the only one waited on and the only one ever killed.
	$p = Start-Process (Join-Path $Bin "ComicRack.exe") -ArgumentList "-ac", "CvsDiff" -PassThru
	Remove-Item Env:CVS_DIFF_OUT, Env:CVS_DIFF_INPUT, Env:CVS_NET_LIVE_FOLDER
	$exited = $p.WaitForExit($TimeoutMinutes * 60000)
	if (-not $exited) { $p.Kill(); $p.WaitForExit() }
	$closedItself = $exited -and $p.ExitCode -eq 0
	Add-Content -Path $report -Value "ComicRack pid $($p.Id) exited with $($p.ExitCode)"
	Assert-Run "ComicRack closed itself" $closedItself "the probe calls MainForm.MenuClose and ComicRack exits with code 0 inside $TimeoutMinutes minutes; the script kills pid $($p.Id) only if this fails"
}
finally {
	Remove-Item Env:CVS_DIFF_OUT, Env:CVS_DIFF_INPUT, Env:CVS_NET_LIVE_FOLDER -ErrorAction SilentlyContinue
	Remove-Item -Recurse -Force $net, $probe, $py, $covers -ErrorAction SilentlyContinue
	foreach ($root in $env:APPDATA, $env:LOCALAPPDATA) {
		Remove-Item -Recurse -Force (Join-Path $root "cYo\ComicRack Community Edition\Configurations\CvsDiff") -ErrorAction SilentlyContinue
	}
}

function Get-RequestCounts([string]$file) {
	$counts = [ordered]@{}
	if (Test-Path $file) {
		foreach ($line in Get-Content $file | Where-Object { $_.Trim() }) {
			$parts = $line.Trim().Split('/')
			$category = if ($parts[0] -like '*comicvine.gamespot.com' -and $parts[1] -eq 'api') { "api/" + $parts[2] } else { "images or other: " + $parts[0] }
			$counts[$category] = 1 + [int]$counts[$category]
		}
	}
	return $counts
}
$requestLines = foreach ($pair in @(@("python", $settingsPy), @("net", $settingsNet))) {
	$counts = Get-RequestCounts (Join-Path $pair[1] "requests.txt")
	$api = ($counts.GetEnumerator() | Where-Object { $_.Key -like "api/*" } | Measure-Object -Property Value -Sum).Sum
	"REQUESTS $($pair[0]): $([int]$api) Comic Vine API calls ($(($counts.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '))"
}
Remove-Item -Recurse -Force $covers -ErrorAction SilentlyContinue

# What each plugin wrote into its own settings folder, by name and size. The folders hold the key, so they are deleted afterwards.
$written = foreach ($folder in $settingsPy, $settingsNet) {
	"$(Split-Path $folder -Leaf): " + ((Get-ChildItem $folder -Recurse -File | ForEach-Object { "$($_.FullName.Substring($folder.Length + 1)) ($($_.Length) bytes)" }) -join ", ")
}
Remove-Item -Recurse -Force $settingsPy, $settingsNet

$probeText = if (Test-Path (Join-Path $out "probe.txt")) { Get-Content (Join-Path $out "probe.txt") -Raw } else { "" }
Assert-Run "probe ran without an exception" ($probeText -notmatch "(?m)^EXCEPTION") "no EXCEPTION line in probe.txt"
$snapshot = Join-Path $out "snapshot.tsv"
Assert-Run "probe wrote a snapshot" (Test-Path $snapshot) "snapshot.tsv exists"

. (Join-Path $here "Compare-Snapshot.ps1")
if (Test-Path $snapshot) {
	$comparison = Compare-Snapshot -Path $snapshot
	Add-Content -Path $report -Value ""
	Add-Content -Path $report -Value $comparison.Lines
	Add-Content -Path $report -Value ""
	Add-Content -Path $report -Value $requestLines
	Assert-Run "both plugins scraped the same number of books" ($comparison.Books -gt 0) "$($comparison.Books) books per set"
}
Add-Content -Path $report -Value ""
Add-Content -Path $report -Value "--- Comic Vine lookups for the book table"
Add-Content -Path $report -Value $lookupLines
Add-Content -Path $report -Value "--- probe.txt"
Add-Content -Path $report -Value $probeText
Add-Content -Path $report -Value "--- files each plugin wrote"
Add-Content -Path $report -Value $written

$after = @(Get-UserSettings)
Assert-Run "user settings unchanged" ($null -eq (Compare-Object $before $after)) "$($before.Count) entries before, $($after.Count) after, in %APPDATA%\Comic Vine Scraper (names, sizes, write times)"
$left = (Test-Path $net) -or (Test-Path $probe) -or (Test-Path $py) -or (Test-Path (Join-Path $env:APPDATA "cYo\ComicRack Community Edition\Configurations\CvsDiff")) -or (Test-Path (Join-Path $env:LOCALAPPDATA "cYo\ComicRack Community Edition\Configurations\CvsDiff"))
Assert-Run "nothing left behind" (-not $left) "the three Scripts folders and the run's ComicRack configuration are removed"
Assert-Run "volume table matches Comic Vine" ($lookupFailures -eq 0) "$($lookupLines.Count) lookups, $lookupFailures mismatches"
Assert-Run "no cover left behind" (-not (Test-Path $covers)) "the downloaded covers and the .cbz files are deleted from the run folder"
Assert-Run "no ComicRack process left" ($null -eq (Get-Process ComicRack -ErrorAction SilentlyContinue)) "Get-Process ComicRack finds nothing"
Assert-Run "settings folders removed" (-not ((Test-Path $settingsPy) -or (Test-Path $settingsNet))) "the folders that held the key are deleted"

$leaks = Get-ChildItem $run -Recurse -File | Where-Object { Select-String -Path $_.FullName -Pattern $apiKey -SimpleMatch -Quiet }
Assert-Run "no key in any kept artifact" (-not $leaks) "scanned $((Get-ChildItem $run -Recurse -File | Measure-Object).Count) files under $run for the API key"

Get-Content $report
Write-Output "--- report: $report"
if ($failures) {
	Write-Output ("DIFFERENTIAL RUN FAILED: " + (($failures | Select-Object -Unique) -join "; "))
	exit 1
}
Write-Output "DIFFERENTIAL RUN COMPLETED (all safety assertions passed)"
