param([string]$Root = 'C:\MyProj\Calamo\out\audiveris', [string]$Other = 'audiveris', [int]$MaxDiff = 2)
# Bars of the fair sets where another reader ($Other: audiveris, or homr after run-audiveris.ps1 -Fair -Homr) disagrees
# with the truth on pitches by at most $MaxDiff notes while our reader agrees with the truth (the bar is absent from
# scored-own\diffs.txt): the bars no check against our reader's diffs ever looks at, where the truth and our reader could
# share an error. sameCount=True (same number of notes, a pitch or two apart) are the ones worth a zoom first (cropbar.py).
function Read-Diffs($path) {
    $map = @{}
    if (-not (Test-Path $path)) { return $null }
    $lines = Get-Content $path
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s+(\d+)\s+(\S+)\s+T:\s?(.*)$') {
            $key = "$($Matches[1]) $($Matches[2])"; $t = $Matches[3]
            $o = if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\s+O:\s?(.*)$') { $Matches[1] } else { '' }
            $map[$key] = @($t, $o)
        }
    }
    $map
}
function Pitches($s) {
    @($s -split '\s+' | Where-Object { $_ } | ForEach-Object { if ($_ -match "^([a-g](?:isis|eses|is|es)?[',]*)") { $Matches[1] } })
}
function MinusBag($a, $b) {
    $l = [Collections.Generic.List[string]]::new(); $a | ForEach-Object { $l.Add($_) }
    foreach ($x in $b) { [void]$l.Remove($x) }
    , $l.ToArray()
}
$rows = foreach ($set in Get-ChildItem $Root -Directory -Filter 'fair-*' | Where-Object { Test-Path "$($_.FullName)\scored-own" } | Sort-Object Name) {
    $own = Read-Diffs "$($set.FullName)\scored-own\diffs.txt"
    $oth = Read-Diffs "$($set.FullName)\scored-$Other\diffs.txt"
    if ($null -eq $oth) { continue }
    foreach ($k in $oth.Keys) {
        if ($own.ContainsKey($k)) { continue }
        $t = Pitches $oth[$k][0]; $o = Pitches $oth[$k][1]
        if ($t.Count -eq 0 -or $o.Count -eq 0) { continue }
        $tOnly = MinusBag $t $o; $oOnly = MinusBag $o $t
        $n = [Math]::Max($tOnly.Count, $oOnly.Count)
        if ($n -eq 0 -or $n -gt $MaxDiff) { continue }
        $m, $part = $k -split ' '
        [pscustomobject]@{ set = $set.Name -replace '^fair-', ''; bar = [int]$m; part = $part; n = $n
            truthOnly = $tOnly -join ' '; otherOnly = $oOnly -join ' '; sameCount = ($t.Count -eq $o.Count) }
    }
}
$rows | Sort-Object set, bar, part
