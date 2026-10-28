param(
    [ValidateSet('eval', 'dev')][string[]]$Kinds = @('eval', 'dev'),
    [string[]]$Only = @(),                  # these sets only
    [string[]]$Skip = @()                   # all but these
)
# How well the todos point at the part-bars the reader got wrong (the owner's comparison with other
# OMRs, 2026-10-26, section 2.6: C:\MyProj\omr-compare\scripts\todo-quality.ps1, made to read this
# repository's runs). Run tools/run-audiveris.ps1 -Eval (or -Dev) -Verify first: per set,
# out\audiveris\<set>\scored-own\ holds diffs.txt (the part-bars the truth scores wrong),
# todos-truth.txt (each todo with the part-bar it covers) and run.txt (the reading's output: every
# part-bar scored, "staff-bars N", and whether the redraw check ran). Marked = a part-bar a todo covers.
#   recall      wrong part-bars that carry a mark
#   precision   marked part-bars that are wrong
#   clean       unmarked part-bars that are right (how far "no mark" can be trusted)
#   load        marked part-bars, share of all (what the user is asked to look at)
# "written" counts the todos the .lys carries (under the 0.8 threshold), "all" the quiet ones too.
$root = Resolve-Path "$PSScriptRoot\.."
function Stats($rows, $label) {
    if (@($rows).Count -eq 0) { return }
    foreach ($mode in 'written', 'all') {
        $T = 0; $W = 0; $M = 0; $WM = 0
        foreach ($r in $rows) {
            # (not "if ... { } else { }" as a value: an empty set unrolls to nothing)
            $marked = $r.All; if ($mode -eq 'written') { $marked = $r.Written }
            $T += $r.Total; $W += $r.Wrong.Count; $M += $marked.Count
            $WM += @($r.Wrong | Where-Object { $marked.Contains($_) }).Count
        }
        $unW = $W - $WM; $unAll = $T - $M
        '{0,-22} {1,-7} part-bars {2,6}  wrong {3,5}  marked {4,5} (load {5,5:F1}%)  recall {6,5:F1}%  precision {7,5:F1}%  clean {8,5:F1}%' -f `
            $label, $mode, $T, $W, $M, (100.0 * $M / $T), (100.0 * $WM / [math]::Max(1, $W)), (100.0 * $WM / [math]::Max(1, $M)), (100.0 * ($unAll - $unW) / [math]::Max(1, $unAll))
    }
}
foreach ($kind in $Kinds) {
    $rows = foreach ($d in Get-ChildItem "$root\out\audiveris" -Directory) {
        $set = $d.Name
        $isDev = $set.StartsWith('dev-')
        if (($kind -eq 'dev') -ne $isDev -or $set -notmatch '^(dev-|dl-|lied-|chopin|burgmuller|clementi|schumann|tchaikovsky)') { continue }
        if (($Only.Count -gt 0 -and $Only -notcontains $set) -or $Skip -contains $set) { continue }
        $own = "$($d.FullName)\scored-own"
        if (-not (Test-Path "$own\run.txt") -or -not (Test-Path "$own\todos-truth.txt")) { continue }
        $txt = Get-Content -Raw "$own\run.txt"
        if ($txt -notmatch 'staff-bars (\d+), exact') { continue }
        $total = [int]$Matches[1]
        $wrong = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($l in Get-Content "$own\diffs.txt" -ErrorAction SilentlyContinue) { if ($l -match '^\s*(\d+)\s+(\S+)\s+T:') { [void]$wrong.Add("$($Matches[1])/$($Matches[2])") } }
        $all = [System.Collections.Generic.HashSet[string]]::new(); $written = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($l in Get-Content "$own\todos-truth.txt") {
            $c = $l -split "`t"
            if ($c[0] -ne 'todo' -or $c[9] -eq 'extra' -or $c[9] -eq 'unscored') { continue }
            $k = "$($c[5])/$($c[6])"
            [void]$all.Add($k); if ($c[8] -eq 'written') { [void]$written.Add($k) }
        }
        [pscustomobject]@{
            Set = $set; Total = $total; Wrong = $wrong; All = $all; Written = $written
            Checked = $txt -match 'verify: input ='; Stopped = $txt -match 'verify: lysc drew nothing'
        }
    }
    $checked = @($rows | Where-Object Checked).Count; $stopped = @($rows | Where-Object Stopped).Count
    "$kind`: $(@($rows).Count) sets, the redraw check ran on $checked, stopped on $stopped" + $(if ($stopped -gt 0) { " (" + (($rows | Where-Object Stopped | ForEach-Object { $_.Set }) -join ', ') + ")" } else { '' })
    Stats $rows $kind
    Stats @($rows | Where-Object Set -match 'scan') "$kind (scans)"
    Stats @($rows | Where-Object Set -notmatch 'scan') "$kind (engraved)"
    ''
}
