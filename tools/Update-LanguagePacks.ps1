# Applies this plugin's product name to every language zip. The zips come from the Python plugin, so two edits are needed:
# - comic-api-net.xml localizes the command with key comic-api-net. It copies comic-vine-scraper.xml for the localized
#   description, renames the table, and sets the command name.
# - Script.ComicVineScraper.xml keeps its name and keys (a format shared with the Python plugin), but FinishFormTitle, the
#   title of the progress, welcome and summary forms, becomes the product name.
# Each run rewrites both from the zip's own text, so a rerun always leaves the zips matching this script.
param([string]$Languages = (Join-Path $PSScriptRoot "..\src\ComicApiPlugin\Package\Languages"))

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$product = "Comic API Plugin"
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Read-Entry($zip, [string]$name) {
	$reader = New-Object System.IO.StreamReader($zip.GetEntry($name).Open(), $utf8)
	try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Write-Entry($zip, [string]$name, [string]$text) {
	$old = $zip.GetEntry($name)
	if ($old) { $old.Delete() }
	$writer = New-Object System.IO.StreamWriter($zip.CreateEntry($name).Open(), $utf8)
	try { $writer.Write($text) } finally { $writer.Dispose() }
}

function Set-Text([string]$xml, [string]$key, [string]$value, [string]$file) {
	$pattern = '<Text Key="' + $key + '" Text="[^"]*" Comment="[^"]*"'
	if (-not [regex]::IsMatch($xml, $pattern)) { throw "$file has no $key text" }
	return [regex]::Replace($xml, $pattern, "<Text Key=`"$key`" Text=`"$value`" Comment=`"$value`"")
}

foreach ($file in Get-ChildItem -Path $Languages -Filter *.zip) {
	$zip = [System.IO.Compression.ZipFile]::Open($file.FullName, [System.IO.Compression.ZipArchiveMode]::Update)
	try {
		$command = (Read-Entry $zip "comic-vine-scraper.xml").Replace('Name="comic-vine-scraper"', 'Name="comic-api-net"')
		Write-Entry $zip "comic-api-net.xml" (Set-Text $command "Name" "$product..." $file.Name)

		$strings = Read-Entry $zip "Script.ComicVineScraper.xml"
		Write-Entry $zip "Script.ComicVineScraper.xml" (Set-Text $strings "FinishFormTitle" $product $file.Name)
	}
	finally {
		$zip.Dispose()
	}
}
