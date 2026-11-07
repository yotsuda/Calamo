# The redraw check's flags on the dev sets (run-audiveris.ps1 -Dev -Reuse -Verify first), counted by the kind
# of message in scored-own\verify.txt and by what the truth says of the part-bar (todos-truth.txt: written or
# quiet, right = a false alarm by the note scoring, wrong = it points at a wrong bar). S57: which kinds of
# false alarm to take away first. -Kind ms (engraved) or scan.
param([string]$Kind = 'ms')
Set-Location (Resolve-Path "$PSScriptRoot\..")
$types = @{}; $examples = @{}
foreach ($d in Get-ChildItem out\audiveris -Directory -Filter "dev-*-$Kind") {
    $own = "$($d.FullName)\scored-own"
    $verdict = @{}
    foreach ($l in Get-Content "$own\todos-truth.txt") {
        $c = $l -split "`t"; if ($c[0] -ne 'todo') { continue }
        $verdict["$($c[5])/$($c[6])"] = "$($c[8]) $($c[9])"     # written/quiet right/wrong/extra/unscored
    }
    foreach ($l in Get-Content "$own\verify.txt") {
        if ($l -notmatch '^bar (\d+) (\S+?)(?: \(([^)]*)\))?: (.*?) \| ') { continue }
        $bar = $Matches[1]; $part = $Matches[2]; $msg = $Matches[4]
        $key = "$bar/$part"
        $v = if ($verdict.ContainsKey($key)) { $verdict[$key] } else { '-' }
        if ($msg -match '^reading has (.*); input shows (.*)$') {
            $has = $Matches[1]; $shows = $Matches[2]
            $t = "reading has [$($has -replace '@-?\d+','' -replace 'x\d+','')] input shows [$($shows -replace '@-?\d+','' -replace 'x\d+','')]"
        }
        elseif ($msg -match 'short|long') { $t = 'bar length' }
        else { $t = $msg.Substring(0, [Math]::Min(40, $msg.Length)) }
        $k = "$v | $t"
        if (-not $types.ContainsKey($k)) { $types[$k] = 0; $examples[$k] = "$($d.Name) $key" }
        $types[$k]++
    }
}
$types.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { '{0,4}  {1}   e.g. {2}' -f $_.Value, $_.Key, $examples[$_.Key] }
