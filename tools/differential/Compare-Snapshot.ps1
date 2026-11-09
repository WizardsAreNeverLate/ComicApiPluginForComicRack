<#
Compares the snapshot the probe writes (snapshot.tsv: set, book index, field, value). The sets are baseline, python and net.
Returns the report lines and the number of books. Id is skipped: ComicRack gives every ComicBook its own GUID. A field is printed when either plugin changed it; a field both left alone is only counted.
#>
function ConvertTo-Short([string]$value) {
	if ($value.Length -gt 300) { return $value.Substring(0, 300) + "...(" + $value.Length + " chars)" }
	return $value
}

function Compare-Snapshot([string]$Path) {
	$data = @{}
	foreach ($line in [System.IO.File]::ReadLines($Path, [System.Text.Encoding]::UTF8)) {
		$parts = $line.Split("`t", 4)
		if ($parts.Count -lt 4) { continue }
		$key = "$($parts[0])`t$($parts[1])"
		if (-not $data.ContainsKey($key)) { $data[$key] = [ordered]@{} }
		$value = $parts[3]
		# A custom thumbnail key is a new GUID per scrape; the thumbnail's own hash is compared instead.
		if ($parts[2] -eq "CustomThumbnailKey" -and $value -match '^[0-9a-f-]{36}$') { $value = "<guid>" }
		$data[$key][$parts[2]] = $value
	}
	$indexes = $data.Keys | Where-Object { $_ -like "baseline`t*" } | ForEach-Object { [int]($_.Split("`t")[1]) } | Sort-Object
	$lines = New-Object System.Collections.Generic.List[string]
	$diffCount = 0
	$sameChanged = 0
	foreach ($i in $indexes) {
		$base = $data["baseline`t$i"]; $py = $data["python`t$i"]; $net = $data["net`t$i"]
		$lines.Add("BOOK ${i}: $($base['~label'])")
		if ($null -eq $py -or $null -eq $net) { $lines.Add("  ! a set is missing"); $diffCount++; continue }
		$fields = @($py.Keys) + @($net.Keys) | Where-Object { $_ -ne "~label" -and $_ -ne "Id" } | Select-Object -Unique
		$unchanged = 0
		foreach ($f in $fields) {
			$b = if ($base.Contains($f)) { $base[$f] } else { "<absent>" }
			$p = if ($py.Contains($f)) { $py[$f] } else { "<absent>" }
			$n = if ($net.Contains($f)) { $net[$f] } else { "<absent>" }
			if ($p -ceq $n) {
				if ($p -ceq $b) { $unchanged++ } else { $sameChanged++; $lines.Add("  = $f : '$(ConvertTo-Short $b)' -> '$(ConvertTo-Short $p)'") }
			}
			else {
				$diffCount++
				$lines.Add("  ! DIFF $f : python='$(ConvertTo-Short $p)' net='$(ConvertTo-Short $n)' (baseline '$(ConvertTo-Short $b)')")
			}
		}
		$lines.Add("  ($unchanged fields untouched by both)")
	}
	$lines.Add("SUMMARY: $($indexes.Count) books, $diffCount differing fields, $sameChanged fields both plugins changed identically")
	[pscustomobject]@{ Lines = $lines; Books = $indexes.Count; Differences = $diffCount }
}
