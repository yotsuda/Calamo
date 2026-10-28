param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @(),     # redraw only these sets (by name)
    [switch]$NoNpz             # redraw only; leave the npz as they are
)
# The detector's Lily# training sets drawn again by today's lysc (run-gen.ps1 -Redraw: the same pieces,
# OmrGen not run; the same resolution and the same random degradation, page by page), then their npz
# made again under the names below with a "-l" before ".npz" (the old ones kept, to go back to).
# A set's resolution and degradation by its name, as every gen-*.ps1 makes them: "-s3" at scale 3,
# "-scan" at 5.6 with random degradation, else 5.6. MuseScore's sets (ms-*, rs-*) are not Lily#'s.
# A set whose .lys today's lysc no longer takes is made again instead (OmrGen, the same seed): the
# v4-train-band sets of 2026-10-10 write 'repeat percent', gone from Lily# 0.14 (drawn as its body once,
# no % sign): run-gen.ps1 -Out out\v4-train-band(-s3|-scan) -Suite band -Count 150 -Bars 8 -Seed 2|3|4
# -Scale 5.6|3|5.6 [-Degrade -DegradeArgs random] -Fonts Emmentaler,Bravura,Petaluma,Leland -GenArgs --vary
# So is every set made before 2026-10-28: its .lys has a bare c as C4, and today's lysc (LilyPond's
# octaves, c' middle C) draws it an octave low, away from its .notes (gen-band read 633 of 1452 notes)
$ErrorActionPreference = 'Stop'
$npz = [ordered]@{
    'v4-train12' = 'v4-train', 'v4-train-s3', 'v4-train-meters', 'v4-train-meters-s3', 'v4-train-scan', 'v4-train-scan2', 'v4-train-scan3', 'v4-train-scan4',
                   'v4-train-times', 'v4-train-times-s3', 'v4-train-times-scan', 'v4-train-ties', 'v4-train-ties-s3', 'v4-train-ties-scan',
                   'v4-train-slurs', 'v4-train-slurs-s3', 'v4-train-slurs-scan', 'ms-train', 'ms-train-scan'
    'v4-band'      = 'v4-train-band', 'v4-train-band-s3'
    'v4-band-scan' = , 'v4-train-band-scan'
    'm4'           = 'm4-train', 'm4-train-s3', 'm4-train-scan', 'm4-piano', 'm4-piano-s3', 'm4-piano-scan'
    'm5'           = 'm5-train', 'm5-train-s3', 'm5-train-scan', 'm5-piano', 'm5-piano-scan'
    'grace'        = 'train-grace', 'train-grace-s3', 'train-grace-scan'
    'ott'          = 'train-ottava', 'train-ottava-s3', 'train-ottava-scan'
    'rep'          = 'train-rep', 'train-rep-s3', 'train-rep-scan'
    'orn'          = 'train-orn', 'train-orn-s3', 'train-orn-scan'
    'tup'          = 'train-tuplets', 'train-tuplets-s3', 'train-tuplets-scan'
}
foreach ($set in $npz.Values | ForEach-Object { $_ } | Where-Object { $_ -notmatch '^ms-' }) {
    if ($Only.Count -and $set -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $a = @{ Out = "$Out\$set"; Redraw = $true; Scale = $(if ($set -match '-s3$') { 3 } else { 5.6 }) }
    if ($set -match '-scan\d*$') { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $set, $sw.Elapsed.TotalSeconds
}
if ($NoNpz -or $Only.Count) { return }
$py = "$PSScriptRoot\.venv\Scripts\python.exe"
foreach ($name in $npz.Keys) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $py "$PSScriptRoot\dataset.py" "$PSScriptRoot\$name-l.npz" @($npz[$name] | ForEach-Object { "$Out\$_" }) | Select-Object -Last 1
    "{0}-l.npz: {1:F0} s" -f $name, $sw.Elapsed.TotalSeconds
}
