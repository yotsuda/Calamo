param(
    [string]$Out = "$PSScriptRoot\..\out\gen",
    [string]$Suite = 'all',
    [int]$Count = 5,
    [int]$Seed = 1,
    [int]$Bars = 16,
    [double]$Scale = 5.6,
    [switch]$Overlay,
    [switch]$Reuse,     # read back the pieces already in $Out
    [switch]$Redraw,    # engrave the pieces already in $Out again (a new lysc), degrade and read them; OmrGen not run
                        # (not a set made before 2026-10-28: its .lys is in the old octaves, a bare c as C4; make it again)
    [switch]$Degrade,   # make the pages look printed and scanned (OmrDegrade) before reading them
    [string]$DegradeArgs = '--scale 0.5 --blur 0.9 --ink 0.15 --paper 0.15 --noise 10 --specks 0.0003',
    [switch]$NoNumbers, # take the tuplet numbers off the pages (as an engraver that prints the first few only), before -Degrade
    [switch]$Verify,    # the redraw check (omr --verify): the .lys engraved again and compared with the page, bar by bar
    [string[]]$OmrArgs = @(),   # more arguments for omr (e.g. '--detector')
    [string[]]$GenArgs = @(),   # more arguments for OmrGen (e.g. '--lyrics')
    [string]$Fonts = ''         # music fonts drawn among, piece by piece (e.g. 'Emmentaler,Bravura,Petaluma,Leland'); '' = Lily#'s default
)
# Generate test scores, engrave them with Lily#, read them back with the OMR, and tabulate.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$gen = "$root\src\OmrGen\bin\Release\net10.0\OmrGen.exe"
$omr = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
$lysc = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe'

if ($Redraw) {
    # (the old drawing gone first: a piece may now take fewer pages)
    Get-ChildItem $Out -Recurse -File -Include 'p??.png', '*.svg', 'boxes-p??.tsv' | Remove-Item -Force
}
elseif (-not $Reuse) {
    if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
    $fontArgs = if ($Fonts) { @('--fonts', $Fonts) } else { @() }
    & $gen --out $Out --suite $Suite --count $Count --seed $Seed --bars $Bars @GenArgs @fontArgs | Out-Host
}
$sw = [Diagnostics.Stopwatch]::StartNew()
if (-not $Reuse) {
    cmd /d /s /c "`"$lysc`" png --scale $Scale --batch `"$Out\batch.txt`" -j 0 < NUL > `"$Out\lysc.log`" 2>&1"
    if ($LASTEXITCODE -ne 0) { Write-Warning "lysc reported failures; see $Out\lysc.log" }
    # the same pages as SVG: what Lily# drew, glyph by glyph, is the symbol-level truth
    cmd /d /s /c "`"$lysc`" svg --batch `"$Out\batch.txt`" -j 0 < NUL > `"$Out\lysc-svg.log`" 2>&1"
    # pages as pNN.png, as the reader expects
    foreach ($lys in Get-ChildItem $Out -Recurse -Filter *.lys | Where-Object { $_.Directory.Name -ne 'omr' }) {
        $dir = $lys.DirectoryName; $name = $lys.BaseName
        $pngs = @(Get-ChildItem $dir -Filter "$name*.png" | Sort-Object { if ($_.BaseName -match 'page(\d+)$') { [int]$Matches[1] } else { 1 } })
        if ($pngs.Count -eq 0) { Write-Warning "${name}: no pages"; continue }
        for ($i = 0; $i -lt $pngs.Count; $i++) { Move-Item -Force $pngs[$i].FullName (Join-Path $dir ('p{0:D2}.png' -f ($i + 1))) }
    }
    $deg = "$root\src\OmrDegrade\bin\Release\net10.0-windows\OmrDegrade.exe"
    if ($NoNumbers) {
        # the numbers' places from the SVG truth (a first reading's --export-boxes), their ink whited out
        Get-ChildItem $Out -Recurse -Filter *.lys | Where-Object { $_.Directory.Name -ne 'omr' } | ForEach-Object -ThrottleLimit ([Environment]::ProcessorCount) -Parallel {
            $dir = $_.DirectoryName; $name = $_.BaseName
            $pages = @(Get-ChildItem $dir -Filter 'p??.png').Count
            & $using:omr --config "$dir\$name.score" --pages $dir --from 1 --to $pages --out "$dir\omr" --svg "$dir\$name{page}.svg" --export-boxes --threads 1 | Out-Null
            for ($i = 1; $i -le $pages; $i++) {
                $png = Join-Path $dir ('p{0:D2}.png' -f $i)
                & $using:deg --in $png --out $png --erase (Join-Path "$dir\omr" ('boxes-p{0:D2}.tsv' -f $i)) --blur 0 --ink 0 --paper 0 --noise 0 --specks 0 | Out-Null
            }
        }
    }
    if ($Degrade) {
        $n = 0
        $rng = [Random]::new(1234)
        foreach ($png in Get-ChildItem $Out -Recurse -Filter 'p??.png') {
            # (a different seed per page, stable across runs)
            $n++
            if ($DegradeArgs -eq 'random') {
                # every page its own printer and scanner: the settings drawn at random, a third
                # of the pages cut to black and white as a scanner set that way does: at the page's
                # own level, a little lighter or darker (a fixed level of 0.3-0.55 rubbed out every
                # staff line and stem of a faint page, which no real scan does)
                $args = @('--scale', ('{0:F2}' -f (0.4 + 0.25 * $rng.NextDouble())), '--blur', ('{0:F2}' -f (0.6 + 1.2 * $rng.NextDouble())),
                          '--ink', ('{0:F2}' -f (0.05 + 0.3 * $rng.NextDouble())), '--paper', ('{0:F2}' -f (0.05 + 0.25 * $rng.NextDouble())),
                          '--noise', ('{0:F0}' -f (3 + 12 * $rng.NextDouble())), '--specks', '0.0003')
                if ($rng.NextDouble() -lt 0.33) { $args += @('--binarize-auto', ('{0:F2}' -f (-0.2 + 0.3 * $rng.NextDouble()))) }
                # (most printers lay the toner unevenly inside a head or a beam: the real scans do)
                if ($rng.NextDouble() -lt 0.7) { $args += @('--fill', ('{0:F2}' -f (0.2 + 0.6 * $rng.NextDouble())), '--fill-edge', ('{0:F1}' -f (2.5 + 3 * $rng.NextDouble()))) }
            } else { $args = @($DegradeArgs -split ' ') }
            & $deg --in $png.FullName --out $png.FullName @args --seed $n | Out-Null
        }
    }
}
$engrave = $sw.Elapsed.TotalSeconds
$sym = @{}

# read every piece back, several at once
$texts = Get-ChildItem $Out -Recurse -Filter *.lys | Where-Object { $_.Directory.Name -ne 'omr' } | ForEach-Object -ThrottleLimit ([Environment]::ProcessorCount) -Parallel {
    $dir = $_.DirectoryName; $name = $_.BaseName
    $pages = @(Get-ChildItem $dir -Filter 'p??.png').Count
    $a = @('--config', "$dir\$name.score", '--pages', $dir, '--from', '1', '--to', "$pages", '--out', "$dir\omr",
           '--truth', "$dir\$name-{part}-v*.notes", '--voices', "$dir\$name-{part}-v*.notes", '--poly',
           '--svg', "$dir\$name{page}.svg")
    if ($using:Overlay) { $a += '--overlay' }
    if ($using:Verify) { $a += '--verify', $using:lysc }
    $a += '--export-boxes'
    $a += '--threads', '1'    # (a reader per core: the detector's own threads would fight over them)
    $a += $using:OmrArgs
    # (no reading left from a run before: a reader that failed to start -- many at once on a busy machine --
    # left the last run's omr.lys to be scored, a check's false WORSE or better. Tried once more if none is written)
    Remove-Item "$dir\omr\omr.lys" -ErrorAction SilentlyContinue
    $text = & $using:omr @a 2>&1 | Out-String
    if (-not (Test-Path "$dir\omr\omr.lys")) { $text = & $using:omr @a 2>&1 | Out-String }
    if (-not (Test-Path "$dir\omr\omr.lys")) { Write-Warning "${name}: no reading written" }
    [pscustomobject]@{ suite = $_.Directory.Parent.Name; piece = $name; pages = $pages; text = $text }
}
$rows = @()
foreach ($t in $texts | Sort-Object suite, piece) {
    $text = $t.text
    $row = [ordered]@{ suite = $t.suite; piece = $t.piece; pages = $t.pages }
    if ($text -match 'signatures read: (\d+)/(\d+)') { $row.sigOk = [int]$Matches[1]; $row.sigAll = [int]$Matches[2] }
    if ($text -match 'notes\+rests: truth (\d+), omr (\d+); pitch\+duration found (\d+)') { $row.truth = [int]$Matches[1]; $row.omr = [int]$Matches[2]; $row.found = [int]$Matches[3] }
    if ($text -match 'right number of voices (\d+)') { $row.voiceCount = [int]$Matches[1] }
    if ($text -match 'staff-bars (\d+), exact') { $row.staffBars = [int]$Matches[1] }
    if ($text -match 'in the right voice and order (\d+)') { $row.inVoice = [int]$Matches[1] }
    # (the voice check counts events: a chord is one)
    if ($text -match 'events in voices: truth (\d+)') { $row.voiceTruth = [int]$Matches[1] }
    # the redraw check: part-bars flagged, part-bars the truth scores wrong, and both
    if ($text -match 'verify: input = \w+; (\d+) part-bars, (\d+) flagged') { $row.partBars = [int]$Matches[1]; $row.flagged = [int]$Matches[2] }
    if ($text -match 'verify vs truth: (\d+) part-bars wrong, \d+ flagged, (\d+) both') { $row.wrongBars = [int]$Matches[1]; $row.hit = [int]$Matches[2] }
    # symbols: head 120/123; head.pos 118/120; ...; head+ 2;
    if ($text -match 'symbols:(.*)') {
        foreach ($m in [regex]::Matches($Matches[1], '([\w.]+)(\+?) (\d+)(?:/(\d+))?;')) {
            $key = $m.Groups[1].Value + $m.Groups[2].Value
            $cell = "$($row.suite)|$key"
            if (-not $sym.ContainsKey($cell)) { $sym[$cell] = @(0, 0) }
            if ($m.Groups[4].Success) { $sym[$cell] = @(($sym[$cell][0] + [int]$m.Groups[3].Value), ($sym[$cell][1] + [int]$m.Groups[4].Value)) }
            else { $sym[$cell] = @(($sym[$cell][0] + [int]$m.Groups[3].Value), 0) }
        }
    }
    $rows += [pscustomobject]$row
}
$rows | Export-Csv -NoTypeInformation "$Out\results.csv"

function Pct($a, $b) { if ($b -eq 0) { 0 } else { [math]::Round(100.0 * $a / $b, 1) } }
"engraving: {0:F1}s for {1} pieces; total {2:F1}s" -f $engrave, $rows.Count, $sw.Elapsed.TotalSeconds
$rows | Group-Object suite | ForEach-Object {
    $g = $_.Group
    $t = ($g | Measure-Object truth -Sum).Sum; $o = ($g | Measure-Object omr -Sum).Sum; $f = ($g | Measure-Object found -Sum).Sum
    $v = ($g | Measure-Object inVoice -Sum).Sum; $vt = ($g | Measure-Object voiceTruth -Sum).Sum
    $so = ($g | Measure-Object sigOk -Sum).Sum; $sa = ($g | Measure-Object sigAll -Sum).Sum
    $vc = ($g | Measure-Object voiceCount -Sum).Sum; $sb = ($g | Measure-Object staffBars -Sum).Sum
    $r = [ordered]@{ suite = $_.Name; pieces = $g.Count; notes = $t; recall = (Pct $f $t); precision = (Pct $f $o); inVoice = (Pct $v $vt); voiceCount = (Pct $vc $sb); signatures = (Pct $so $sa) }
    if ($Verify) {
        # flagged: part-bars the redraw check points at; wrong: part-bars the truth scores wrong; hit: both.
        # A check that flags every wrong bar and nothing else has hit = wrong = flagged
        $r.flagged = ($g | Measure-Object flagged -Sum).Sum; $r.wrong = ($g | Measure-Object wrongBars -Sum).Sum; $r.hit = ($g | Measure-Object hit -Sum).Sum
    }
    [pscustomobject]$r
} | Format-Table -AutoSize | Out-String -Width 200

# symbol-level: per suite, each check as ok/all (misses), and found-without-truth as +n
$suites = $rows.suite | Sort-Object -Unique
$keys = $sym.Keys | ForEach-Object { ($_ -split '\|')[1] } | Sort-Object -Unique
$table = foreach ($k in $keys) {
    $r = [ordered]@{ check = $k }
    foreach ($su in $suites) {
        $v = $sym["$su|$k"]
        $r[$su] = if ($null -eq $v) { '' } elseif ($k.EndsWith('+')) { "$($v[0])" } else { "$($v[1] - $v[0])/$($v[1])" }
    }
    [pscustomobject]$r
}
"symbol misses (wrong/all; '+' rows: found with no truth)"
$table | Format-Table -AutoSize | Out-String -Width 220
$table | Export-Csv -NoTypeInformation "$Out\symbols.csv"
