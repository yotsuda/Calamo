param(
    [string]$Out = "$PSScriptRoot\..\out\gen-header",
    [string]$Suite = 'basic,piano,chords,lied',
    [int]$Count = 10,
    [int]$Seed = 7,
    [switch]$Reuse      # read the pieces already engraved in $Out again
)
# The words over the music (Header, Windows' OCR) against what the generator wrote: pieces with a
# title, subtitle, composer, poet and tempo drawn at random (OmrGen --headers), engraved by Lily#,
# read with --header; each field compared as written (the reading's .lys against the piece's).
$ErrorActionPreference = 'Stop'
$gen = @('--headers')
if ($Reuse) { & "$PSScriptRoot\run-gen.ps1" -Out $Out -Reuse -OmrArgs @('--header', '--detector') | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $Out -Suite $Suite -Count $Count -Seed $Seed -GenArgs $gen -OmrArgs @('--header', '--detector') | Out-Null }

function Fields($path) {
    $f = @{}
    foreach ($line in Get-Content $path -Encoding utf8) {
        if ($line -match '^(title|subtitle|composer|poet)\s+"(.*)"\s*$') { $f[$Matches[1]] = $Matches[2] }
        elseif ($line -match '^tempo\s+(.*)$') { $f['tempo'] = $Matches[1].Trim() }
        elseif ($line -match '^(section|part)\b') { break }
    }
    $f
}
$names = 'title', 'subtitle', 'composer', 'poet', 'tempo'
$tally = @{}; foreach ($n in $names) { $tally[$n] = @(0, 0, 0) }   # right, written, read where none was written
$wrong = @()
foreach ($lys in Get-ChildItem $Out -Recurse -Filter *.lys | Where-Object { $_.Directory.Name -ne 'omr' }) {
    $read = Join-Path $lys.DirectoryName 'omr\omr.lys'
    if (-not (Test-Path $read)) { continue }
    $want = Fields $lys.FullName; $got = Fields $read
    foreach ($n in $names) {
        if ($want.ContainsKey($n)) {
            $tally[$n][1]++
            if ($got[$n] -ceq $want[$n]) { $tally[$n][0]++ } else { $wrong += "$($lys.BaseName) ${n}: [$($want[$n])] read [$($got[$n])]" }
        }
        elseif ($got.ContainsKey($n) -and -not ($n -eq 'title')) { $tally[$n][2]++; $wrong += "$($lys.BaseName) ${n}: none, read [$($got[$n])]" }
    }
}
$names | ForEach-Object { [pscustomobject]@{ field = $_; right = $tally[$_][0]; written = $tally[$_][1]; extra = $tally[$_][2] } } | Format-Table -AutoSize | Out-String
$wrong | Set-Content "$Out\header-diffs.txt" -Encoding utf8
"differences: $($wrong.Count) ($Out\header-diffs.txt)"
$wrong | Select-Object -First 25
