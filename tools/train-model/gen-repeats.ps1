param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for the form's signs (detector31, S22): the basic, ledger, piano, chords and voices2
# suites with OmrGen --repeats (a stretch played twice between |: and :|, with a first and second ending
# under volta brackets, or a jump with the segno and coda signs), each piece drawn in one of the four
# music fonts with its own engraving (--vary), clean at the usual resolution, at scale 3 (about 160 dpi),
# and printed-and-scanned (random degradation). Read back with --export-boxes (run-gen.ps1), which writes
# the boxes (rep.open, rep.close, volta.l, volta.r, scripts.segno, scripts.coda) dataset.py turns into the npz:
#   python dataset.py rep.npz out/train-rep out/train-rep-s3 out/train-rep-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-rep';      Seed = 2; Scale = 5.6; Degrade = $false }
    @{ Name = 'train-rep-s3';   Seed = 3; Scale = 3;   Degrade = $false }
    @{ Name = 'train-rep-scan'; Seed = 4; Scale = 5.6; Degrade = $true }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (50 pieces of 12 bars a suite, five suites: one repeat or jump a piece, two to four of its signs)
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,piano,chords,voices2'; Count = 50; Bars = 12; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--repeats', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
