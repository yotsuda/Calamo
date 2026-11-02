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
# The train-ottava-w* sets (detector47) are the same with every line drawn as an old print's wave with
# no hook (--ottava-wave 1, Lily#'s layout { ottavaLine wave }, REQUESTS 17): kinkel7-1's scan, bar 21,
# whose "8va" beside a wave detector42, taught only dashes, saw at 0.36 and as an 8vb. Beside ott-l's
# dashed sets, half the lines learnt are waves:
#   python dataset.py ottw.npz out/train-ottava-w out/train-ottava-w-s3 out/train-ottava-w-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-ottava';         Seed = 2; Scale = 5.6; Degrade = $false }
    @{ Name = 'train-ottava-s3';      Seed = 3; Scale = 3;   Degrade = $false }
    @{ Name = 'train-ottava-scan';    Seed = 4; Scale = 5.6; Degrade = $true }
    @{ Name = 'train-ottava-15';      Seed = 5; Scale = 5.6; Degrade = $false; Share15 = 0.75 }
    @{ Name = 'train-ottava-15-s3';   Seed = 6; Scale = 3;   Degrade = $false; Share15 = 0.75 }
    @{ Name = 'train-ottava-15-scan'; Seed = 8; Scale = 5.6; Degrade = $true;  Share15 = 0.75 }
    @{ Name = 'train-ottava-w';       Seed = 12; Scale = 5.6; Degrade = $false; Wave = 1 }
    @{ Name = 'train-ottava-w-s3';    Seed = 13; Scale = 3;   Degrade = $false; Wave = 1 }
    @{ Name = 'train-ottava-w-scan';  Seed = 14; Scale = 5.6; Degrade = $true;  Wave = 1 }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (60 pieces of 16 bars a suite, four suites: about 750 lines a set, a fifth of them 15ma or 15mb)
    $genArgs = @('--ottava', '--vary')
    if ($s.Share15) { $genArgs += @('--ottava-15', ('{0}' -f $s.Share15)) }
    if ($s.Wave) { $genArgs += @('--ottava-wave', ('{0}' -f $s.Wave)) }
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,piano,chords'; Count = 60; Bars = 16; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = $genArgs }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
