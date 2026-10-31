param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @(),     # make only these sets (by name)
    [string]$Lysc = ''         # the lysc that engraves ('' = run-gen.ps1's, Lily#'s Release build)
)
# The training sets for cue notes (detector39, detector40, detector41): the basic, ledger, chords, tuplets
# and slurs suites with OmrGen --cues (a bar of the part's rest with another instrument's line printed small
# over it, as a Sibelius part prints one: the Eroica trumpet part, 2026-10-30), each piece drawn in one of
# the four music fonts with its own engraving (--vary) and its own cue size (layout { cueSize -4 or -3 }:
# 0.63 or 0.71 of full size, Lily#'s REQUESTS.md 13, 2026-10-31; not -2, 0.79, which taught detector41 to
# take an old print's small heads for grace notes), clean at the usual resolution, at
# scale 3 (about 30 px a space), at scale 1.25 (about 12 px a space, a phone's photo of a part: the Eroica
# photo's small heads, 2026-10-31), and printed-and-scanned (random degradation); and the evaluation sets
# (seed 7). The cue's heads are grace_head, its accidentals, dots, rests and stems' tips the usual classes
# (told by cue { } in the .lys, TGlyph.Cue, whatever their size). The training sets hold a quarter of their
# cue bars on one note (--cue-whole: a small whole note read at 0.13, 2026-10-31); the evaluation sets do
# not, as before. Read back with --export-boxes (run-gen.ps1), which writes the boxes dataset.py turns into the npz:
#   python dataset.py cues2.npz out/train-cue out/train-cue-s3 out/train-cue-s125 out/train-cue-scan
#   python dataset.py eval-cue.npz out/gen-cue; python dataset.py eval-cue-scan.npz out/gen-cue-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-cue';      Seed = 2; Scale = 5.6;  Degrade = $false; Count = 50; Train = $true }
    @{ Name = 'train-cue-s3';   Seed = 3; Scale = 3;    Degrade = $false; Count = 50; Train = $true }
    @{ Name = 'train-cue-s125'; Seed = 5; Scale = 1.25; Degrade = $false; Count = 50; Train = $true }
    @{ Name = 'train-cue-scan'; Seed = 4; Scale = 5.6;  Degrade = $true;  Count = 50; Train = $true }
    @{ Name = 'gen-cue';        Seed = 7; Scale = 5.6;  Degrade = $false; Count = 6;  Train = $false }
    @{ Name = 'gen-cue-scan';   Seed = 7; Scale = 5.6;  Degrade = $true;  Count = 6;  Train = $false }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (50 pieces of 12 bars a suite, five suites: about a quarter of a piece's bars cues)
    $gen = @('--cues', '--vary') + $(if ($s.Train) { @('--cue-whole', '0.25') } else { @() })
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,chords,tuplets,slurs'; Count = $s.Count; Bars = 12; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = $gen }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    if ($Lysc) { $a.Lysc = $Lysc }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
