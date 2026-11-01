param(
    [string[]]$Sets = @(),                  # only these (bohemian, ninetofive, bwv610, fugue1, scan-<p>); all when empty
    [string]$Audiveris = 'C:\Program Files\Audiveris\Audiveris.exe',
    [double]$MaxPixels = 18e6,              # Audiveris refuses a page over 20 million pixels
    [switch]$Reuse,                         # keep the pages and Audiveris's output from the run before
    [switch]$Eval,                          # the held-out set (data/eval, prep-eval.ps1) instead of the benchmarks
    [switch]$Dev,                           # data/dev/lieder (prep-eval.ps1's E): scored as -Eval, but NOT held out -- debug on it
    [switch]$NoAlign,                       # (-Eval, -Dev) score the measures in the order read, not matched with the truth's bars (--align)
    [string[]]$OmrArgs = @(),               # more arguments for this reader's own reading (e.g. '--no-unprinted', to tell what a step does)
    [switch]$Verify                         # this reader's reading with the redraw check (--verify): its todos as Lily# gets them (tools/todo-quality.ps1)
)
# Audiveris against this reader on the real-score benchmarks (run-real.ps1's), page for page. Both
# read the same pages, scaled down to fit Audiveris's limit; Audiveris's MusicXML is scored by
# omr --pred against the same truth as the reader's own reading (--detector). Audiveris is
# run as a program only (AGPL): its output is the one thing taken from it.
Add-Type -AssemblyName System.Drawing
$Sets = @($Sets | ForEach-Object { $_ -split ',' })   # (pwsh -File passes a list as one string)
$root = Resolve-Path "$PSScriptRoot\.."
# a folder's pages, p01.png on (not the page images Lily# leaves beside a page it read, p01.omr-p01.png:
# one in tchaikovsky39-5's scan made it a page of two from 2026-10-28, and its row fell out of the table)
function PageCount($dir) { @(Get-ChildItem "$dir\p*.png" | Where-Object Name -match '^p\d+\.png$').Count }
$omr ="$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
$out = "$root\out\audiveris"
$lysc = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe'
if ($Verify) { $OmrArgs += '--verify', $lysc }
$benches = [ordered]@{
    'bohemian'   = @{ Pages = 'C:\MyProj\LilySharp-Lab\sessions\p661\bohemian\hi4'; To = 19; Args = @('--config', "$root\data\bohemian\bohemian.score", '--truth', "$root\data\bohemian\truth.md") }
    'ninetofive' = @{ Pages = "$root\data\ninetofive"; To = 3; Args = @('--config', "$root\data\ninetofive\ninetofive.score", '--truth', "$root\data\ninetofive\page-Bass.notes") }
    'bwv610'     = @{ Pages = "$root\data\bwv610"; To = 2; Args = @('--config', "$root\data\bwv610\bwv610.score", '--truth', "$root\data\bwv610\page-{part}.notes", '--poly') }
    'fugue1'     = @{ Pages = "$root\data\wtk1"; To = 2; Args = @('--config', "$root\data\wtk1\fugue1.score", '--truth', "$root\data\wtk1\page-{part}-v*.notes", '--poly') }
}
foreach ($scan in Get-ChildItem "$root\data\print" -Directory | Where-Object { Test-Path "$($_.FullName)\scan\p01.png" }) {
    $p = $scan.Name; $dir = $scan.FullName
    $benches["scan-$p"] = @{ Pages = "$dir\scan"; To = 1; Args = @('--config', "$dir\$p.score", '--truth', "$dir\$p-{part}-v*.notes", '--poly') }
}
if ($Eval) {
    # (never debug the reader on these: see prep-eval.ps1)
    $benches = [ordered]@{}
    foreach ($d in Get-ChildItem "$root\data\eval\dichterliebe\ms" -Directory | Sort-Object Name) {
        $benches["dl-ms-$($d.Name)"] = @{ Pages = $d.FullName; To = (PageCount $d.FullName)
                                          Args = @('--config', "$($d.FullName)\$($d.Name).score", '--truth', "$($d.FullName)\truth.musicxml", '--poly') }
    }
    $c = "$root\data\eval\chopin28-4"
    $benches['chopin28-4-mutopia'] = @{ Pages = "$c\mutopia"; To = 1; Args = @('--config', "$c\mutopia\chopin28-4.score", '--truth', "$c\truth\page-{part}.notes", '--pickup', '1/4', '--poly') }
    # the old prints: Breitkopf's Dichterliebe (the sixteen songs on through, one truth numbered on), Peters' prelude
    $dl = "$root\data\eval\dichterliebe"
    $xmls = (Get-ChildItem "$dl\ms" -Directory | Sort-Object Name | ForEach-Object { "$($_.FullName)\truth.musicxml" }) -join '+'
    $benches['dl-scan'] = @{ Pages = "$dl\scan"; To = (PageCount "$dl\scan"); Args = @('--config', "$dl\scan\dichterliebe.score", '--truth', $xmls, '--poly') }
    $benches['chopin28-4-scan'] = @{ Pages = "$c\scan"; To = 1; Args = @('--config', "$c\scan\chopin28-4.score", '--truth', "$c\truth\page-{part}.notes", '--pickup', '1/4', '--poly') }
    # short piano pieces from Mutopia (2026-10-14, data/eval/CANDIDATES-2026-10-14.md): the truth is the Mutopia
    # source's, mutopia/ its own PDF, scan/ an IMSLP print of the edition it was typed from, the truth put right
    # where the owner found the print differs (truth/; truth-mutopia/ keeps Mutopia's own for its PDF where they part).
    # Clementi: the first movement only (truth/page.ly; Mutopia's page 1, Schirmer's p.3)
    foreach ($n in 'burgmuller100-01', 'burgmuller100-02', 'clementi36-1', 'schumann15-1', 'schumann15-7', 'tchaikovsky39-1', 'tchaikovsky39-5') {
        $d = "$root\data\eval\$n"
        $pickup = if ($n -eq 'schumann15-7') { @('--pickup', '1/4') } else { @() }
        # (and the new typesettings, a folder each: dorico, rje, abc; an edition's own truth, truth-<folder>, where its notation differs)
        foreach ($kind in Get-ChildItem $d -Directory | Where-Object { Test-Path "$($_.FullName)\$n.score" } | ForEach-Object Name) {
            $truth = if (Test-Path "$d\truth-$kind") { "$d\truth-$kind" } else { "$d\truth" }
            # (Clementi's Mutopia PDF and Schirmer scan: their first page; Bosse's pages hold the first movement only)
            $to = if ($n -eq 'clementi36-1' -and $kind -in 'mutopia', 'scan') { 1 } else { (PageCount "$d\$kind") }
            $benches["$n-$kind"] = @{ Pages = "$d\$kind"; To = $to; Args = @('--config', "$d\$kind\$n.score", '--truth', "$truth\page-{part}.notes") + $pickup + @('--poly') }
        }
    }
    # fifteen OpenScore Lieder songs (2026-10-21, data/eval/lieder/songs.tsv): ms/ MuseScore's drawing, scan/ the
    # IMSLP print OpenScore typed it from (once downloaded by hand), both against MuseScore's MusicXML
    foreach ($d in Get-ChildItem "$root\data\eval\lieder" -Directory | Sort-Object Name) {
        foreach ($kind in 'ms', 'scan') {
            if (-not (Test-Path "$($d.FullName)\$kind\p01.png")) { continue }
            $benches["lied-$($d.Name)-$kind"] = @{ Pages = "$($d.FullName)\$kind"; To = (PageCount "$($d.FullName)\$kind")
                                                  Args = @('--config', "$($d.FullName)\$kind\$($d.Name).score", '--truth', "$($d.FullName)\ms\truth.musicxml", '--poly') }
        }
    }
}
if ($Dev) {
    $Eval = $true   # (scored as the held-out set is)
    $benches = [ordered]@{}
    foreach ($d in Get-ChildItem "$root\data\dev\lieder" -Directory | Sort-Object Name) {
        foreach ($kind in 'ms', 'scan') {
            if (-not (Test-Path "$($d.FullName)\$kind\p01.png")) { continue }
            $benches["dev-$($d.Name)-$kind"] = @{ Pages = "$($d.FullName)\$kind"; To = (PageCount "$($d.FullName)\$kind")
                                                 Args = @('--config', "$($d.FullName)\$kind\$($d.Name).score", '--truth', "$($d.FullName)\ms\truth.musicxml", '--poly') }
        }
    }
}
$total = @{ audiveris = @(0, 0); this = @(0, 0) }   # notes+rests found, of the truth's
$rows = [System.Collections.Generic.List[object]]::new()   # this reader's count per set
$pattern = 'exact events|notes\+rests|^align:|^ties:|^slur (starts|ends):'
$marks = @(@('ties', 'tie'), @('slur starts', 'slurStart'), @('slur ends', 'slurEnd'))   # (the summary's name, the column's)
foreach ($name in $benches.Keys) {
    if ($Sets.Count -gt 0 -and $Sets -notcontains $name) { continue }
    $b = $benches[$name]
    $dir = "$out\$name"
    $pages = "$dir\pages"
    if (-not $Reuse -and (Test-Path $dir)) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $pages, "$dir\aud" | Out-Null
    # the pages, scaled to fit (both readers read these)
    $files = @()
    for ($i = 1; $i -le $b.To; $i++) {
        $dst = "$pages\p{0:D2}.png" -f $i
        $files += $dst
        if (Test-Path $dst) { continue }
        $img = [Drawing.Bitmap]::new(("$($b.Pages)\p{0:D2}.png" -f $i))
        $k = [Math]::Min(1.0, [Math]::Sqrt($MaxPixels / ($img.Width * $img.Height)))
        $w = [int]($img.Width * $k); $h = [int]($img.Height * $k)
        $small = [Drawing.Bitmap]::new($w, $h)
        $g = [Drawing.Graphics]::FromImage($small)
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($img, 0, 0, $w, $h)
        $small.Save($dst, [Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $small.Dispose(); $img.Dispose()
    }
    # Audiveris: every page in one run, each its own book (p01.mxl, ...)
    $t = [Diagnostics.Stopwatch]::StartNew()
    if (-not ($Reuse -and (Get-ChildItem "$dir\aud\*.mxl" -ErrorAction SilentlyContinue))) {
        $quoted = ($files | ForEach-Object { "`"$_`"" }) -join ' '
        cmd /d /s /c "`"$Audiveris`" -batch -transcribe -export -output `"$dir\aud`" -- $quoted < NUL > `"$dir\aud\run.log`" 2>&1"
    }
    $audSec = $t.Elapsed.TotalSeconds
    # a page Audiveris made nothing of is "-"; one it split into movements (p01.mvt1.mxl, ...) is their files in order
    $preds = @(); $notes = @()
    foreach ($f in $files) {
        $stem = [IO.Path]::GetFileNameWithoutExtension($f)
        $m = @(Get-ChildItem "$dir\aud" -Filter "$stem*.mxl" | Sort-Object Name)
        if ($m.Count -eq 0) { $preds += '-'; $notes += "$stem none" } else { $preds += ($m.FullName -join '+'); if ($m.Count -gt 1) { $notes += "$stem $($m.Count) movements" } }
    }
    $common = $b.Args + @('--pages', $pages, '--from', '1', '--to', "$($b.To)")
    # (held out: both readers' measures matched with the truth's bars in order -- a page read as
    # nothing or a movement split in two would otherwise put every bar after it off by one)
    if ($Eval -and -not $NoAlign) { $common += '--align' }
    $audText = & $omr @($common + @('--out', "$dir\scored-audiveris", '--pred', ($preds -join ','))) 2>&1 | Out-String
    $t.Restart()
    # (held out: this reader's measures too are scored in the order read, as Audiveris's are -- the
    # printed bar numbers it goes by leave out a pickup the truth counts)
    $ownText = & $omr @($common + @('--out', "$dir\scored-own", '--detector') + $(if ($Eval) { @('--by-order') } else { @() }) + $OmrArgs) 2>&1 | Out-String
    $ownSec = $t.Elapsed.TotalSeconds
    # (its whole output kept: the part-bars scored, the redraw check's summary)
    $ownText | Out-File "$dir\scored-own\run.txt" -Encoding utf8
    $pick = { param($text) (($text -split "`n" | Where-Object { $_ -match $pattern } | ForEach-Object { $_.Trim() }) -join ' | ') }
    "{0,-16} audiveris ({1,5:F1}s{2}): {3}" -f $name, $audSec, $(if ($notes) { '; ' + ($notes -join ', ') } else { '' }), (& $pick $audText)
    "{0,-16} this      ({1,5:F1}s): {2}" -f '', $ownSec, (& $pick $ownText)
    foreach ($k in @(@('audiveris', $audText), @('this', $ownText))) {
        if ($k[1] -match 'truth (\d+), omr \d+; pitch\+duration found (\d+)') { $total[$k[0]][0] += [int]$Matches[2]; $total[$k[0]][1] += [int]$Matches[1] }
    }
    if ($ownText -match 'truth (\d+), omr \d+; pitch\+duration found (\d+)') {
        $row = [ordered]@{ set = $name; truth = [int]$Matches[1]; found = [int]$Matches[2] }
        # (a MusicXML truth's ties, a note each, and slurs, a chord each: found of the truth's, and read right of those read;
        # every row has the columns, blank where the truth marks none: Export-Csv takes the first row's)
        foreach ($m in $marks) {
            foreach ($c in 'Truth', 'Found', 'Omr', 'Right') { $row["$($m[1])$c"] = '' }
            if ($ownText -match "(?m)^$($m[0]): truth (\d+), omr (\d+), found (\d+), read right (\d+)") {
                $row["$($m[1])Truth"] = [int]$Matches[1]; $row["$($m[1])Found"] = [int]$Matches[3]
                $row["$($m[1])Omr"] = [int]$Matches[2]; $row["$($m[1])Right"] = [int]$Matches[4]
            }
        }
        $rows.Add([pscustomobject]$row)
    }
}
foreach ($k in 'audiveris', 'this') {
    if ($total[$k][1] -gt 0) { "total {0,-10} notes+rests found {1}/{2} ({3:F1}%)" -f $k, $total[$k][0], $total[$k][1], (100.0 * $total[$k][0] / $total[$k][1]) }
}
# this reader's count per set, kept (docs/<kind>-sets.tsv: committed with the run it records, as check.ps1's
# baseline is) so that the next run says which sets went up or down -- the totals alone hid elgar60-1's
# 539 -> 517 behind other sets' gains (ROADMAP section 7). A run of every set writes out/audiveris/<kind>-sets.tsv
# and compares it with the kept one; copy it over docs/ to keep the new numbers.
$kind = if ($Dev) { 'dev' } elseif ($Eval) { 'eval' } else { 'real' }
if ($Sets.Count -eq 0 -and $rows.Count -gt 0) {
    $rows | Export-Csv "$out\$kind-sets.tsv" -Delimiter "`t" -NoTypeInformation -UseQuotes Never
    $kept = "$root\docs\$kind-sets.tsv"
    if (Test-Path $kept) {
        $was = @{}; foreach ($r in Import-Csv $kept -Delimiter "`t") { $was[$r.set] = $r }
        $up = 0; $down = 0; $mUp = 0; $mDown = 0
        foreach ($r in $rows) {
            if (-not $was.ContainsKey($r.set)) { "  new     {0,-34} {1}/{2}" -f $r.set, $r.found, $r.truth; continue }
            $w = $was[$r.set]
            $d = $r.found - [int]$w.found
            if ($d -gt 0) { $up++; "  better  {0,-34} {1} -> {2} (+{3})" -f $r.set, $w.found, $r.found, $d }
            elseif ($d -lt 0) { $down++; "  WORSE   {0,-34} {1} -> {2} ({3})" -f $r.set, $w.found, $r.found, $d }
            # (ties and slurs: the truth's found, more is better, and those read wrong, fewer is better; a kept table
            # without them says nothing)
            foreach ($m in $marks) {
                $f = "$($m[1])Found"; $o = "$($m[1])Omr"; $g = "$($m[1])Right"
                if ("$($r.$f)" -eq '' -or "$($w.$f)" -eq '') { continue }
                $was2 = @([int]$w.$f, ([int]$w.$o - [int]$w.$g)); $now2 = @([int]$r.$f, ([int]$r.$o - [int]$r.$g))
                foreach ($i in 0, 1) {
                    $d = ($now2[$i] - $was2[$i]) * $(if ($i -eq 0) { 1 } else { -1 })
                    $what = "$($m[0]) $(if ($i -eq 0) { 'found' } else { 'read wrong' })"
                    if ($d -gt 0) { $mUp++; "  better  {0,-34} {1} {2} -> {3}" -f $r.set, $what, $was2[$i], $now2[$i] }
                    elseif ($d -lt 0) { $mDown++; "  WORSE   {0,-34} {1} {2} -> {3}" -f $r.set, $what, $was2[$i], $now2[$i] }
                }
            }
        }
        "sets against docs\$kind-sets.tsv: $up better, $down worse, $($rows.Count - $up - $down) the same; ties and slurs $mUp better, $mDown worse (copy out\audiveris\$kind-sets.tsv over it to keep these)"
    }
    else { "no docs\$kind-sets.tsv yet: copy out\audiveris\$kind-sets.tsv there to compare the next run with this one" }
}
