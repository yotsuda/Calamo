param(
    [Parameter(Mandatory)][string]$Dir,       # a piece's folder (out\gen-grace\basic\basic001)
    [Parameter(Mandatory)][string]$Pattern,   # regex over the .lys; the first group (or the whole match) is the token whose glyph is wanted
    [int]$Nth = 1,                            # which match
    [string]$Cut,                             # png to write a cutout to
    [int]$Half = 160,                         # half size of the cutout
    [switch]$Read,                            # run omr --detector --at on the spot
    [string]$OmrArgs = ''
)
# Where a .lys token's glyph is on the page (S21): the token's character offset is the SVG's
# data-pos, the <g transform="translate(dx,dy) scale(..)"> is applied, and the page is drawn at
# the PNG's width over the viewBox's. First page only (a degraded set's page is half the size:
# halve the position for it). With -Read, the reader's --at debug lines for the spot.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$name = Split-Path $Dir -Leaf
$lys = [IO.File]::ReadAllText("$Dir\$name.lys")
$svg = [IO.File]::ReadAllText("$Dir\$name.svg")
$ms = [regex]::Matches($lys, $Pattern)
if ($ms.Count -lt $Nth) { throw "only $($ms.Count) matches of '$Pattern'" }
$m = $ms[$Nth - 1]
$pos = if ($m.Groups.Count -gt 1 -and $m.Groups[1].Success) { $m.Groups[1].Index } else { $m.Index }
$vb = [regex]::Match($svg, 'viewBox="0 0 ([\d.]+) ([\d.]+)"')
$png = [Drawing.Bitmap]::FromFile("$Dir\p01.png")
$scale = $png.Width / [double]$vb.Groups[1].Value
$tr = [regex]::Match($svg, '<g transform="translate\(([\d.-]+),([\d.-]+)\) scale\(([\d.]+),([\d.]+)\)">')
$dx = [double]$tr.Groups[1].Value; $dy = [double]$tr.Groups[2].Value; $sx = [double]$tr.Groups[3].Value
$el = [regex]::Match($svg, "<(text|path|line|rect)[^>]*\bx=`"([\d.-]+)`"[^>]*\by=`"([\d.-]+)`"[^>]*data-pos=`"$pos`"")
if (-not $el.Success) { $el = [regex]::Match($svg, "<(text|path|line|rect)[^>]*data-pos=`"$pos`"[^>]*>"); "element: $($el.Value)"; throw "no x/y for data-pos $pos" }
$x = ([double]$el.Groups[2].Value * $sx + $dx) * $scale
$y = ([double]$el.Groups[3].Value * $sx + $dy) * $scale
"$name '$($m.Value)' pos $pos -> px $([int]$x),$([int]$y)  ($($el.Value))"
if ($Cut) {
    $x0 = [Math]::Max(0, [int]$x - $Half); $y0 = [Math]::Max(0, [int]$y - $Half)
    $w = [Math]::Min($png.Width - $x0, 2 * $Half); $h = [Math]::Min($png.Height - $y0, 2 * $Half)
    $bmp = $png.Clone((New-Object Drawing.Rectangle $x0, $y0, $w, $h), $png.PixelFormat)
    $bmp.Save($Cut); $bmp.Dispose()
    "cut -> $Cut"
}
$png.Dispose()
if ($Read) {
    $exe = Get-ChildItem "$PSScriptRoot\..\src\Omr\bin\Release\*\omr.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    $tmp = Join-Path $env:TEMP "grace-at-$name"
    $args = @('--config', "$Dir\$name.score", '--pages', $Dir, '--from', '1', '--to', '1', '--out', $tmp, '--poly', '--detector', '--at', "$([int]$x),$([int]$y)") + ($OmrArgs -split ' ' | Where-Object { $_ })
    & $exe @args 2>&1 | Where-Object { $_ -match 'grace|detected|stem|beams|head at|rest at|flag rows' }
}
