param(
    [string]$Out = "$PSScriptRoot\..\out\gen-lied-all",   # a run-gen.ps1 folder of pieces made with OmrGen --lyrics
    [switch]$Detail                                       # list every bar with a miss or an extra
)
# How well the reader reads a song's words (S54): each piece's .lys (the truth) and the reader's
# omr/omr.lys, by part, line of words (the n-th track of the part) and bar. Counted per bar: the syllables
# as spelt, alone ("found"); with the hyphen too ("hyphen"); letter by letter over the found and the
# rest, by edit distance in the order of the bar ("chars"). Prints one line, the rates in percent.
$ErrorActionPreference = 'Stop'

# part -> track number -> list of bars -> list of syllables as written ("schwin-")
function Tracks([string]$path) {
    $tracks = @{}
    foreach ($line in Get-Content $path) {
        if ($line -notmatch '^\s*lyrics\s+\S+\s+sings\s+(\S+)\s*\{(.*)\}\s*$') { continue }
        $part = $Matches[1]; $body = $Matches[2]
        if (-not $tracks.ContainsKey($part)) { $tracks[$part] = [Collections.Generic.List[object]]::new() }
        $bars = [Collections.Generic.List[object]]::new()
        foreach ($b in ($body -split '\|')) { $bars.Add(@($b.Trim() -split '\s+' | Where-Object { $_ -and $_ -ne '~' -and $_ -ne '_' })) }
        $tracks[$part].Add($bars)
    }
    return $tracks
}
function Edit([string]$a, [string]$b) {
    $d = 0..$b.Length
    for ($i = 1; $i -le $a.Length; $i++) {
        $prev = $d[0]; $d[0] = $i
        for ($j = 1; $j -le $b.Length; $j++) {
            $t = $d[$j]
            $d[$j] = [Math]::Min([Math]::Min($d[$j] + 1, $d[$j - 1] + 1), $prev + $(if ($a[$i - 1] -ceq $b[$j - 1]) { 0 } else { 1 }))
            $prev = $t
        }
    }
    return $d[$b.Length]
}
$truthN = 0; $readN = 0; $found = 0; $hyphen = 0; $chars = 0; $wrongChars = 0; $pieces = 0; $lines = @()
foreach ($dir in Get-ChildItem $Out -Recurse -Directory | Where-Object { Test-Path "$($_.FullName)\omr\omr.lys" }) {
    $src = Join-Path $dir.FullName "$($dir.Name).lys"
    if (-not (Test-Path $src)) { continue }
    $t = Tracks $src; $r = Tracks "$($dir.FullName)\omr\omr.lys"
    if ($t.Count -eq 0 -and $r.Count -eq 0) { continue }
    $pieces++
    foreach ($part in @($t.Keys) + @($r.Keys | Where-Object { -not $t.ContainsKey($_) })) {
        $tt = @(); if ($t.ContainsKey($part)) { $tt = $t[$part] }
        $rt = @(); if ($r.ContainsKey($part)) { $rt = $r[$part] }
        for ($v = 0; $v -lt [Math]::Max($tt.Count, $rt.Count); $v++) {
            $tb = @(); if ($v -lt $tt.Count) { $tb = $tt[$v] }
            $rb = @(); if ($v -lt $rt.Count) { $rb = $rt[$v] }
            for ($b = 0; $b -lt [Math]::Max($tb.Count, $rb.Count); $b++) {
                $x = @(); if ($b -lt $tb.Count) { $x = @($tb[$b]) }
                $y = @(); if ($b -lt $rb.Count) { $y = @($rb[$b]) }
                $truthN += $x.Count; $readN += $y.Count
                $left = [Collections.Generic.List[string]]::new(); foreach ($q in $y) { $left.Add($q) }
                $missed = @()
                foreach ($q in $x) {
                    if ($left.Remove($q)) { $found++; $hyphen++ ; $chars += $q.TrimEnd('-').Length; continue }
                    $bare = $q.TrimEnd('-')
                    $other = $left | Where-Object { $_.TrimEnd('-') -ceq $bare } | Select-Object -First 1
                    if ($other) { [void]$left.Remove($other); $found++; $chars += $bare.Length; continue }
                    $missed += $q
                }
                # (what is left, in order: letter by letter)
                for ($i = 0; $i -lt $missed.Count; $i++) {
                    $m = $missed[$i].TrimEnd('-'); $chars += $m.Length
                    $wrongChars += if ($i -lt $left.Count) { [Math]::Min($m.Length, (Edit $m $left[$i].TrimEnd('-'))) } else { $m.Length }
                }
                if ($Detail -and ($missed.Count -gt 0 -or $left.Count -gt 0)) { $lines += "$($dir.Name) $part v$($v + 1) bar $($b + 1): truth [$($x -join ' ')] read [$($y -join ' ')]" }
            }
        }
    }
}
if ($Detail) { $lines }
$pct = { param($a, $n) if ($n -eq 0) { 'n/a' } else { '{0:F1}' -f (100.0 * $a / $n) } }
"pieces $pieces syllables $truthN read $readN  recall $(& $pct $found $truthN)  precision $(& $pct $found $readN)  hyphen $(& $pct $hyphen $truthN)  chars $(& $pct ($chars - $wrongChars) $chars)"