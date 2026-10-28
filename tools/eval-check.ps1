param([string]$Piece, [string]$Edition, [string]$EdRun, [string]$MuRun, [string]$Label)
# For the owner, each part-bar where this reader's reading of an edition (EdRun: a run of omr with
# --truth --by-order --align on data/eval/<Piece>/<Edition>) disagrees with the truth: the edition's bar
# over the truth's (MuRun: the same on <Piece>/mutopia, Mutopia's PDF), each led by its system's start
# (clef, key and time) -- one PNG each, and an index to answer in.
Add-Type -AssemblyName System.Drawing
$e = Resolve-Path "$PSScriptRoot\..\data\eval"
$outDir = "$e\$Piece\check\$Edition"
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory $outDir | Out-Null

# the truth's bar b -> the read measure (by order: the measures read, in order, less those that stand for no bar)
function Measures($run) {
    $j = Get-Content "$run\omr.omr.json" -Raw | ConvertFrom-Json
    $order = $j.measures | Select-Object -ExpandProperty measure | Sort-Object -Unique
    $extra = Get-Content "$run\diffs.txt" | Where-Object { $_ -match '^\s*\+(\d+)' } | ForEach-Object { [int]$Matches[1] } | Sort-Object -Unique
    $kept = @(); for ($i = 0; $i -lt $order.Count; $i++) { if ($extra -notcontains ($i + 1)) { $kept += $order[$i] } }
    $map = @{}
    foreach ($m in $j.measures) { $b = [array]::IndexOf($kept, $m.measure) + 1; if ($b -gt 0) { $map["$b $($m.part)"] = $m } }
    return @{ Map = $map; All = $j.measures }
}
$ed = Measures $EdRun; $mu = Measures $MuRun

# the part-bars that differ: "  12 up    T: ..." (not "+9 ...": a measure read that stands for no bar)
$keys = Get-Content "$EdRun\diffs.txt" | Where-Object { $_ -match '^\s*(\d+)\s+(\S+)\s+T:' } | ForEach-Object { "$($Matches[1]) $($Matches[2])" } | Select-Object -Unique

$big = [Drawing.Font]::new('Segoe UI', 17, [Drawing.FontStyle]::Bold)
# the bar on its page, led by the start of its system on that staff (the clef, the key and any time
# signature: the first measure's left part, ten spaces at most) when it is not that first measure itself
function Crop($page, $m, $all) {
    $bmp = [Drawing.Bitmap]::new($page)
    $s = ($m.staff[3] - $m.staff[1]) / 4
    $y0 = [Math]::Max(0, [int]($m.box[1] - 2 * $s)); $y1 = [Math]::Min($bmp.Height - 1, [int]($m.box[3] + 2 * $s))
    $first = $all | Where-Object { $_.page -eq $m.page -and $_.part -eq $m.part -and [Math]::Abs($_.staff[1] - $m.staff[1]) -lt $s } | Sort-Object { $_.box[0] } | Select-Object -First 1
    # (from the system's start to the bar's end: any clef change on the way is in the picture; the bar in a red frame)
    $x0 = [Math]::Max(0, [int]((@($first.box[0], $m.box[0]) | Measure-Object -Minimum).Minimum - $s)); $x1 = [Math]::Min($bmp.Width - 1, [int]($m.box[2] + $s))
    $o = $bmp.Clone([Drawing.Rectangle]::new($x0, $y0, $x1 - $x0, $y1 - $y0), [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $bmp.Dispose()
    $g = [Drawing.Graphics]::FromImage($o)
    $g.DrawRectangle([Drawing.Pen]::new([Drawing.Color]::Red, [float]([Math]::Max(3, $s / 5))), [float]($m.box[0] - $x0), [float]2, [float]($m.box[2] - $m.box[0]), [float]($y1 - $y0 - 5))
    $g.Dispose()
    # (no wider than 1600 pixels)
    if ($o.Width -le 1600) { return $o }
    $k = 1600 / $o.Width; $small = [Drawing.Bitmap]::new($o, 1600, [int]($o.Height * $k)); $o.Dispose(); return $small
}
$rows = @()
foreach ($k in $keys) {
    $bar, $part = $k -split ' '
    $a = $ed.Map[$k]; $b = $mu.Map[$k]
    if (-not $a -or -not $b) { $rows += "| $bar | $part | (no box) | | |"; continue }
    $ca = Crop "$e\$Piece\$Edition\p$('{0:D2}' -f $a.page).png" $a $ed.All
    $cb = Crop "$e\$Piece\mutopia\p$('{0:D2}' -f $b.page).png" $b $mu.All
    $w = [Math]::Max($ca.Width, $cb.Width) + 10; $h = 40 + $ca.Height + 40 + $cb.Height + 10
    $o = [Drawing.Bitmap]::new($w, $h); $g = [Drawing.Graphics]::FromImage($o); $g.Clear([Drawing.Color]::White)
    $g.DrawString("bar $bar  $part   $Label", $big, [Drawing.Brushes]::DarkBlue, 5, 5); $g.DrawImage($ca, 0, 40, $ca.Width, $ca.Height)
    $y = 40 + $ca.Height; $g.DrawString('the truth (Mutopia)', $big, [Drawing.Brushes]::DarkGreen, 5, $y + 5); $g.DrawImage($cb, 0, $y + 40, $cb.Width, $cb.Height)
    $file = 'b{0:D2}-{1}.png' -f [int]$bar, $part
    $o.Save("$outDir\$file"); $g.Dispose(); $o.Dispose(); $ca.Dispose(); $cb.Dispose()
    $rows += "| $bar | $part | [$file]($file) | | |"
}
$md = @("# $Piece / $Edition : bars where this reader's reading disagrees with the truth", '',
        "Each image: this edition's bar (top), the truth's bar as Mutopia prints it (bottom), each in a red frame, shown from its system's start on that staff (so the clef, key and time, and any clef change before the bar, are in the picture).",
        'Only notes and rests count (pitch and length, accidentals that change a pitch, chord notes, how rests are written). Slurs, marks, dynamics and fingering do not.',
        "Answer in the last columns: **same** (this edition = the truth: the OMR misread it) or what this edition has instead.", '',
        '| bar | staff | image | edition = truth? | note |', '|---|---|---|---|---|') + $rows
[IO.File]::WriteAllLines("$outDir\index.md", $md)
"{0}/{1}: {2} images" -f $Piece, $Edition, $keys.Count
