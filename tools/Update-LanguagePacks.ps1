# Applies this plugin's own wording to the language zips. The zips come from the Python plugin, so these edits are needed:
# - comic-api-net.xml localizes the command with key comic-api-net. It copies comic-vine-scraper.xml for the localized
#   description, renames the table, and sets the command name.
# - Script.ComicVineScraper.xml keeps its name and keys (a format shared with the Python plugin), but FinishFormTitle, the
#   title of the progress, welcome and summary forms, becomes the product name.
# - In English only, the welcome form's start button and the message that names it say "Query API" instead of
#   "Start Scraping...". The other languages keep their own translation.
# - In English only, the strings for the VerseDB provider (cover search, its candidate dialog, the Providers tab) are
#   added. They have no counterpart in the Python plugin, so ComicRack falls back to the English default text in every other
#   language.
# Each run rewrites from the zip's own text and leaves an unchanged entry alone, so a rerun changes nothing.
param([string]$Languages = (Join-Path $PSScriptRoot "..\src\ComicApiPlugin\Package\Languages"))

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$product = "Comic API Plugin"
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

function Read-Entry($zip, [string]$name) {
	$entry = $zip.GetEntry($name)
	if (-not $entry) { return $null }
	$reader = New-Object System.IO.StreamReader($entry.Open(), $utf8)
	try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Write-Entry($zip, [string]$name, [string]$text) {
	if ((Read-Entry $zip $name) -ceq $text) { return }
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

# Texts this plugin adds on top of the Python plugin's. Use no quote, ampersand, or angle bracket in a value.
$verseDbTexts = [ordered]@{
	WelcomeFormCoverSearch = "Cover Search (VerseDB)"
	CoverMatchFormTitle = "VerseDB Cover Search"
	CoverMatchFormChooseText = "VerseDB found these possible matches for the cover of {0}. Choose the right issue."
	CoverMatchFormYourCover = "Your cover"
	CoverMatchFormDateCol = "Cover date"
	CoverMatchFormSelect = "Select"
	CoverSearchNoMatchesText = "VerseDB found no matches for the cover of {0}."
	CoverSearchRateLimited = "VerseDB asks to wait {0} seconds..."
	VerseDbErrorTitle = "VerseDB Error"
	VerseDbErrorText = "VerseDB reported a problem:\n\n{0}"
	ConfigFormProvidersTab = "Providers"
	ConfigFormProvidersText = "Tick the providers to use. A provider higher in the list comes first on the start dialog."
	ConfigFormProviderUp = "Up"
	ConfigFormProviderDown = "Down"
	ConfigFormVerseDbName = "VerseDB"
	ConfigFormVerseDbText = "VerseDB cover search needs an API token. Paste yours in the space below."
	ConfigFormVerseDbClickHere = "Click here to create a VerseDB API token"
}

function Add-Text([string]$xml, [string]$key, [string]$value) {
	if ([regex]::IsMatch($xml, '<Text Key="' + $key + '" ')) { return Set-Text $xml $key $value "en.zip" }
	$line = "`t<Text Key=`"$key`" Text=`"$value`" Comment=`"$value`"/>`r`n"
	return $xml.Replace("  </Texts>", $line + "  </Texts>")
}
foreach ($file in Get-ChildItem -Path $Languages -Filter *.zip) {
	$zip = [System.IO.Compression.ZipFile]::Open($file.FullName, [System.IO.Compression.ZipArchiveMode]::Update)
	try {
		$command = (Read-Entry $zip "comic-vine-scraper.xml").Replace('Name="comic-vine-scraper"', 'Name="comic-api-net"')
		Write-Entry $zip "comic-api-net.xml" (Set-Text $command "Name" "$product..." $file.Name)

		$strings = Set-Text (Read-Entry $zip "Script.ComicVineScraper.xml") "FinishFormTitle" $product $file.Name
		if ($file.Name -eq "en.zip") {
			$strings = (Set-Text $strings "WelcomeFormStart" "Query API" $file.Name).Replace("'Start Scraping...'", "'Query API'")
			foreach ($entry in $verseDbTexts.GetEnumerator()) { $strings = Add-Text $strings $entry.Key $entry.Value }
		}
		Write-Entry $zip "Script.ComicVineScraper.xml" $strings
	}
	finally {
		$zip.Dispose()
	}
}
