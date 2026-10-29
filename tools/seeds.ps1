param(
    [string]$SeedRoot = 'C:\MyProj\omr-seeds',   # the owner's seeds (Lily#'s Report Misread to OMR), outside every repository
    [switch]$Mark                            # write each seed's result into its omr-triage.json (lastRead)
)
# The seeds the owner reported (docs/repro/lilysharp-requests-2026-10-29-seed-from-fixed-bars.md): each
# reported bar read again by this build and compared with the bar the owner put right. An evaluation of
# real scans only: never training data, never in a release (the folder lives on the owner's machine).
# Lily# writes a seed's folder and never touches it again; this side writes one file of its own beside
# it, omr-triage.json: what was found (cause, note, commit, by hand) and, with -Mark, the last reading.
# A folder with no omr-triage.json is one nobody here has looked at yet.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$omr = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
$work = "$root\out\seeds"
$commit = (git -C $root rev-parse --short HEAD)

# (a bar as text, compared: its todos, its line break and the spaces left out)
function Norm([string]$bar) {
    if ($null -eq $bar) { return $null }
    $t = [regex]::Replace($bar, '@todo\((?:[^()"]|"(?:[^"\\]|\\.)*")*\)', '')
    $t = [regex]::Replace($t, '\|\s*break\s*$', '|')
    return ([regex]::Replace($t, '\s+', ' ')).Trim()
}

$seeds = Get-ChildItem $SeedRoot -Directory | Where-Object { Test-Path "$($_.FullName)\seed.json" } | Sort-Object Name
$read = @{}      # page image's hash -> the reading's folder
$rows = foreach ($dir in $seeds) {
    $s = Get-Content "$($dir.FullName)\seed.json" -Raw | ConvertFrom-Json
    $triagePath = "$($dir.FullName)\omr-triage.json"
    $triage = if (Test-Path $triagePath) { Get-Content $triagePath -Raw | ConvertFrom-Json } else { $null }
    $now = $null; $right = $null
    if ($s.scan -and $s.scan.imageCopy -and (Test-Path "$($dir.FullName)\$($s.scan.imageCopy)")) {
        $img = "$($dir.FullName)\$($s.scan.imageCopy)"
        $hash = (Get-FileHash $img -Algorithm SHA256).Hash.Substring(0, 16)
        if (-not $read.ContainsKey($hash)) {
            $out = "$work\$hash"
            if (Test-Path $out) { Remove-Item $out -Recurse -Force }
            cmd /d /s /c "`"$omr`" read `"$img`" --out `"$out`" < NUL > NUL 2>&1"
            $read[$hash] = $out
        }
        $out = $read[$hash]
        $side = Get-ChildItem $out -Filter *.omr.json | Select-Object -First 1
        if ($side) {
            $j = Get-Content $side.FullName -Raw | ConvertFrom-Json
            # (the bar the seed names: its measure and part in this reading; the page and its box when the numbers moved)
            $m = $j.measures | Where-Object { $_.measure -eq $s.measure -and $_.part -eq $s.part } | Select-Object -First 1
            if ($m) {
                $lines = Get-Content "$out\$($j.lys)"
                $now = $lines[$m.line - 1]
            }
        }
        # (a draft only partly put right, or none: no bar to compare with)
        if ($now -and $s.correct -and -not $s.partial) { $right = (Norm $now) -eq (Norm $s.correct) }
    }
    if ($Mark) {
        $t = if ($triage) { $triage } else { [pscustomobject]@{ cause = $null; note = $null; commit = $null } }
        $t | Add-Member -Force -NotePropertyName lastRead -NotePropertyValue ([ordered]@{ date = (Get-Date -Format 'yyyy-MM-dd'); commit = $commit; right = $right; reading = (Norm $now) })
        $t | ConvertTo-Json -Depth 4 | Set-Content $triagePath -Encoding utf8
    }
    [pscustomobject]@{
        Seed    = $dir.Name
        Seen    = if ($triage -and $triage.cause) { $triage.cause } else { '-' }
        Now     = if ($null -eq $right) { '?' } elseif ($right) { 'right' } else { 'WRONG' }
        Reading = Norm $now
        Correct = Norm $s.correct
    }
}
$rows | Format-Table -AutoSize -Wrap
$n = @($rows | Where-Object Now -eq 'right').Count
"seeds: $n of $(@($rows).Count) read right now ($commit)"
