param(
    [string]$Out = "$PSScriptRoot\..\out\ms-train",
    [string[]]$Sets = @('train'),       # generated sets under out\ whose .lys are drawn again (never the evaluation sets, seed 7)
    [string[]]$Fonts = @('Leland', 'Bravura', 'Petaluma', 'Gonville', 'MuseJazz', 'Emmentaler', 'Finale Maestro', 'Finale Broadway'),
    [int]$PerPiece = 1,                 # fonts per piece, taken in turn (1: each piece in one font, the fonts sharing the pieces)
    [int]$Offset = 0,                   # the first piece's font (another set of the same pieces in other fonts)
    [int]$Resolution = 300,             # dpi (21 pixels a staff space; -Degrade wants 600: its settings are in pixels, set for Lily#'s 56 a space)
    [switch]$Degrade,                   # make the pages look printed and scanned (OmrDegrade, the settings drawn at random as run-gen's 'random')
    [switch]$Band,                      # band pieces: mend the MusicXML's percent repeats and the TABs' look (ms_band_xml.py), frets in $FretFaces
    [string[]]$FretFaces = @(),         # "face" or "face|bold"/"face|italic"; none: every one data/ms-frets.tsv has (ms_frets.py)
    [double[]]$FretSizes = @(9, 9, 10, 11, 12.5),   # points, one per piece at random (9 is MuseScore's own)
    [switch]$Mute,                      # band training data: some guitar and bass notes drawn with cross heads (muted)
    [switch]$Reuse                      # keep the pages already drawn; only the boxes again
)
# Other fonts for the detector's training data (B4a): the generated pieces are exported to MusicXML
# by Lily# and drawn by MuseScore 4 in each of its music fonts; the SVG MuseScore writes names every
# element's class, and ms_boxes.py turns it into the boxes dataset.py reads (omr/boxes-pNN.tsv).
# The notes' truth is the MusicXML. MuseScore is a tool here only: nothing of it ships.
$ErrorActionPreference = 'Stop'
# pwsh -File hands an array over as one string "a,b"
$Sets = @($Sets | ForEach-Object { $_ -split ',' })
$Fonts = @($Fonts | ForEach-Object { $_ -split ',' })
$FretFaces = @($FretFaces | ForEach-Object { $_ -split ',' })
$FretSizes = @($FretSizes | ForEach-Object { "$_" -split ',' } | ForEach-Object { [double]$_ })
$root = Resolve-Path "$PSScriptRoot\.."
if (-not $FretFaces.Count) {
    # the faces and styles the frets' table has (a face it left out would give boxes no names)
    $FretFaces = @(Get-Content "$root\data\ms-frets.tsv" | Where-Object { $_ -match '^fret:' } |
        ForEach-Object { ($_ -split "`t")[0].Substring(5) } | Select-Object -Unique)
}
$lysc = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe'
$ms = 'C:\Program Files\MuseScore 4\bin\MuseScore4.exe'
$py = "$root\tools\train-model\.venv\Scripts\python.exe"
$sw = [Diagnostics.Stopwatch]::StartNew()

if (-not $Reuse) {
    if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
    New-Item -ItemType Directory -Force $Out | Out-Null
    $Out = (Resolve-Path $Out).Path
    # the pieces, each given $PerPiece fonts in turn: <out>\<font>\<set>-<suite>\<piece>\<piece>.lys
    $pieces = foreach ($set in $Sets) {
        Get-ChildItem "$root\out\$set" -Recurse -Filter *.lys | Where-Object { $_.Directory.Name -ne 'omr' } |
            Sort-Object FullName | ForEach-Object { [pscustomobject]@{ Lys = $_; Set = $set } }
    }
    # a job per music font (and per frets' face for band pieces: "font|face")
    $jobs = @{}
    $k = 0
    $frng = [Random]::new(4321 + $Offset)
    foreach ($p in $pieces) {
        for ($j = 0; $j -lt $PerPiece; $j++) {
            $font = $Fonts[($k + $j + $Offset) % $Fonts.Count]
            $dir = Join-Path $Out ("{0}\{1}-{2}\{3}" -f ($font -replace ' ', ''), $p.Set, $p.Lys.Directory.Parent.Name, $p.Lys.BaseName)
            New-Item -ItemType Directory -Force $dir | Out-Null
            Copy-Item $p.Lys.FullName $dir
            $key = $font
            if ($Band) {
                $face, $fstyle = $FretFaces[$frng.Next($FretFaces.Count)] -split '\|'
                $size = $FretSizes[$frng.Next($FretSizes.Count)]
                # ms_boxes.fret_face: the face, the size, the style
                [IO.File]::WriteAllText("$dir\fret.txt", "$face`n$size`n$fstyle")
                $key = "$font|$face|$size|$fstyle"
            }
            if (-not $jobs.ContainsKey($key)) { $jobs[$key] = [Collections.Generic.List[string]]::new() }
            $jobs[$key].Add((Join-Path $dir $p.Lys.Name))
        }
        $k++
    }
    $all = $jobs.Values | ForEach-Object { $_ }
    [IO.File]::WriteAllLines("$Out\batch.txt", [string[]]$all)
    cmd /d /s /c "`"$lysc`" xml --batch `"$Out\batch.txt`" -j 0 < NUL > `"$Out\lysc-xml.log`" 2>&1"
    if ($LASTEXITCODE -ne 0) { Write-Warning "lysc reported failures; see $Out\lysc-xml.log" }
    foreach ($lys in $all) {
        $xml = [IO.Path]::ChangeExtension($lys, '.xml')
        if (-not (Test-Path $xml)) { Write-Warning "no $xml"; continue }
        # Lily# writes a tempo it does not draw (a metronome mark in every part); MuseScore would
        # draw it, a black head in the text the boxes do not name
        $t = [IO.File]::ReadAllText($xml)
        $t = [regex]::Replace($t, '(?s)\s*<direction[^>]*>\s*<direction-type>\s*<metronome>.*?</direction>', '')
        [IO.File]::WriteAllText($xml, $t)
    }
    $src = '.xml'
    if ($Band) {
        $xmls = @($all | ForEach-Object { [IO.Path]::ChangeExtension($_, '.xml') } | Where-Object { Test-Path $_ })
        # a hundred paths a call (the command line's length)
        for ($i = 0; $i -lt $xmls.Count; $i += 100) {
            & $py "$root\tools\train-model\ms_band_xml.py" @(if ($Mute) { '--mute' }) @($xmls[$i..([Math]::Min($i + 99, $xmls.Count - 1))])
        }
        # MuseScore's own .mscx: a style file sets the frets' face only on a score read from it, and
        # the TAB's staff type is mended there
        $list = foreach ($x in $xmls) { @{ in = $x; out = [IO.Path]::ChangeExtension($x, '.mscx') } }
        ConvertTo-Json -InputObject @($list) -Depth 4 | Out-File -Encoding utf8 "$Out\mscx.json"
        cmd /d /s /c "`"$ms`" -j `"$Out\mscx.json`" < NUL > `"$Out\mscx.log`" 2>&1"
        if ($LASTEXITCODE -ne 0) { Write-Warning "MuseScore reported failures making .mscx; see $Out\mscx.log" }
        $mscxs = @($xmls | ForEach-Object { [IO.Path]::ChangeExtension($_, '.mscx') } | Where-Object { Test-Path $_ })
        for ($i = 0; $i -lt $mscxs.Count; $i += 100) {
            & $py "$root\tools\train-model\ms_band_xml.py" --mscx @($mscxs[$i..([Math]::Min($i + 99, $mscxs.Count - 1))])
        }
        $src = '.mscx'
    }
    # one MuseScore run per font (and frets' face), its style file naming them (a music font it does not know draws Bravura)
    foreach ($key in $jobs.Keys) {
        $font, $face, $size, $fstyle = $key -split '\|'
        $name = $key -replace '[ |]', ''
        $fret = if ($face -and $face -ne 'default') { "<tabFretNumberFontFace>$face</tabFretNumberFontFace>`n" } else { '' }
        if ($size) { $fret += "<tabFretNumberFontSize>$size</tabFretNumberFontSize>`n" }
        $st = if ($fstyle) { @{ bold = 1; italic = 2 }[$fstyle] }
        if ($st) { $fret += "<tabFretNumberFontStyle>$st</tabFretNumberFontStyle>`n" }
        $style = "<?xml version=`"1.0`" encoding=`"UTF-8`"?>`n<museScore version=`"4.70`">`n<Style>`n<musicalSymbolFont>$font</musicalSymbolFont>`n<musicalTextFont>$font Text</musicalTextFont>`n$fret</Style>`n</museScore>`n"
        [IO.File]::WriteAllText("$Out\$name.mss", $style)
        $list = foreach ($lys in $jobs[$key]) {
            $dir = Split-Path $lys
            @{ in = [IO.Path]::ChangeExtension($lys, $src); out = @("$dir\page.svg", "$dir\page.png") }
        }
        # MuseScore now and then crashes on a face (Segoe Print's SVG) and leaves the rest of the job
        # undrawn: the pieces with no page yet go again, twice at most
        for ($try = 0; $try -lt 3 -and @($list).Count; $try++) {
            ConvertTo-Json -InputObject @($list) -Depth 4 | Out-File -Encoding utf8 "$Out\$name.json"
            cmd /d /s /c "`"$ms`" -S `"$Out\$name.mss`" -r $Resolution -j `"$Out\$name.json`" < NUL > `"$Out\$name.log`" 2>&1"
            if ($LASTEXITCODE -ne 0) { Write-Warning "MuseScore reported failures for $key; see $Out\$name.log" }
            $list = @($list | Where-Object { -not (Test-Path ($_.out[0] -replace '\.svg$', '-1.svg')) })
        }
    }
    # page-N.svg/png as pNN, as the reader and dataset.py expect
    foreach ($f in Get-ChildItem $Out -Recurse -Include 'page-*.svg', 'page-*.png') {
        if ($f.BaseName -match '^page-(\d+)$') { Move-Item -Force $f.FullName (Join-Path $f.DirectoryName ('p{0:D2}{1}' -f [int]$Matches[1], $f.Extension)) }
    }
    if ($Degrade) {
        $deg = "$root\src\OmrDegrade\bin\Release\net10.0-windows\OmrDegrade.exe"
        $n = 0
        $rng = [Random]::new(1234)
        foreach ($png in Get-ChildItem $Out -Recurse -Filter 'p??.png') {
            # as run-gen's 'random': every page its own printer and scanner
            $n++
            $a = @('--scale', ('{0:F2}' -f (0.4 + 0.25 * $rng.NextDouble())), '--blur', ('{0:F2}' -f (0.6 + 1.2 * $rng.NextDouble())),
                   '--ink', ('{0:F2}' -f (0.05 + 0.3 * $rng.NextDouble())), '--paper', ('{0:F2}' -f (0.05 + 0.25 * $rng.NextDouble())),
                   '--noise', ('{0:F0}' -f (3 + 12 * $rng.NextDouble())), '--specks', '0.0003')
            if ($rng.NextDouble() -lt 0.33) { $a += @('--binarize-auto', ('{0:F2}' -f (-0.2 + 0.3 * $rng.NextDouble()))) }
            if ($rng.NextDouble() -lt 0.7) { $a += @('--fill', ('{0:F2}' -f (0.2 + 0.6 * $rng.NextDouble())), '--fill-edge', ('{0:F1}' -f (2.5 + 3 * $rng.NextDouble()))) }
            & $deg --in $png.FullName --out $png.FullName @a --seed $n | Out-Null
        }
    }
}
$drawn = $sw.Elapsed.TotalSeconds
# the boxes, from each page's SVG (the page's own scale: a degraded page is smaller)
$env:USERNAME = if ($env:USERNAME) { $env:USERNAME } else { 'omr' }
& $py "$root\tools\train-model\ms_boxes.py" $Out | Out-Host
"drawn in {0:F1}s; total {1:F1}s" -f $drawn, $sw.Elapsed.TotalSeconds
