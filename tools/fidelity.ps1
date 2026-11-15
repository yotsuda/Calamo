param(
    # generated sets (folders under out\) to set against the real pages; each is copied to out\fidelity\<set>
    # and read there, so check's own folders are left as check left them
    [string[]]$Sets = @('gen-scan', 'gen-scan-fill', 'gen-mix-scan', 'gen-lied-all-scan', 'gen-s25-scan'),
    [switch]$Read,                       # (re)read the copies; without it the last reading of each copy is counted
    [string[]]$OmrArgs = @('--detector'),   # as the real sets are read: with the detector (data\detector.onnx)
    [int]$Top = 5                        # the real pages' kinds of error the S65 goal looks at
)
# S65 (ROADMAP section 0, "the main line's order" 2): how far the generated sets bring out the real pages'
# kinds of error. The real side is docs\dev-sets.tsv and docs\eval-sets.tsv (the c_* columns: Scorer.CauseNames,
# events in part-bars read wrong), the generated side each piece's omr\causes.txt, written by the same Scorer.
# Both are put per 1,000 notes of the truth. Numbers only: no page of the held-out set is looked at.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$causes = 'lost', 'missed', 'extra', 'extraMeasure', 'value', 'tuplet', 'key', 'accidental', 'octave', 'clef', 'pitch', 'voice'
$fid = "$root\out\fidelity"

function Real($path, $filter) {
    $rows = Import-Csv $path -Delimiter "`t" | Where-Object { $_.truth -and (& $filter $_) }
    $c = [ordered]@{}; foreach ($k in $causes) { $c[$k] = ($rows | ForEach-Object { [int]("0" + $_."c_$k") } | Measure-Object -Sum).Sum }
    [pscustomobject]@{ Truth = ($rows | Measure-Object truth -Sum).Sum; Causes = $c; Count = @($rows).Count }
}

function Generated($dir) {
    $c = [ordered]@{}; foreach ($k in $causes) { $c[$k] = 0 }
    foreach ($f in Get-ChildItem $dir -Recurse -Filter causes.txt) {
        foreach ($line in Get-Content $f.FullName) {
            # "  13 rh    missed 1, extra 7, value 2"
            foreach ($m in [regex]::Matches($line, '([A-Za-z]+) (\d+)')) { if ($c.Contains($m.Groups[1].Value)) { $c[$m.Groups[1].Value] += [int]$m.Groups[2].Value } }
        }
    }
    $truth = (Import-Csv "$dir\results.csv" | Measure-Object truth -Sum).Sum
    [pscustomobject]@{ Truth = $truth; Causes = $c; Count = @(Import-Csv "$dir\results.csv").Count }
}

$columns = [ordered]@{}
$columns['dev'] = Real "$root\docs\dev-sets.tsv" { $true }
$columns['eval-scan'] = Real "$root\docs\eval-sets.tsv" { param($r) $r.set -match 'scan' }
$columns['eval-clean'] = Real "$root\docs\eval-sets.tsv" { param($r) $r.set -notmatch 'scan' }
foreach ($s in $Sets) {
    $src = "$root\out\$s"; $dst = "$fid\$s"
    if (-not (Test-Path "$src\batch.txt")) { Write-Warning "${s}: no set at $src"; continue }
    if ($Read -or -not (Test-Path "$dst\results.csv")) {
        # (the pages, scores and truth only: no reading of check's comes along)
        robocopy $src $dst /MIR /XD omr /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "${s}: robocopy failed ($LASTEXITCODE)" }
        $global:LASTEXITCODE = 0
        & "$PSScriptRoot\run-gen.ps1" -Out $dst -Reuse -OmrArgs $OmrArgs | Out-Null
    }
    $columns[$s] = Generated $dst
}

function Rate($col, $k) { if ($col.Truth) { 1000.0 * $col.Causes[$k] / $col.Truth } else { 0 } }
"per 1,000 notes of the truth (pieces / notes under each name)"
$head = foreach ($n in $columns.Keys) { '{0} ({1}/{2})' -f $n, $columns[$n].Count, $columns[$n].Truth }
$head -join ' | '
$table = foreach ($k in $causes) {
    $r = [ordered]@{ cause = $k }
    foreach ($n in $columns.Keys) { $r[$n] = '{0:F1}' -f (Rate $columns[$n] $k) }
    [pscustomobject]$r
}
$table | Format-Table -AutoSize | Out-String -Width 250

# the same as shares of each column's errors: a set can pass the rate by being broken another way (a low
# resolution's bars lost whole) and still not err as the real pages do
"shares of each column's errors (%), and the column's errors per 1,000 notes"
$share = foreach ($k in $causes) {
    $r = [ordered]@{ cause = $k }
    foreach ($n in $columns.Keys) { $all = ($columns[$n].Causes.Values | Measure-Object -Sum).Sum; $r[$n] = if ($all) { '{0:F0}' -f (100.0 * $columns[$n].Causes[$k] / $all) } else { '-' } }
    [pscustomobject]$r
}
$tot = [ordered]@{ cause = '(per 1,000)' }
foreach ($n in $columns.Keys) { $tot[$n] = '{0:F1}' -f $(if ($columns[$n].Truth) { 1000.0 * ($columns[$n].Causes.Values | Measure-Object -Sum).Sum / $columns[$n].Truth } else { 0 }) }
@($share) + [pscustomobject]$tot | Format-Table -AutoSize | Out-String -Width 250

# the goal (ROADMAP S65): each of the real pages' top kinds shows in some generated set at half its real rate or more
$gens = @($columns.Keys | Where-Object { $_ -notin 'dev', 'eval-scan', 'eval-clean' })
$realAll = [pscustomobject]@{ Truth = $columns['dev'].Truth + $columns['eval-scan'].Truth + $columns['eval-clean'].Truth; Causes = [ordered]@{} }
foreach ($k in $causes) { $realAll.Causes[$k] = $columns['dev'].Causes[$k] + $columns['eval-scan'].Causes[$k] + $columns['eval-clean'].Causes[$k] }
"the real pages' top $Top kinds (dev + eval) and the generated set that comes nearest"
foreach ($k in ($causes | Sort-Object { - (Rate $realAll $_) } | Select-Object -First $Top)) {
    $real = Rate $realAll $k
    $best = $gens | Sort-Object { - (Rate $columns[$_] $k) } | Select-Object -First 1
    $g = if ($best) { Rate $columns[$best] $k } else { 0 }
    '{0,-13} real {1,6:F1}   best {2,-20} {3,6:F1}   {4}' -f $k, $real, $best, $g, $(if ($g -ge $real / 2) { 'ok' } else { 'short ({0:P0} of real)' -f ($(if ($real) { $g / $real } else { 0 })) })
}
