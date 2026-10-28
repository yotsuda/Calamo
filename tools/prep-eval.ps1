param([switch]$Force)    # make again what is there already
# The held-out evaluation set (data/eval): scores this reader was never developed on, read by it and
# by Audiveris (run-audiveris.ps1 -Eval). Never debug the reader on these pages: what is learnt from
# them stops them measuring anything.
#  A. dichterliebe/ms/NN: Schumann's Dichterliebe from OpenScore Lieder (CC0, MuseScore), each song's
#     pages drawn by MuseScore 4 at 300 dpi; the truth is MuseScore's MusicXML of the same file.
#  B. dichterliebe/scan: the same songs printed by Breitkopf & Haertel (Schumanns Werke, IMSLP #270924),
#     downloaded by hand (IMSLP lets no script fetch it) -- prepared elsewhere.
#  C. chopin28-4: Chopin's prelude Op.28 No.4 from Mutopia (LilyPond, after Peters/Scholtz): mutopia/
#     is the published A4 PDF at 300 dpi; the truth (truth/page-{up,down}.notes, a pickup of 1/4) is
#     LilyPond 2.26's event listener on the .ly (convert-ly, and the old layout commands that no
#     longer parse taken out: truth/page.ly). The Peters/Scholtz print (IMSLP #86359) is downloaded by hand.
#  D. lieder/<song>: fifteen songs from OpenScore Lieder (CC0, 2026-10-21; lieder/songs.tsv), chosen for the
#     notation they carry (repeats and voltas, ornaments, grace notes, ottavas, pedals, arpeggios) and their
#     prints' spread. ms/ is MuseScore 4's drawing at 300 dpi, the truth MuseScore's MusicXML of it (as A);
#     scan/ is the IMSLP print OpenScore typed it from (songs.tsv's imslp, downloaded by hand to
#     scan/imslp.pdf), the song's pages (songs.tsv's pages) as they were scanned, read against the same truth.
#  E. data/dev/lieder/<song>: made as D, but NOT held out: other songs of the same IMSLP files (2026-10-22), to
#     reproduce and debug what the held-out prints show (run-audiveris.ps1 -Dev).
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path "$PSScriptRoot\.."
$ms = 'C:\Program Files\MuseScore 4\bin\MuseScore4.exe'
$src = "$root\data\eval\dichterliebe\src"
foreach ($f in Get-ChildItem "$src\*.mscz" | Sort-Object Name) {
    $n = $f.BaseName
    $dir = "$root\data\eval\dichterliebe\ms\$n"
    if ((Test-Path "$dir\truth.musicxml") -and -not $Force) { continue }
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $dir | Out-Null
    cmd /d /s /c "`"$ms`" -r 300 -o `"$dir\raw.png`" `"$($f.FullName)`" < NUL > NUL 2>&1"
    cmd /d /s /c "`"$ms`" -o `"$dir\truth.musicxml`" `"$($f.FullName)`" < NUL > NUL 2>&1"
    # MuseScore's pages are transparent: on white, as pNN.png
    $raws = Get-ChildItem "$dir\raw-*.png" | Sort-Object { [int]($_.BaseName -replace '\D', '') }
    $i = 0
    foreach ($r in $raws) {
        $i++
        $b = [Drawing.Bitmap]::new($r.FullName)
        $w = [Drawing.Bitmap]::new($b.Width, $b.Height)
        $g = [Drawing.Graphics]::FromImage($w)
        $g.Clear([Drawing.Color]::White); $g.DrawImage($b, 0, 0, $b.Width, $b.Height)
        $w.Save(("$dir\p{0:D2}.png" -f $i), [Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $w.Dispose(); $b.Dispose()
        Remove-Item $r.FullName
    }
    # the parts: the voice, the piano's two staves (MusicXmlTruth gives each staff in order a part)
    $title = ((Get-Content "$src\$n.md" -Encoding utf8 | Where-Object { $_ -match '^#\s' } | Select-Object -First 1) -replace '^#\s*', '').Trim()
    @(
        "# Schumann, Dichterliebe Op.48 No.$([int]$n): $title (OpenScore Lieder, CC0; drawn by MuseScore 4 at 300 dpi)."
        "# Held-out evaluation (data/eval): never debug the reader on it."
        "title $title"
        'part voice treble "Voice"'
        'part rh treble "Piano right"'
        'part lh bass "Piano left"'
    ) | Set-Content "$dir\$n.score" -Encoding utf8
    "{0}: {1} pages  {2}" -f $n, $i, $title
}
# the old prints, as scanned: each page's own image out of the PDF (B: all 34 pages; C: page 4, the prelude),
# turned as the page says (/Rotate: Butterworth's are stored on their side)
$py = "$root\tools\train-model\.venv\Scripts\python.exe"
$extract = "import pymupdf,sys`nd=pymupdf.open(sys.argv[1]); keep=[int(x) for x in sys.argv[3].split(',')] if sys.argv[3] else range(1,d.page_count+1)`nfor n,i in enumerate(keep):`n    pm=pymupdf.Pixmap(d,d[i-1].get_images()[0][0])`n    if pm.n-pm.alpha>1 or pm.alpha: pm=pymupdf.Pixmap(pymupdf.csGRAY,pm)`n    pm.save(sys.argv[2]+f'/p{n+1:02d}.png')`n    if d[i-1].rotation:`n        from PIL import Image; f=sys.argv[2]+f'/p{n+1:02d}.png'; Image.open(f).rotate(-d[i-1].rotation, expand=True).save(f)"
if ((Test-Path "$root\data\eval\dichterliebe\scan\breitkopf.pdf") -and (-not (Test-Path "$root\data\eval\dichterliebe\scan\p01.png") -or $Force)) {
    & $py -c $extract "$root\data\eval\dichterliebe\scan\breitkopf.pdf" "$root\data\eval\dichterliebe\scan" ''
    "dichterliebe/scan: Breitkopf's pages"
}
if ((Test-Path "$root\data\eval\chopin28-4\scan\peters.pdf") -and (-not (Test-Path "$root\data\eval\chopin28-4\scan\p01.png") -or $Force)) {
    & $py -c $extract "$root\data\eval\chopin28-4\scan\peters.pdf" "$root\data\eval\chopin28-4\scan" '4'
    "chopin28-4/scan: Peters' page 4"
}
$c = "$root\data\eval\chopin28-4"
if (-not (Test-Path "$c\mutopia\p01.png") -or $Force) {
    & "$root\tools\train-model\.venv\Scripts\python.exe" -c "import pymupdf,sys; d=pymupdf.open(sys.argv[1]); [d[i].get_pixmap(dpi=300, alpha=False).save(sys.argv[2]+f'/p{i+1:02d}.png') for i in range(d.page_count)]" "$c\src\Chop-28-4a-a4.pdf" "$c\mutopia"
    "chopin28-4: the Mutopia PDF at 300 dpi"
}
# D. OpenScore Lieder: MuseScore's pages and truth, and the IMSLP print's pages once it is there; and
# data/dev/lieder, made the same way: songs outside the held-out set, from the same IMSLP files, to debug on
$evalSongs = Import-Csv "$root\data\eval\lieder\songs.tsv" -Delimiter "`t" -Header slug, path, id, imslp, pages | Where-Object { $_.slug -notmatch '^(#|slug$)' }
foreach ($ld in "$root\data\eval\lieder", "$root\data\dev\lieder") {
$held = $ld -match '\\eval\\'
foreach ($song in Import-Csv "$ld\songs.tsv" -Delimiter "`t" -Header slug, path, id, imslp, pages | Where-Object { $_.slug -notmatch '^(#|slug$)' }) {
    $n = $song.slug; $dir = "$ld\$n"
    $mscx = "$dir\src\lc$($song.id).mscx"
    if (-not (Test-Path "$dir\ms\truth.musicxml") -or $Force) {
        if (Test-Path "$dir\ms") { Remove-Item -Recurse -Force "$dir\ms" }
        New-Item -ItemType Directory -Force "$dir\ms" | Out-Null
        cmd /d /s /c "`"$ms`" -r 300 -o `"$dir\ms\raw.png`" `"$mscx`" < NUL > NUL 2>&1"
        cmd /d /s /c "`"$ms`" -o `"$dir\ms\truth.musicxml`" `"$mscx`" < NUL > NUL 2>&1"
        $i = 0
        foreach ($r in Get-ChildItem "$dir\ms\raw-*.png" | Sort-Object { [int]($_.BaseName -replace '\D', '') }) {
            $i++
            $b = [Drawing.Bitmap]::new($r.FullName)
            $w = [Drawing.Bitmap]::new($b.Width, $b.Height)
            $g = [Drawing.Graphics]::FromImage($w)
            $g.Clear([Drawing.Color]::White); $g.DrawImage($b, 0, 0, $b.Width, $b.Height)
            $w.Save(("$dir\ms\p{0:D2}.png" -f $i), [Drawing.Imaging.ImageFormat]::Png)
            $g.Dispose(); $w.Dispose(); $b.Dispose()
            Remove-Item $r.FullName
        }
        "lieder/${n}: MuseScore's $i pages"
    }
    # the parts: the voice, the piano's two staves, each by its first clef in the truth
    [xml]$x = Get-Content "$dir\ms\truth.musicxml" -Raw -Encoding utf8
    # (the song's own title; the work's is the set's)
    $title = @($x.'score-partwise'.'movement-title', $x.'score-partwise'.work.'work-title') | Where-Object { $_ } | Select-Object -First 1
    $clefs = foreach ($c in @($x.SelectNodes('//part') | ForEach-Object { $p = $_; $cl = @($p.SelectNodes('measure[1]/attributes/clef')); if ($cl.Count) { $cl } })) {
        $oct = $c.'clef-octave-change'
        switch ($c.sign) { 'F' { 'bass' } 'C' { if ($c.line -eq '3') { 'alto' } else { 'tenor' } } default { if ($oct -eq '-1') { 'treble_8' } else { 'treble' } } }
    }
    $what = if ($held) { "# Held-out evaluation (data/eval): never debug the reader on it." } else { "# Development (data/dev): outside the held-out set, to debug the reader on." }
    $head = @("# $($song.path -replace '_', ' ') (OpenScore Lieder, CC0; lc$($song.id).mscx; IMSLP #$($song.imslp)).", $what, "title $title",
              "part voice $($clefs[0]) `"Voice`"", "part rh $($clefs[1]) `"Piano right`"", "part lh $($clefs[2]) `"Piano left`"")
    if (-not (Test-Path "$dir\ms\$n.score") -or $Force) { $head | Set-Content "$dir\ms\$n.score" -Encoding utf8 }
    # the print: the song's pages out of the IMSLP file, each its own scanned image (a set's file, downloaded
    # once to one song's scan/, serves the set's other songs; a development song reads the held-out set's copy in place)
    $pdf = "$dir\scan\imslp.pdf"
    if (-not (Test-Path $pdf)) {
        $shared = Import-Csv "$ld\songs.tsv" -Delimiter "`t" -Header slug, path, id, imslp, pages | Where-Object { $_.imslp -eq $song.imslp -and $_.slug -ne $n } |
                  ForEach-Object { "$ld\$($_.slug)\scan\imslp.pdf" } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($shared) { New-Item -ItemType Directory -Force "$dir\scan" | Out-Null; Copy-Item $shared $pdf }
        elseif (-not $held) {
            $pdf = $evalSongs | Where-Object { $_.imslp -eq $song.imslp } | ForEach-Object { "$root\data\eval\lieder\$($_.slug)\scan\imslp.pdf" } |
                   Where-Object { Test-Path $_ } | Select-Object -First 1
        }
    }
    if ($pdf -and (Test-Path $pdf) -and $song.pages -match '^(\d+)-(\d+)$' -and (-not (Test-Path "$dir\scan\p01.png") -or $Force)) {
        New-Item -ItemType Directory -Force "$dir\scan" | Out-Null
        & $py -c $extract $pdf "$dir\scan" (([int]$Matches[1]..[int]$Matches[2]) -join ',')
        $head | Set-Content "$dir\scan\$n.score" -Encoding utf8
        "$(Split-Path $ld -Leaf)/${n}/scan: IMSLP pages $($song.pages)"
    }
}
}
