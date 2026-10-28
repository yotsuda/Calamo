param(
    [int]$Count = 10,
    [int]$MeterCount = 40,  # extra pieces of the meters suite: a time signature is one sample a piece
    [int]$BandCount = 150,  # pieces of the band suite: the letters in the rehearsal marks' boxes (A-Z over the pieces; every third piece numbers them 1-9)
    [int]$Seed = 1,         # the training set; the evaluation set (check.ps1) is seed 7
    [double[]]$Scales = @(5.6, 3),  # engraving scales: shapes differ with the resolution
    [switch]$GatherOnly     # the sets as they are (out/train*): only gather the samples and rebuild
)
# Rebuild data/glyph-train.tsv: generate and engrave a training set, read it back, and gather every
# sample SymEval labelled from Lily#'s SVG. Rebuild omr afterwards (it copies the file).
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$dirs = @()
$letterDirs = @()
foreach ($sc in $Scales) {
    $tag = if ($sc -eq 5.6) { '' } else { "-s$sc" }
    if (-not $GatherOnly) {
        # (--mmrests: the same music with stretches of multi-bar rests, whose counts give the digit classifier its 0s, in 10 and 20)
        & "$PSScriptRoot\run-gen.ps1" -Out "$root\out\train$tag" -Count $Count -Seed $Seed -Scale $sc -GenArgs '--mmrests' | Out-Null
        & "$PSScriptRoot\run-gen.ps1" -Out "$root\out\train-meters$tag" -Suite meters -Count $MeterCount -Seed $Seed -Scale $sc | Out-Null
        # (band charts of 24 bars: three to six boxed letters or figures a piece, the "l:" family)
        & "$PSScriptRoot\run-gen.ps1" -Out "$root\out\train-letters$tag" -Suite band -Count $BandCount -Seed $Seed -Scale $sc -Bars 24 | Out-Null
    }
    $dirs += "$root\out\train$tag", "$root\out\train-meters$tag"
    $letterDirs += "$root\out\train-letters$tag"
}
$rows = Get-ChildItem $dirs -Recurse -Filter glyph-samples.tsv | Sort-Object FullName | ForEach-Object { [IO.File]::ReadAllLines($_.FullName) }
# (the band charts give the letters only: their staves of every kind would flood the other families with
# tens of thousands of "none"s, and the time signatures of the degraded set were read worse for it)
$rows += Get-ChildItem $letterDirs -Recurse -Filter glyph-samples.tsv | Sort-Object FullName | ForEach-Object { [IO.File]::ReadAllLines($_.FullName) } | Where-Object { $_.StartsWith('l:') }
$head = "# label, then GlyphKnn features. From tools/train.ps1 -Seed $Seed -Count $Count -MeterCount $MeterCount -BandCount $BandCount -Scales $($Scales -join ',') (lysc-engraved generated scores)."
[IO.File]::WriteAllLines("$root\data\glyph-train.tsv", @($head) + $rows)
"$($rows.Count) samples -> data/glyph-train.tsv"
$rows | ForEach-Object { ($_ -split "`t")[0] } | Group-Object | Sort-Object Name | ForEach-Object { "{0,6} {1}" -f $_.Count, $_.Name }
cmd /d /s /c "dotnet build `"$root\src\Omr`" -c Release -v q -nologo < NUL" | Out-Null
