param(
    [string]$LilySharp = 'C:\MyProj\LilySharp',
    [string[]]$Fonts = @('Bravura', 'Petaluma', 'Leland')
)
# The SMuFL fonts' glyph tables for the SVG truth (SvgTruth), in the form of data/emmentaler-bbox.tsv:
# codepoint, feta name, box (staff spaces, y up). Lily# writes a SMuFL font's glyphs by their SMuFL
# code points; the feta name comes from Lily#'s MusicGlyphs table (LilySharp.Core/Svg/MusicGlyph.cs),
# the box from the font's own metadata (glyphBBoxes) beside lysc. Run again when Lily# adds a glyph.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$table = Get-Content "$LilySharp\LilySharp.Core\Svg\MusicGlyph.cs" -Raw
$fontDir = "$LilySharp\LilySharp.Cli\bin\Release\net10.0\Fonts"
# new(MusicGlyph.NoteheadBlack, "noteheadBlack", 0xE0A4, "noteheads.s2", EmmentalerGlyphs.NoteheadBlack),
$rows = [regex]::Matches($table, 'new\(MusicGlyph\.\w+,\s*"([^"]+)",\s*0x([0-9A-Fa-f]+),\s*"([^"]*)"') | ForEach-Object {
    [pscustomobject]@{ Smufl = $_.Groups[1].Value; Code = [Convert]::ToInt32($_.Groups[2].Value, 16); Feta = $_.Groups[3].Value }
} | Where-Object { $_.Code -ne 0 -and $_.Feta -ne '' }
# (the time digits: the truth names them by the digit, as Emmentaler draws them in ASCII)
$digits = @{ zero = '0'; one = '1'; two = '2'; three = '3'; four = '4'; five = '5'; six = '6'; seven = '7'; eight = '8'; nine = '9' }
$inv = [Globalization.CultureInfo]::InvariantCulture
foreach ($font in $Fonts) {
    $meta = Get-Content "$fontDir\$($font.ToLowerInvariant())_metadata.json" -Raw | ConvertFrom-Json
    $lines = @("# codepoint`tname`tx0`ty0`tx1`ty1 (staff spaces, y up) from Lily# MusicGlyphs and $font's SMuFL metadata (tools/smufl-tables.ps1)")
    $missing = @()
    foreach ($r in $rows | Sort-Object Code -Unique) {
        $b = $meta.glyphBBoxes.($r.Smufl)
        if ($null -eq $b) { $missing += $r.Smufl; continue }
        $name = if ($digits.ContainsKey($r.Feta)) { $digits[$r.Feta] } else { $r.Feta }
        $v = @($b.bBoxSW[0], $b.bBoxSW[1], $b.bBoxNE[0], $b.bBoxNE[1]) | ForEach-Object { ([double]$_).ToString('F6', $inv) }
        $lines += ('{0:X4}' -f $r.Code) + "`t$name`t" + ($v -join "`t")
    }
    $out = "$root\data\smufl-bbox-$font.tsv"
    [IO.File]::WriteAllLines($out, $lines)
    "{0}: {1} glyphs -> {2}{3}" -f $font, ($lines.Count - 1), $out, $(if ($missing) { "; not in the font: $($missing -join ', ')" } else { '' })
}
