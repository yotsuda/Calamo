param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for articulations (S15, detector25): generated pieces with OmrGen --artic, and
# --marks too (an accent stands among hairpins and dynamics, a staccato among the pedal's marks),
# each drawn in one of the four music fonts with its own engraving (--vary), clean at the usual
# resolution, at scale 3 (about 160 dpi), and printed-and-scanned (random degradation). Every suite,
# and the piano suite again (two staves' marks between them). Read back with --export-boxes
# (run-gen.ps1), which writes the boxes dataset.py turns into the npz:
#   python dataset.py m5.npz out/m5-train out/m5-train-s3 out/m5-train-scan out/m5-piano out/m5-piano-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'm5-train';       Suite = 'all';   Count = 20; Seed = 51; Scale = 5.6; Degrade = $false }
    @{ Name = 'm5-train-s3';    Suite = 'all';   Count = 20; Seed = 52; Scale = 3;   Degrade = $false }
    @{ Name = 'm5-train-scan';  Suite = 'all';   Count = 20; Seed = 53; Scale = 5.6; Degrade = $true }
    @{ Name = 'm5-piano';       Suite = 'piano'; Count = 40; Seed = 54; Scale = 5.6; Degrade = $false }
    @{ Name = 'm5-piano-scan';  Suite = 'piano'; Count = 40; Seed = 56; Scale = 5.6; Degrade = $true }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $a = @{ Out = "$Out\$($s.Name)"; Suite = $s.Suite; Count = $s.Count; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--artic', '--marks', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
