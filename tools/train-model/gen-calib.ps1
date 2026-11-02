param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The calibration sets (S53): pieces no detector learnt from and no check reads (seeds 101-105, past
# every training set's), for calibrate.py to measure how often each class's score is right. Every
# suite with most of what the detector reads on it (dynamics, articulations, grace notes, ornaments,
# tuplets, repeats, octave lines), in the four music fonts with their own engraving, clean, at scale 3
# and printed-and-scanned; and the band suite's, clean and scanned. Then:
#   python dataset.py cal.npz out/cal out/cal-s3 out/cal-scan out/cal-band out/cal-band-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$all = @('--vary', '--marks', '--artic', '--graces', '--ornaments', '--tuplets', '--repeats', '--ottava')
$sets = @(
    @{ Name = 'cal';           Suite = 'all';  Count = 4;  Bars = 16; Seed = 101; Scale = 5.6; Degrade = $false; GenArgs = $all }
    @{ Name = 'cal-s3';        Suite = 'all';  Count = 4;  Bars = 16; Seed = 102; Scale = 3;   Degrade = $false; GenArgs = $all }
    @{ Name = 'cal-scan';      Suite = 'all';  Count = 4;  Bars = 16; Seed = 103; Scale = 5.6; Degrade = $true;  GenArgs = $all }
    @{ Name = 'cal-band';      Suite = 'band'; Count = 16; Bars = 8;  Seed = 104; Scale = 5.6; Degrade = $false; GenArgs = @('--vary') }
    @{ Name = 'cal-band-scan'; Suite = 'band'; Count = 16; Bars = 8;  Seed = 105; Scale = 5.6; Degrade = $true;  GenArgs = @('--vary') }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $a = @{ Out = "$Out\$($s.Name)"; Suite = $s.Suite; Count = $s.Count; Bars = $s.Bars; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = $s.GenArgs }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
