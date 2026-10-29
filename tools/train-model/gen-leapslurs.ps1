param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for the hooked two-note slurs (detector38): the basic, ledger, piano, chords and
# voices2 suites with OmrGen --leapslurs (a slur from an eighth or sixteenth over a leap of a fourth to
# an octave, which Lily# draws as the short hook old prints give two-note slurs), each piece drawn in
# one of the four music fonts with its own engraving (--vary), clean at the usual resolution, at scale
# 3 (about 160 dpi), and printed-and-scanned (random degradation); and the evaluation sets (seed 7).
# Read back with --export-boxes (run-gen.ps1), which writes the boxes dataset.py turns into the npz:
#   python dataset.py ls.npz out/train-ls out/train-ls-s3 out/train-ls-scan
#   python dataset.py eval-ls.npz out/gen-ls; python dataset.py eval-ls-scan.npz out/gen-ls-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-ls';      Seed = 2; Scale = 5.6; Degrade = $false; Count = 50 }
    @{ Name = 'train-ls-s3';   Seed = 3; Scale = 3;   Degrade = $false; Count = 50 }
    @{ Name = 'train-ls-scan'; Seed = 4; Scale = 5.6; Degrade = $true;  Count = 50 }
    @{ Name = 'gen-ls';        Seed = 7; Scale = 5.6; Degrade = $false; Count = 6 }
    @{ Name = 'gen-ls-scan';   Seed = 7; Scale = 5.6; Degrade = $true;  Count = 6 }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # (50 pieces of 12 bars a suite, five suites: some fifteen hooked slurs a piece)
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'basic,ledger,piano,chords,voices2'; Count = $s.Count; Bars = 12; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--leapslurs', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
