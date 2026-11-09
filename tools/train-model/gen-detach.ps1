param(
    [string]$Out = "$PSScriptRoot\..\..\out",
    [string[]]$Only = @()      # make only these sets (by name)
)
# The training sets for slurs that start high on the upper staff and end on notes drawn on the staff below
# (S58, Schumann's Op.25-15): the threes suite with OmrGen --detach (a line of eighths falling to the left hand's
# staff in a beam of its own under the slur), each piece drawn in one of the four music fonts with its own
# engraving (--vary), clean at the usual resolution, at scale 3 (about 160 dpi), and printed-and-scanned
# (random degradation). Held-out pieces are seed 7. Read back with --export-boxes (run-gen.ps1):
#   python dataset.py detach.npz out/train-detach out/train-detach-s3 out/train-detach-scan
$ErrorActionPreference = 'Stop'
$fonts = 'Emmentaler,Bravura,Petaluma,Leland'
$sets = @(
    @{ Name = 'train-detach';      Seed = 57; Scale = 5.6; Degrade = $false; Count = 80 }
    @{ Name = 'train-detach-s3';   Seed = 58; Scale = 3;   Degrade = $false; Count = 80 }
    @{ Name = 'train-detach-scan'; Seed = 59; Scale = 5.6; Degrade = $true;  Count = 80 }
)
foreach ($s in $sets) {
    if ($Only.Count -and $s.Name -notin $Only) { continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $a = @{ Out = "$Out\$($s.Name)"; Suite = 'threes'; Count = $s.Count; Bars = 12; Seed = $s.Seed; Scale = $s.Scale; Fonts = $fonts; GenArgs = @('--detach', '--vary') }
    if ($s.Degrade) { $a.Degrade = $true; $a.DegradeArgs = 'random' }
    & "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
    "{0}: {1:F0} s" -f $s.Name, $sw.Elapsed.TotalSeconds
}
