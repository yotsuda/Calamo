param(
    [string]$From = '',   # a folder holding the IMSLP PDFs as downloaded (their names start IMSLP<number> / PMLUS<number>)
    [switch]$Force        # extract the pages again
)
# S59, the fair set (2026-11-14, owner's decision of 2026-11-13): old prints outside homr's training data
# (OpenScore Lieder, PDMX, GrandStaff), scanned page by page. Held out like data/eval: never debug the reader on them.
# data/fair/<piece>/<edition>/: imslp.pdf (the whole download), pNN.png (the piece's pages, grey), <piece>.score.
# The truth is written by hand, data/fair/<piece>/truth/<piece>.lys; an edition whose notes differ (Lassen's Hainauer
# print, in another key) has its own, data/fair/<piece>/truth-<edition>/<piece>.lys. run-audiveris.ps1 -Fair scores them.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$fair = "$root\data\fair"
$py = "$root\tools\train-model\.venv\Scripts\python.exe"
# (piece, edition, IMSLP file number, its pages, rendered dpi or 0 = the embedded image, title, parts)
$voice = @('voice treble "Voice"', 'rh treble "Piano right"', 'lh bass "Piano left"')
$piano = @('rh treble "Piano right"', 'lh bass "Piano left"')
$sets = @(
    @{ Piece = 'lassen48-4'; Ed = 'bosworth'; File = 'IMSLP679347'; Pages = '2,3'; Dpi = 0; Title = 'Vorsatz (Resolution)'; Parts = $voice
       Note = 'Lassen, Eduard/6 Lieder Op.48/4 Vorsatz. Bosworth & Co., London (IMSLP #679347).' }
    @{ Piece = 'lassen48-4'; Ed = 'hainauer'; File = 'IMSLP710192'; Pages = '2,3'; Dpi = 300; Title = 'Vorsatz'; Parts = $voice
       Note = 'Lassen, Eduard/Lieder und Gesaenge No.37 = Op.48-4 Vorsatz. Julius Hainauer, Breslau (IMSLP #710192). Its pages are strips of images: rendered at 300 dpi.' }
    @{ Piece = 'sommer9-1'; Ed = 'litolff'; File = 'IMSLP777922'; Pages = '6,7'; Dpi = 0; Title = 'Der Bote'; Parts = $voice
       Note = 'Sommer, Hans/Lieder nach Gedichten von Eichendorff Op.9/1 Der Bote. Collection Litolff No.1584, Braunschweig (IMSLP #777922).' }
    @{ Piece = 'lang22'; Ed = 'schmidt'; File = 'PMLUS00461'; Pages = '1,2,3,4'; Dpi = 0; Title = 'Irish Love Song'; Parts = $voice
       Note = 'Lang, Margaret Ruthven/An Irish Love Song Op.22. Arthur P. Schmidt, Boston 1895 (IMSLP #510037, PMLUS00461; public domain in the US only: not redistributed, data/LICENSES.md).' }
    @{ Piece = 'hue-melodies1'; Ed = 'leduc'; File = 'IMSLP994220'; Pages = '1,2,3'; Dpi = 0; Title = 'Six Melodies I'; Parts = $voice
       Note = 'Hue, Georges/6 Melodies (Heine)/1. Leduc, Paris (IMSLP #994220).' }
    @{ Piece = 'bargiel41-1'; Ed = 'breitkopf'; File = 'IMSLP16043'; Pages = '2,3'; Dpi = 0; Title = 'Tempo di Menuetto'; Parts = $piano
       Note = 'Bargiel, Woldemar/8 Pianofortestuecke Op.41/1 Tempo di Menuetto. Breitkopf & Haertel, Leipzig (IMSLP #16043).' }
    @{ Piece = 'bargiel41-1'; Ed = 'schirmer'; File = 'IMSLP07475'; Pages = '2,3,4,5'; Dpi = 0; Title = 'Menuetto'; Parts = $piano
       Note = 'Bargiel, Woldemar/Op.41-1 Tempo di Menuetto, Oeuvres choisis No.55. G. Schirmer, New York (IMSLP #07475).' }
    @{ Piece = 'lacombe28-1'; Ed = 'hamelle'; File = 'IMSLP958979'; Pages = '4,5'; Dpi = 0; Title = 'Esquisses et souvenirs I'; Parts = $piano
       Note = 'Lacombe, Paul/Esquisses et souvenirs Op.28/1. J. Hamelle, Paris (IMSLP #958979).' }
)
# (the embedded image of each page, grey; a page with no single image, or Dpi set, rendered instead)
$extract = @'
import pymupdf, sys
d = pymupdf.open(sys.argv[1]); dpi = int(sys.argv[4])
for n, i in enumerate(int(x) for x in sys.argv[3].split(',')):
    p = d[i - 1]; ims = p.get_images()
    pm = None; own = 0
    if dpi == 0 and len(ims) == 1:
        pm = pymupdf.Pixmap(d, ims[0][0])
        try:
            if pm.n - pm.alpha > 1 or pm.alpha: pm = pymupdf.Pixmap(pymupdf.csGRAY, pm)
        except ValueError:   # (no colorspace to convert from: rendered at the image's own resolution)
            own = round(pm.width / (p.rect.width / 72)); pm = None
    if pm is None:
        pm = p.get_pixmap(dpi=dpi or own or 300, colorspace=pymupdf.csGRAY, alpha=False)
    f = sys.argv[2] + f'/p{n + 1:02d}.png'
    pm.save(f)
    if dpi == 0 and own == 0 and p.rotation:
        from PIL import Image; Image.open(f).rotate(-p.rotation, expand=True).save(f)
'@
foreach ($s in $sets) {
    $dir = "$fair\$($s.Piece)\$($s.Ed)"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $pdf = "$dir\imslp.pdf"
    if (-not (Test-Path $pdf)) {
        if (-not $From) { throw "no ${pdf}: pass -From <the folder of the downloads>" }
        $src = @(Get-ChildItem -LiteralPath $From -Filter "$($s.File)*.pdf")
        if ($src.Count -ne 1) { throw "$($s.File): $($src.Count) files in $From" }
        Copy-Item -LiteralPath $src[0].FullName $pdf
    }
    if ($Force -or -not (Test-Path "$dir\p01.png")) {
        Get-ChildItem $dir -Filter 'p*.png' | Remove-Item
        & $py -I -c $extract $pdf $dir $s.Pages $s.Dpi
        if ($LASTEXITCODE) { throw "$($s.Piece)/$($s.Ed): extraction failed" }
    }
    $score = @("# $($s.Note)", '# Fair set (data/fair, S59): held out, never to debug the reader on.', "title $($s.Title)") + ($s.Parts | ForEach-Object { "part $_" })
    Set-Content "$dir\$($s.Piece).score" $score -Encoding utf8NoBOM
    "$($s.Piece)/$($s.Ed): pages $($s.Pages) -> $((Get-ChildItem $dir -Filter 'p*.png').Count) png"
}
