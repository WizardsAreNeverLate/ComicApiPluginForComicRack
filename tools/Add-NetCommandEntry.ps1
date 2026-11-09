# Adds comic-api-net.xml to every language zip, so ComicRack can localize the command with key comic-api-net.
# The entry copies comic-vine-scraper.xml, renames the table, and puts ".NET" in the command name. Safe to run again.
param([string]$Languages = (Join-Path $PSScriptRoot "..\src\ComicApiPlugin\Package\Languages"))

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($file in Get-ChildItem -Path $Languages -Filter *.zip) {
	$zip = [System.IO.Compression.ZipFile]::Open($file.FullName, [System.IO.Compression.ZipArchiveMode]::Update)
	try {
		if ($zip.GetEntry("comic-api-net.xml")) { continue }
		$reader = New-Object System.IO.StreamReader($zip.GetEntry("comic-vine-scraper.xml").Open(), $utf8)
		$text = $reader.ReadToEnd()
		$reader.Dispose()
		$text = $text.Replace('Name="comic-vine-scraper"', 'Name="comic-api-net"')
		$text = [regex]::Replace($text, '(<Text Key="Name" Text="[^"]*?)(\.\.\.)?(" Comment=)', '$1 .NET$2$3')
		$entry = $zip.CreateEntry("comic-api-net.xml")
		$writer = New-Object System.IO.StreamWriter($entry.Open(), $utf8)
		$writer.Write($text)
		$writer.Dispose()
	}
	finally {
		$zip.Dispose()
	}
}
