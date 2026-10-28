param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for the octave lines (detector27, S18): the basic, ledger, piano and chords suites
# with OmrGen --ottava (8va, 8vb, 15ma and 15mb over stretches of the notes, the notes written as they
# sound), each piece drawn in one of the four music fonts with its own engraving (--vary), clean at the
# usual resolution, at scale 3 (about 160 dpi), and printed-and-scanned (random degradation). Read back
# with --export-boxes (run-gen.ps1), which writes the boxes dataset.py turns into the npz:
#   python dataset.py ott.npz out/train-ottava out/train-ottava-s3 out/train-ottava-scan
# The train-ottava-15* sets (detector29, S18 second step) are the same again with three lines in
# four 15ma or 15mb (--ottava-15 0.75): with a fifth, detector28 had 195 and 235 of them and read
# them at 33-75%, against 95% for 8va and 8vb.
#   python dataset.py ott15.npz out/train-ottava-15 out/train-ottava-15-s3 out/train-ottava-15-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-ottava';         Seed = 2; Scale = 5.6; Degrade = $false }
    @{ Name = 'train-ottava-s3';      Seed = 3; Scale = 3;   Degrade = $false }
    @{ Name = 'train-ottava-scan';    Seed = 4; Scale = 5.6; Degrade = $true }
    @{ Name = 'train-ottava-15';      Seed = 5; Scale = 5.6; Degrade = $false; Share15 = 0.75 }
    @{ Name = 'train-ottava-15-s3';   Seed = 6; Scale = 3;   Degrade = $false; Share15 = 0.75 }
    @{ Name = 'train-ottava-15-scan'; Seed = 8; Scale = 5.6; Degrade = $true;  Share15 = 0.75 }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (60 pieces of 16 bars a suite, four suites: about 750 lines a set, a fifth of them 15ma or 15mb)
    $genArgs = @('--ottava', '--vary')
    if ($s.Share15) { $genArgs += @('--ottava-15', ('{0}' -f $s.Share15)) }
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,piano,chords'; Count = 60; Bars = 16; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = $genArgs }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
