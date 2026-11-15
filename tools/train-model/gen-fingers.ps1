param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for fingerings (ROADMAP section 0's (B), 2026-11-15): every suite with OmrGen --fingers
# (runs of digits 1-5 over single notes; the music the same as without), each piece drawn in one of the four
# music fonts with its own engraving (--vary), clean at the usual resolution, at scale 3 (about 160 dpi), and
# printed-and-scanned (random degradation). The detector met no fingering before and took its digits for
# tuplet numbers (gen-fing). Held-out pieces are seed 7 (check's gen-fing). Read back with --export-boxes
# (run-gen.ps1), whose boxes have finger.N:
#   python dataset.py fingers.npz out/train-fingers out/train-fingers-s3 out/train-fingers-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-fingers';      Seed = 60; Scale = 5.6; Degrade = $false }
    @{ Name = 'train-fingers-s3';   Seed = 61; Scale = 3;   Degrade = $false }
    @{ Name = 'train-fingers-scan'; Seed = 62; Scale = 5.6; Degrade = $true }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'all'; Count = 9; Bars = 12; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--fingers', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
