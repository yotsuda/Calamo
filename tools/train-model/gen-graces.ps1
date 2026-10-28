param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for the grace notes (detector30, S21): the basic, ledger, piano and chords suites
# with OmrGen --graces (acciaccaturas, appoggiaturas, and two or three sixteenths before the notes,
# now and then with a sign of their own), each piece drawn in one of the four music fonts with its own
# engraving (--vary), clean at the usual resolution, at scale 3 (about 160 dpi), and printed-and-scanned
# (random degradation). Read back with --export-boxes (run-gen.ps1), which writes the boxes (grace.head,
# grace.slash, the small signs as signs, the grace stems' tips as stem.N) dataset.py turns into the npz:
#   python dataset.py grace.npz out/train-grace out/train-grace-s3 out/train-grace-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-grace';      Seed = 2; Scale = 5.6; Degrade = $false }
    @{ Name = 'train-grace-s3';   Seed = 3; Scale = 3;   Degrade = $false }
    @{ Name = 'train-grace-scan'; Seed = 4; Scale = 5.6; Degrade = $true }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (60 pieces of 16 bars a suite, four suites: a grace note or a group before six notes in a hundred)
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,piano,chords'; Count = 60; Bars = 16; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--graces', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
