param([string]$Root = 'C:\MyProj\Calamo\out\audiveris', [string]$Other = 'homr', [int]$Systematic = 3,
      [switch]$Reserve)   # the reserve sets instead (no scored-own: this reader never read them)
# Bars where the truth may have dropped an accidental that carries on to the end of the bar (the commonest truth error
# found against homr in Kuecken and Wuerst, 2026-11-18): the other reader has a note on the same staff step as one of
# the truth's but with another accidental, and that very pitch is written elsewhere in the same bar of the truth (so a
# sign earlier in the bar may still hold). A swap the other reader makes in $Systematic or more bars of a set (a key
# signature it misread: des'' -> d'' all through) is left out. Only the diffs files are read: never this reader.
function Read-Diffs($path) {
    $map = [ordered]@{}
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
function Step($p) { ($p -replace '(isis|eses|is|es)', '') }   # letter and octave marks: the staff step
$rows = foreach ($set in Get-ChildItem $Root -Directory -Filter 'fair-*' | Where-Object { $_.Name -notlike '*-truth' } | Sort-Object Name) {
    $own = Test-Path "$($set.FullName)\scored-own"
    if ($Reserve -eq $own) { continue }
    $d = Read-Diffs "$($set.FullName)\scored-$Other\diffs.txt"
    if ($null -eq $d) { continue }
    $found = foreach ($k in $d.Keys) {
        $t = Pitches $d[$k][0]; $o = Pitches $d[$k][1]
        $lost = MinusBag $t $o; $more = MinusBag $o $t
        foreach ($m in $lost) {
            foreach ($e in $more) {
                if ((Step $m) -eq (Step $e) -and $m -ne $e -and $t -contains $e) {
                    [pscustomobject]@{ Set = $set.Name -replace '^fair-', ''; Bar = $k; Truth = $m; Other = $e; T = $d[$k][0]; O = $d[$k][1] }
                }
            }
        }
    }
    $swaps = @($found) | Group-Object { "$($_.Truth)>$($_.Other)" }
    foreach ($g in $swaps) {
        $bars = @($d.Keys | Where-Object { $p = Pitches $d[$_][0]; $q = Pitches $d[$_][1]; (MinusBag $p $q) -contains $g.Group[0].Truth -and (MinusBag $q $p) -contains $g.Group[0].Other })
        if ($bars.Count -lt $Systematic) { $g.Group }
    }
}
$rows = @($rows | Group-Object Set, Bar, Truth, Other | ForEach-Object { $_.Group[0] })
$rows | Sort-Object Set, { [int]($_.Bar -split ' ')[0] } | Format-Table Set, Bar, Truth, Other, T, O -AutoSize -Wrap
"carry suspects: $(@($rows).Count)"
