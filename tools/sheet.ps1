param(
    [string]$Pattern = 'head missed',   # regex over sym-diffs.txt lines
    [string]$Suite = '*',
    [int]$Max = 12,
    [string]$Out = "$PSScriptRoot\..\out\sheet.png",
    [string]$Gen = "$PSScriptRoot\..\out\gen"
)
# A contact sheet of symbol-level misses: a tile around each matching line of sym-diffs.txt, the
# spot circled, the line as its caption. For looking at a kind of error before changing anything.
Add-Type -AssemblyName System.Drawing
$tw = 360; $th = 300; $cols = 4; $cap = 18
$hits = foreach ($f in Get-ChildItem (Join-Path $Gen $Suite) -Recurse -Filter sym-diffs.txt) {
    foreach ($l in [IO.File]::ReadAllLines($f.FullName)) {
        if ($l -match $Pattern -and $l -match '^p(\d+) .* at (\d+),(\d+)') {
            [pscustomobject]@{ Dir = $f.Directory.Parent.FullName; Piece = $f.Directory.Parent.Name; Page = [int]$Matches[1]; X = [int]$Matches[2]; Y = [int]$Matches[3]; Text = $l }
        }
    }
}
$hits = @($hits | Select-Object -First $Max)
if ($hits.Count -eq 0) { "no lines match '$Pattern'"; return }
$rows = [math]::Ceiling($hits.Count / $cols)
$sheet = New-Object Drawing.Bitmap ($cols * $tw), ($rows * ($th + $cap))
$g = [Drawing.Graphics]::FromImage($sheet)
$g.Clear([Drawing.Color]::White)
$pen = New-Object Drawing.Pen ([Drawing.Color]::Red), 2
$font = New-Object Drawing.Font 'Consolas', 8
$cache = @{}
for ($i = 0; $i -lt $hits.Count; $i++) {
    $h = $hits[$i]
    $png = Join-Path $h.Dir ('p{0:D2}.png' -f $h.Page)
    if (-not $cache.ContainsKey($png)) { $cache[$png] = [Drawing.Bitmap]::FromFile($png) }
    $src = $cache[$png]
    # a region twice the tile, shown at half size
    $sx = $h.X - $tw; $sy = $h.Y - $th
    $tx = ($i % $cols) * $tw; $ty = [math]::Floor($i / $cols) * ($th + $cap)
    $g.DrawImage($src, (New-Object Drawing.Rectangle $tx, ($ty + $cap), $tw, $th), (New-Object Drawing.Rectangle $sx, $sy, (2 * $tw), (2 * $th)), [Drawing.GraphicsUnit]::Pixel)
    $g.DrawEllipse($pen, $tx + $tw / 2 - 18, $ty + $cap + $th / 2 - 18, 36, 36)
    $g.DrawString("$($h.Piece) $($h.Text -replace '^p\d+ ', '')", $font, [Drawing.Brushes]::Black, $tx + 2, $ty + 2)
    $g.DrawRectangle([Drawing.Pens]::Gray, $tx, $ty, $tw - 1, $th + $cap - 1)
}
$sheet.Save((Join-Path (Resolve-Path (Split-Path $Out)) (Split-Path $Out -Leaf)))
foreach ($b in $cache.Values) { $b.Dispose() }
"$($hits.Count) tiles -> $Out"
