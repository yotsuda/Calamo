param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for dynamics, hairpins and the pedal (S14, detector24): generated pieces with
# OmrGen --marks, each drawn in one of the four music fonts with its own engraving (--vary), clean
# at the usual resolution, at scale 3 (about 160 dpi), and printed-and-scanned (random degradation).
# Every suite for the dynamics and hairpins on all kinds of music, and the piano suite again for
# the pedal (only a grand staff's left hand has one). The pieces are read back with --export-boxes
# (run-gen.ps1), which writes the boxes dataset.py turns into the npz:
#   python dataset.py m4.npz out/m4-train out/m4-train-s3 out/m4-train-scan out/m4-piano out/m4-piano-s3 out/m4-piano-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'm4-train';       Suite = 'all';   Count = 20; Seed = 41; Scale = 5.6; Degrade = $false }
    @{ Name = 'm4-train-s3';    Suite = 'all';   Count = 20; Seed = 42; Scale = 3;   Degrade = $false }
    @{ Name = 'm4-train-scan';  Suite = 'all';   Count = 20; Seed = 43; Scale = 5.6; Degrade = $true }
    @{ Name = 'm4-piano';       Suite = 'piano'; Count = 80; Seed = 44; Scale = 5.6; Degrade = $false }
    @{ Name = 'm4-piano-s3';    Suite = 'piano'; Count = 60; Seed = 45; Scale = 3;   Degrade = $false }
    @{ Name = 'm4-piano-scan';  Suite = 'piano'; Count = 80; Seed = 46; Scale = 5.6; Degrade = $true }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $a = @{ Out = "$Out\$($s.Name)"; Suite = $s.Suite; Count = $s.Count; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--marks', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
