param(
    [string]$Out = "$PSScriptRoot\..\..\out"
)
# The training set for an old print's swollen black heads (ROADMAP section 0's S65 -> S60's retraining,
# 2026-11-17): every suite with a share of the chords three or four notes with a second inside
# (OmrGen --clusters 0.3), each piece in one of the four music fonts with its own engraving (--vary),
# printed-and-scanned (random degradation), and on 0.3 of the pages the black heads swollen into each other
# (run-gen.ps1 -SwellShare; S65's gen-clusters-bheads12 reproduced the real scans' missed seconds that way).
# 0.3, not all: -SwellShare 1 on random pages took the detector to 86-95% (S60 (c)), far worse than any real scan.
# Fingerings too (--fingers), so that the set teaches no page without them. Read back with --export-boxes:
#   python dataset.py swell.npz out/train-swell-scan
$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
$a = @{ Out = "$Out\train-swell-scan"; Suite = 'all'; Count = 9; Bars = 12; Seed = 63; Scale = 5.6
        Fonts = 'Emmentaler,Bravura,Petaluma,Leland'; GenArgs = @('--fingers', '--vary', '--clusters', '0.3')
        Degrade = $true; DegradeArgs = 'random'; SwellShare = 0.3 }
& "$PSScriptRoot\..\run-gen.ps1" @a | Out-Null
"train-swell-scan: {0:F0} s" -f $sw.Elapsed.TotalSeconds
