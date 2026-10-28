param(
    [string[]]$Sets = @(),          # only these sets (names under $Out); all when empty
    [switch]$Eval,                  # the held-out sets only (every set not dev-*)
    [switch]$Dev,                   # the development sets only (dev-*)
    [string]$Out = '',              # where the sets are (out/audiveris: run-audiveris.ps1 -Eval / -Dev)
    [switch]$BySet,                 # a line per set too
    [string]$Before = ''            # the same sets read with --no-fix (a copy of out/audiveris): each bar put right, held against how it was read before
)
# How well the todos mark the bars that are wrong (ROADMAP S33: what the automatic correction can
# reach). Reads each set's scored-own/todos-truth.txt (omr writes it with --truth, after the
# todos are placed) and diffs.txt: of the todos, by source and by whether they go into the .lys,
# how many mark a part-bar the truth scores wrong (the hit rate); of the part-bars scored wrong,
# how many carry no mark at all (what no correction of the marked bars can reach).
$root = Resolve-Path "$PSScriptRoot\.."
if ($Out -eq '') { $Out = "$root\out\audiveris" }
$Sets = @($Sets | ForEach-Object { $_ -split ',' })

$todos = [System.Collections.Generic.List[object]]::new()
$perSet = [ordered]@{}
foreach ($f in Get-ChildItem "$Out\*\scored-own\todos-truth.txt" | Sort-Object FullName) {
    $set = $f.Directory.Parent.Name
    if ($Sets.Count -gt 0 -and $Sets -notcontains $set) { continue }
    if ($Dev -and -not $set.StartsWith('dev-')) { continue }
    if ($Eval -and $set.StartsWith('dev-')) { continue }
    $unmarked = 0; $lost = 0; $n = 0; $hit = 0
    foreach ($line in Get-Content $f.FullName) {
        $c = $line -split "`t"
        if ($c[0] -eq 'todo') {
            $t = [pscustomobject]@{ set = $set; key = $c[1]; source = $c[2]; kind = $c[3]; bar = [int]$c[4]; scored = [int]$c[5]; part = $c[6]
                                    confidence = [double]$c[7]; written = ($c[8] -eq 'written'); verdict = $c[9] }
            $todos.Add($t); $n++
            if ($t.verdict -eq 'wrong') { $hit++ }
        }
        elseif ($c[0] -eq 'unmarked') { $unmarked++; if ($c.Count -gt 3 -and $c[3] -eq 'lost') { $lost++ } }
    }
    # (a wrong part-bar prints one "bar part T:" line in diffs.txt; an extra measure's lines begin with +)
    $wrongBars = @(Get-Content "$($f.Directory.FullName)\diffs.txt" -ErrorAction SilentlyContinue | Where-Object { $_ -match '^\s*\d+\s+\S+\s+T:' }).Count
    $perSet[$set] = [pscustomobject]@{ set = $set; todos = $n; hit = $hit; wrongBars = $wrongBars; unmarked = $unmarked; lost = $lost }
}
if ($todos.Count -eq 0 -and $perSet.Count -eq 0) { "no todos-truth.txt under $Out (run run-audiveris.ps1 -Eval/-Dev -Reuse with a reader that writes it)"; return }

function Rate($num, $den) { if ($den -gt 0) { '{0,5:F1}%' -f (100.0 * $num / $den) } else { '    -' } }
function Row($name, $group) {
    $w = @($group | Where-Object verdict -eq 'wrong').Count; $r = @($group | Where-Object verdict -eq 'right').Count
    $x = @($group | Where-Object verdict -eq 'extra').Count; $u = @($group | Where-Object verdict -eq 'unscored').Count
    '{0,-28} {1,6} wrong {2,5} right {3,5} extra {4,4} unscored {5,4}  hit {6}' -f $name, @($group).Count, $w, $r, $x, $u, (Rate $w ($w + $r))
}
"sets $($perSet.Count): todos $($todos.Count) (a todo hits when a part-bar it covers is scored wrong)"
foreach ($g in $todos | Group-Object source, written | Sort-Object Name) {
    $src, $wr = $g.Name -split ', '
    Row ("{0} {1}" -f $src, $(if ($wr -eq 'True') { 'written' } else { 'quiet' })) $g.Group
}
Row 'all written' ($todos | Where-Object written)
Row 'all' $todos
''
'by confidence (the written and the quiet alike):'
foreach ($g in $todos | Group-Object { [math]::Floor($_.confidence * 5) / 5 } | Sort-Object { [double]$_.Name }) {
    $lo = [double]$g.Name
    Row ('  {0:F1}-{1:F1}' -f $lo, ($lo + 0.2)) $g.Group
}
''
$wrongAll = ($perSet.Values | Measure-Object wrongBars -Sum).Sum
$unmarkedAll = ($perSet.Values | Measure-Object unmarked -Sum).Sum
$lostAll = ($perSet.Values | Measure-Object lost -Sum).Sum
"part-bars scored wrong: $wrongAll; with a mark $($wrongAll - $unmarkedAll) ($(Rate ($wrongAll - $unmarkedAll) $wrongAll)), without $unmarkedAll (of them in bars no system was found for: $lostAll)"
if ($BySet) {
    ''
    '{0,-34} {1,6} {2,6} {3,8} {4,9} {5,6}' -f 'set', 'todos', 'hit', 'wrongBar', 'unmarked', 'lost'
    foreach ($s in $perSet.Values) { '{0,-34} {1,6} {2,6} {3,8} {4,9} {5,6}' -f $s.set, $s.todos, $s.hit, $s.wrongBars, $s.unmarked, $s.lost }
}
# the bars put right (source "fixed"), each against the same bar read with --no-fix ($Before): put
# right (exact now, not before), nearer (more of the truth's events found than before), the same,
# further (fewer), or exact before already (nothing to put right there: a bar the truth leaves short)
if ($Before -ne '') {
    # per set: the wrong part-bars of diffs.txt as "bar/part" -> how many of the truth's events the reading has (pitch and duration)
    function WrongBars($diffs) {
        $m = @{}
        $lines = @(Get-Content $diffs -ErrorAction SilentlyContinue)
        for ($i = 0; $i + 1 -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\s*(\d+)\s+(\S+)\s+T:\s*(.*)$') {
                $k = "$($Matches[1])/$($Matches[2])"; $t = @($Matches[3] -split ' ' | Where-Object { $_ })
                if ($lines[$i + 1] -match '^\s*O:\s*(.*)$') {
                    $o = [System.Collections.Generic.List[string]]@($Matches[1] -split ' ' | Where-Object { $_ })
                    $found = 0
                    foreach ($e in $t) { if ($o.Remove($e)) { $found++ } }
                    $m[$k] = @{ Found = $found; Truth = $t.Count }
                }
            }
        }
        $m
    }
    $fixed = $todos | Where-Object source -eq 'fixed'
    $tally = [ordered]@{ 'put right' = 0; 'nearer' = 0; 'the same' = 0; 'further' = 0; 'BROKEN' = 0; 'exact before' = 0; 'no before' = 0 }
    $cache = @{}
    foreach ($t in $fixed) {
        if (-not $cache.ContainsKey($t.set)) {
            $cache[$t.set] = @{ After = (WrongBars "$Out\$($t.set)\scored-own\diffs.txt"); Before = (WrongBars "$Before\$($t.set)\scored-own\diffs.txt"); Has = (Test-Path "$Before\$($t.set)\scored-own\diffs.txt") }
        }
        $c = $cache[$t.set]
        if (-not $c.Has) { $tally['no before']++; continue }
        $k = "$($t.scored)/$($t.part)"
        $a = $c.After[$k]; $b = $c.Before[$k]
        # (a bar exact before and wrong now is the worst outcome: BROKEN, counted apart from one exact both ways)
        $verdict = if ($null -eq $b) { if ($null -eq $a) { 'exact before' } else { 'BROKEN' } } elseif ($null -eq $a) { 'put right' }
                   elseif ($a.Found -gt $b.Found) { 'nearer' } elseif ($a.Found -lt $b.Found) { 'further' } else { 'the same' }
        $tally[$verdict]++
        $t | Add-Member -NotePropertyName outcome -NotePropertyValue $verdict
    }
    ''
    "bars put right ({0}), against the reading with --no-fix: {1}" -f @($fixed).Count, (($tally.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ')
    if ($BySet) { $fixed | Where-Object { $_.outcome -in 'further', 'BROKEN', 'the same' } | Format-Table set, key, outcome -AutoSize | Out-String -Width 120 }
}
