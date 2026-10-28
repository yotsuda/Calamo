param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for the ornaments (detector32, S23): the basic, ledger, piano, chords and voices2
# suites with OmrGen --ornaments (a trill, mordent, prall, turn or reverse turn over a note in ten), each
# piece drawn in one of the four music fonts with its own engraving (--vary), clean at the usual
# resolution, at scale 3 (about 160 dpi), and printed-and-scanned (random degradation). Read back with
# --export-boxes (run-gen.ps1), which writes the boxes (scripts.trill, scripts.mordent, scripts.prall,
# scripts.turn, scripts.reverseturn) dataset.py turns into the npz:
#   python dataset.py orn.npz out/train-orn out/train-orn-s3 out/train-orn-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-orn';      Seed = 2; Scale = 5.6; Degrade = $false }
    @{ Name = 'train-orn-s3';   Seed = 3; Scale = 3;   Degrade = $false }
    @{ Name = 'train-orn-scan'; Seed = 4; Scale = 5.6; Degrade = $true }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (50 pieces of 12 bars a suite, five suites: six to ten ornaments a piece)
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,piano,chords,voices2'; Count = 50; Bars = 12; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--ornaments', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
