param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for the other tuplet numbers (detector26): the tuplets suite with OmrGen
# --tuplets (half its groups quintuplets, sextuplets and septuplets of sixteenths), each piece
# drawn in one of the four music fonts with its own engraving (--vary), clean at the usual
# resolution, at scale 3 (about 160 dpi), and printed-and-scanned (random degradation). Read back
# with --export-boxes (run-gen.ps1), which writes the boxes dataset.py turns into the npz:
#   python dataset.py tup.npz out/train-tuplets out/train-tuplets-s3 out/train-tuplets-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-tuplets';      Seed = 2; Scale = 5.6; Degrade = $false }
    @{ Name = 'train-tuplets-s3';   Seed = 3; Scale = 3;   Degrade = $false }
    @{ Name = 'train-tuplets-scan'; Seed = 4; Scale = 5.6; Degrade = $true }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (200 pieces of 16 bars: about 1,700 numbers a set, a third of them 5, 6 and 7 -- 150 of 8 bars
    # gave 315, too few for a class the detector must read by value)
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'tuplets'; Count = 200; Bars = 16; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--tuplets', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
