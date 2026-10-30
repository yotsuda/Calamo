param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for cue notes (detector39): the basic, ledger, chords, tuplets and slurs suites with
# OmrGen --cues (a bar of the part's rest with another instrument's line printed small over it, as a
# Sibelius part prints one: the Eroica trumpet part, 2026-10-30), each piece drawn in one of the four
# music fonts with its own engraving (--vary), clean at the usual resolution, at scale 3 (about 160 dpi),
# and printed-and-scanned (random degradation); and the evaluation sets (seed 7). The cue's heads are
# grace_head, its accidentals, dots and stems' tips the usual classes (Lily# draws its rests full size
# still: docs/repro/REQUESTS.md 12).
# Read back with --export-boxes (run-gen.ps1), which writes the boxes dataset.py turns into the npz:
#   python dataset.py cues.npz out/train-cue out/train-cue-s3 out/train-cue-scan
#   python dataset.py eval-cue.npz out/gen-cue; python dataset.py eval-cue-scan.npz out/gen-cue-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-cue';      Seed = 2; Scale = 5.6; Degrade = $false; Count = 50 }
    @{ Name = 'train-cue-s3';   Seed = 3; Scale = 3;   Degrade = $false; Count = 50 }
    @{ Name = 'train-cue-scan'; Seed = 4; Scale = 5.6; Degrade = $true;  Count = 50 }
    @{ Name = 'gen-cue';        Seed = 7; Scale = 5.6; Degrade = $false; Count = 6 }
    @{ Name = 'gen-cue-scan';   Seed = 7; Scale = 5.6; Degrade = $true;  Count = 6 }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (50 pieces of 12 bars a suite, five suites: about a quarter of a piece's bars cues)
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,chords,tuplets,slurs'; Count = $s.Count; Bars = 12; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--cues', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
