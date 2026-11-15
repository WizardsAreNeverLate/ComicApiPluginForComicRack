param(
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Debug",
	[string]$ComicRackDir = $env:COMICRACK_DIR,
	[switch]$Rebuild
)

# Builds the solution without running the test suites. See Build.ps1.
& (Join-Path $PSScriptRoot "Build.ps1") @PSBoundParameters -SkipTests
exit $LASTEXITCODE
