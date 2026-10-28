param(
    [switch]$Update,    # accept the current numbers as the new baseline (runs everything again)
    [switch]$Accept,    # accept the numbers of the last run as the new baseline, without running (after a run that showed WORSE lines you can explain)
    [switch]$Fresh,     # regenerate and re-engrave the generated set first
    [switch]$Detector   # the learnt detector's heads (--detector) instead of the rules': its own baseline
)
# The regression gate: the generated set (seed 7, 10 per suite) and the real benchmarks, compared
# with docs/baseline.json (docs/baseline-detector.json with -Detector). Run it at the start of a
# session and before every commit. Every run leaves its numbers in out/check-last(-detector).json.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$gen = "$root\out\gen"
$basePath = if ($Detector) { "$root\docs\baseline-detector.json" } else { "$root\docs\baseline.json" }
$lastPath = if ($Detector) { "$root\out\check-last-detector.json" } else { "$root\out\check-last.json" }
if ($Accept) {
    if (-not (Test-Path $lastPath)) { throw "no last run: $lastPath" }
    Copy-Item $lastPath $basePath -Force
    "baseline written from the last run ($((Get-Item $lastPath).LastWriteTime.ToString('HH:mm'))) -> $basePath"
    return
}
$omrArgs = if ($Detector) { @('--detector') } else { @() }

$gen3 = "$root\out\gen-s3"    # the same pieces engraved at scale 3 (about 160 dpi)
# (with the detector the scale 5.6 set also runs the redraw check, --verify: a few seconds more)
if ($Fresh -or -not (Test-Path "$gen\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Count 10 -Seed 7 -Verify:$Detector -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Reuse -Verify:$Detector -OmrArgs $omrArgs | Out-Null }
if ($Fresh -or -not (Test-Path "$gen3\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $gen3 -Count 10 -Seed 7 -Scale 3 -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $gen3 -Reuse -OmrArgs $omrArgs | Out-Null }
$genS = "$root\out\gen-scan"   # the same pieces as if printed and scanned (OmrDegrade, about 270 dpi)
if ($Fresh -or -not (Test-Path "$genS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genS -Count 10 -Seed 7 -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genS -Reuse -OmrArgs $omrArgs | Out-Null }
# songs (OmrGen's lied): a voice over a piano, its staff hidden in the systems where it rests
$genL = "$root\out\gen-lied"
if ($Fresh -or -not (Test-Path "$genL\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genL -Suite lied -Count 10 -Seed 7 -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genL -Reuse -OmrArgs $omrArgs | Out-Null }
# the same songs as printed songs have them: words, dynamics and hairpins, a tempo marking and
# words of expression, a fuller piano, a change of key or meter, grace notes
$genA = "$root\out\gen-lied-all"
$liedArgs = @('--lyrics', '--dynamics', '--texts', '--piano', '--changes', '--grace')
# (--header: the words of expression, "dolce", "rit.", are read by the OCR, which runs with it: textA below)
$liedOmr = @($omrArgs) + @('--header')
if ($Fresh -or -not (Test-Path "$genA\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genA -Suite lied -Count 10 -Seed 7 -GenArgs $liedArgs -OmrArgs $liedOmr | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genA -Reuse -OmrArgs $liedOmr | Out-Null }
# the same suites with stretches where every part rests, written as multi-bar rests (R1*N): church
# rests and the count over the staff, the bars counted on past them
$genR = "$root\out\gen-mmrests"
if ($Fresh -or -not (Test-Path "$genR\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genR -Count 10 -Seed 7 -GenArgs '--mmrests' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genR -Reuse -OmrArgs $omrArgs | Out-Null }
# (and printed and scanned, as gen-scan)
$genRS = "$root\out\gen-mmrests-scan"
if ($Fresh -or -not (Test-Path "$genRS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genRS -Count 10 -Seed 7 -GenArgs '--mmrests' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genRS -Reuse -OmrArgs $omrArgs | Out-Null }
# the tuplets suite with the other tuplet numbers (OmrGen --tuplets: quintuplets, sextuplets and
# septuplets of sixteenths among the triplets), clean and printed and scanned
$genT = "$root\out\gen-tup"
if ($Fresh -or -not (Test-Path "$genT\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genT -Suite tuplets -Count 10 -Seed 7 -GenArgs '--tuplets' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genT -Reuse -OmrArgs $omrArgs | Out-Null }
$genTS = "$root\out\gen-tup-scan"
if ($Fresh -or -not (Test-Path "$genTS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genTS -Suite tuplets -Count 10 -Seed 7 -GenArgs '--tuplets' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genTS -Reuse -OmrArgs $omrArgs | Out-Null }
# octave lines (OmrGen --ottava: 8va, 8vb, 15ma and 15mb over stretches of the notes, which are written
# as they sound) on the basic, ledger and piano suites, clean and printed and scanned
# (the suites as one string: an array would be bound to run-gen's [string] joined by spaces)
$genO = "$root\out\gen-ott"
if ($Fresh -or -not (Test-Path "$genO\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genO -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--ottava' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genO -Reuse -OmrArgs $omrArgs | Out-Null }
$genOS = "$root\out\gen-ott-scan"
if ($Fresh -or -not (Test-Path "$genOS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genOS -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--ottava' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genOS -Reuse -OmrArgs $omrArgs | Out-Null }
# grace notes (OmrGen --graces: acciaccaturas, appoggiaturas, two or three sixteenths before the notes)
# on the basic, chords and piano suites, clean and printed and scanned (S21; tools/score-marks.ps1
# counts the grace notes themselves, which the note truth leaves out)
$genG = "$root\out\gen-grace"
if ($Fresh -or -not (Test-Path "$genG\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genG -Suite 'basic,chords,piano' -Count 10 -Seed 7 -GenArgs '--graces' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genG -Reuse -OmrArgs $omrArgs | Out-Null }
$genGS = "$root\out\gen-grace-scan"
if ($Fresh -or -not (Test-Path "$genGS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genGS -Suite 'basic,chords,piano' -Count 10 -Seed 7 -GenArgs '--graces' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genGS -Reuse -OmrArgs $omrArgs | Out-Null }
# the form (OmrGen --repeats: a stretch played twice, with or without a first and second ending, or a
# D.S. al Coda) on the basic, chords and piano suites, clean and printed and scanned (S22;
# tools/score-marks.ps1 counts the repeat signs, the endings and the jumps by bar: formP / formPS below)
$genP = "$root\out\gen-rep"
# (--header: the jumps' and ends' words, "Fine", "D.C. al Fine", "D.S. al Coda", are read by the OCR, which runs with it; about a second a page)
# (as arrays: $omrArgs from the if above is a lone string, and a string + a string is one argument, "--detector--header")
$repArgs = @($omrArgs) + @('--header')
if ($Fresh -or -not (Test-Path "$genP\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genP -Suite 'basic,chords,piano' -Count 10 -Seed 7 -GenArgs '--repeats' -OmrArgs $repArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genP -Reuse -OmrArgs $repArgs | Out-Null }
$genPS = "$root\out\gen-rep-scan"
if ($Fresh -or -not (Test-Path "$genPS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genPS -Suite 'basic,chords,piano' -Count 10 -Seed 7 -GenArgs '--repeats' -Degrade -OmrArgs $repArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genPS -Reuse -OmrArgs $repArgs | Out-Null }
# ornaments (OmrGen --ornaments: a trill, mordent, prall, turn or reverse turn over a note in ten) on the
# basic, ledger and piano suites, clean and printed and scanned (S23; tools/score-marks.ps1 counts them
# by name: ornN / ornNS below)
$genN = "$root\out\gen-orn"
if ($Fresh -or -not (Test-Path "$genN\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genN -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--ornaments' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genN -Reuse -OmrArgs $omrArgs | Out-Null }
$genNS = "$root\out\gen-orn-scan"
if ($Fresh -or -not (Test-Path "$genNS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genNS -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--ornaments' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genNS -Reuse -OmrArgs $omrArgs | Out-Null }
# the rarer scripts (OmrGen --scripts2: a portato, stopped + or pralltriller on a note in twelve) on the
# basic, ledger and piano suites, clean and printed and scanned (scored by name like the ornaments: scrK / scrKS below)
$genK = "$root\out\gen-s2"
if ($Fresh -or -not (Test-Path "$genK\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genK -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--scripts2' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genK -Reuse -OmrArgs $omrArgs | Out-Null }
$genKS = "$root\out\gen-s2-scan"
if ($Fresh -or -not (Test-Path "$genKS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genKS -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--scripts2' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genKS -Reuse -OmrArgs $omrArgs | Out-Null }
# text spanners (OmrGen --spans: rit., accel., rall. and words with a dashed line after them over two to
# twelve notes, every part's) on the basic, ledger and piano suites, clean and printed and scanned
# (S25; read with the OCR, --header; tools/score-marks.ps1 counts their starts and ends by bar: spanX / spanXS below)
$spanArgs = @($omrArgs) + @('--header')
$genX = "$root\out\gen-span"
if ($Fresh -or -not (Test-Path "$genX\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genX -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--spans' -OmrArgs $spanArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genX -Reuse -OmrArgs $spanArgs | Out-Null }
$genXS = "$root\out\gen-span-scan"
if ($Fresh -or -not (Test-Path "$genXS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genXS -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--spans' -Degrade -OmrArgs $spanArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genXS -Reuse -OmrArgs $spanArgs | Out-Null }
# trill lines (OmrGen --trills: a "tr" and a wavy line over one to six notes, every part's) on the
# basic, ledger and piano suites, clean and printed and scanned (tools/score-marks.ps1 counts their
# starts and ends by bar: trillW / trillWS below; read with the detector only)
$genW = "$root\out\gen-trill"
if ($Fresh -or -not (Test-Path "$genW\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genW -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--trills' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genW -Reuse -OmrArgs $omrArgs | Out-Null }
$genWS = "$root\out\gen-trill-scan"
if ($Fresh -or -not (Test-Path "$genWS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genWS -Suite 'basic,ledger,piano' -Count 10 -Seed 7 -GenArgs '--trills' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genWS -Reuse -OmrArgs $omrArgs | Out-Null }
# arpeggios (OmrGen --arpeggios: a wavy line before a chord in four) on the chords and piano suites,
# clean and printed and scanned (tools/score-marks.ps1 counts them by bar: arpY / arpYS below; read by
# rule, with or without the detector)
$genY = "$root\out\gen-arp"
if ($Fresh -or -not (Test-Path "$genY\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genY -Suite 'chords,piano' -Count 10 -Seed 7 -GenArgs '--arpeggios' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genY -Reuse -OmrArgs $omrArgs | Out-Null }
$genYS = "$root\out\gen-arp-scan"
if ($Fresh -or -not (Test-Path "$genYS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genYS -Suite 'chords,piano' -Count 10 -Seed 7 -GenArgs '--arpeggios' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genYS -Reuse -OmrArgs $omrArgs | Out-Null }
# glissandi (OmrGen --glissandi: a straight line from a note to the next in its bar) on the basic,
# ledger, chords, voices2 and piano suites, clean and printed and scanned (tools/score-marks.ps1 counts
# them by bar: glZ / glZS below; read by rule, with or without the detector)
$genZ = "$root\out\gen-gliss"
if ($Fresh -or -not (Test-Path "$genZ\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genZ -Suite 'basic,ledger,chords,voices2,piano' -Count 10 -Seed 7 -GenArgs '--glissandi' -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genZ -Reuse -OmrArgs $omrArgs | Out-Null }
$genZS = "$root\out\gen-gliss-scan"
if ($Fresh -or -not (Test-Path "$genZS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genZS -Suite 'basic,ledger,chords,voices2,piano' -Count 10 -Seed 7 -GenArgs '--glissandi' -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genZS -Reuse -OmrArgs $omrArgs | Out-Null }
# triplets with no number printed (run-gen -NoNumbers: the tuplets suite with its numbers whited out,
# as an engraver leaves them off after the first few), read from the bars' lengths; clean and printed and scanned
$genU = "$root\out\gen-tupn"
if ($Fresh -or -not (Test-Path "$genU\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genU -Suite tuplets -Count 10 -Seed 7 -NoNumbers -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genU -Reuse -OmrArgs $omrArgs | Out-Null }
$genUS = "$root\out\gen-tupn-scan"
if ($Fresh -or -not (Test-Path "$genUS\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genUS -Suite tuplets -Count 10 -Seed 7 -NoNumbers -Degrade -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genUS -Reuse -OmrArgs $omrArgs | Out-Null }
# pages printed small (OmrDegrade --scale 0.13 and 0.18: a space of 7 and 10 pixels, IMSLP's 96 dpi
# pages and about 140 dpi), read enlarged (Img.Load); the basic and piano suites
$loArgs = '--blur 0.4 --ink 0.05 --paper 0.05 --noise 3 --specks 0'
$genLo7 = "$root\out\gen-lo7"
if ($Fresh -or -not (Test-Path "$genLo7\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genLo7 -Suite 'basic,piano' -Count 10 -Seed 7 -Degrade -DegradeArgs "--scale 0.13 $loArgs" -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genLo7 -Reuse -OmrArgs $omrArgs | Out-Null }
$genLo10 = "$root\out\gen-lo10"
if ($Fresh -or -not (Test-Path "$genLo10\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genLo10 -Suite 'basic,piano' -Count 10 -Seed 7 -Degrade -DegradeArgs "--scale 0.18 $loArgs" -OmrArgs $omrArgs | Out-Null }
else { & "$PSScriptRoot\run-gen.ps1" -Out $genLo10 -Reuse -OmrArgs $omrArgs | Out-Null }
$sets =@(@{ Dir = $gen; Tag = 'gen'; Sym = 'sym' }, @{ Dir = $genLo7; Tag = 'genLo7'; Sym = 'symLo7' }, @{ Dir = $genLo10; Tag = 'genLo10'; Sym = 'symLo10' }, @{ Dir = $gen3; Tag = 'gen3'; Sym = 'sym3' }, @{ Dir = $genS; Tag = 'genS'; Sym = 'symS' }, @{ Dir = $genL; Tag = 'genL'; Sym = 'symL' },
          @{ Dir = $genA; Tag = 'genA'; Sym = 'symA' }, @{ Dir = $genR; Tag = 'genR'; Sym = 'symR' }, @{ Dir = $genRS; Tag = 'genRS'; Sym = 'symRS' },
          @{ Dir = $genT; Tag = 'genT'; Sym = 'symT' }, @{ Dir = $genTS; Tag = 'genTS'; Sym = 'symTS' }, @{ Dir = $genO; Tag = 'genO'; Sym = 'symO' }, @{ Dir = $genOS; Tag = 'genOS'; Sym = 'symOS' },
          @{ Dir = $genG; Tag = 'genG'; Sym = 'symG' }, @{ Dir = $genGS; Tag = 'genGS'; Sym = 'symGS' }, @{ Dir = $genP; Tag = 'genP'; Sym = 'symP' }, @{ Dir = $genPS; Tag = 'genPS'; Sym = 'symPS' },
          @{ Dir = $genN; Tag = 'genN'; Sym = 'symN' }, @{ Dir = $genNS; Tag = 'genNS'; Sym = 'symNS' },
          @{ Dir = $genK; Tag = 'genK'; Sym = 'symK' }, @{ Dir = $genKS; Tag = 'genKS'; Sym = 'symKS' },
          @{ Dir = $genX; Tag = 'genX'; Sym = 'symX' }, @{ Dir = $genXS; Tag = 'genXS'; Sym = 'symXS' },
           @{ Dir = $genW; Tag = 'genW'; Sym = 'symW' }, @{ Dir = $genWS; Tag = 'genWS'; Sym = 'symWS' },
           @{ Dir = $genY; Tag = 'genY'; Sym = 'symY' }, @{ Dir = $genYS; Tag = 'genYS'; Sym = 'symYS' },
           @{ Dir = $genZ; Tag = 'genZ'; Sym = 'symZ' }, @{ Dir = $genZS; Tag = 'genZS'; Sym = 'symZS' },
           @{ Dir = $genU; Tag = 'genU'; Sym = 'symU' }, @{ Dir = $genUS; Tag = 'genUS'; Sym = 'symUS' })
if ($Detector) {
    # printed with toner that thins inside a head (OmrDegrade --fill, as the real scans are): the
    # rules read a tenth of it, so only the detector is measured on it
    $genF = "$root\out\gen-scan-fill"
    $fillArgs = '--scale 0.5 --blur 0.9 --ink 0.25 --paper 0.15 --noise 10 --specks 0.0003 --fill 0.6 --fill-edge 4'
    if ($Fresh -or -not (Test-Path "$genF\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genF -Count 10 -Seed 7 -Degrade -DegradeArgs $fillArgs -OmrArgs $omrArgs | Out-Null }
    else { & "$PSScriptRoot\run-gen.ps1" -Out $genF -Reuse -OmrArgs $omrArgs | Out-Null }
    $sets += @{ Dir = $genF; Tag = 'genF'; Sym = 'symF' }
    # band scores (B3): vocal with chord names and lyrics, guitar and bass with TABs, drums, percent
    # repeats; the detector's cross heads and percent signs are read only with it
    $genB = "$root\out\gen-band"
    if ($Fresh -or -not (Test-Path "$genB\batch.txt")) { & "$PSScriptRoot\run-gen.ps1" -Out $genB -Suite band -Count 10 -Seed 7 -OmrArgs $omrArgs | Out-Null }
    else { & "$PSScriptRoot\run-gen.ps1" -Out $genB -Reuse -OmrArgs $omrArgs | Out-Null }
    $sets += @{ Dir = $genB; Tag = 'genB'; Sym = 'symB' }
}
$real = & "$PSScriptRoot\run-real.ps1" -OmrArgs $omrArgs | Out-String

$m = [ordered]@{}
foreach ($set in $sets) {
    # generated: per suite, the note-level numbers (higher is better)...
    $rows = Import-Csv "$($set.Dir)\results.csv"
    foreach ($g in $rows | Group-Object suite) {
        $t = ($g.Group | Measure-Object truth -Sum).Sum; $o = ($g.Group | Measure-Object omr -Sum).Sum
        $f = ($g.Group | Measure-Object found -Sum).Sum; $v = ($g.Group | Measure-Object inVoice -Sum).Sum; $vt = ($g.Group | Measure-Object voiceTruth -Sum).Sum
        $m["$($set.Tag).$($g.Name).recall"] = [math]::Round(100.0 * $f / $t, 2)
        $m["$($set.Tag).$($g.Name).precision"] = [math]::Round(100.0 * $f / $o, 2)
        $m["$($set.Tag).$($g.Name).inVoice"] = [math]::Round(100.0 * $v / $vt, 2)
        # the redraw check (lower is better): part-bars flagged that the truth scores right, and
        # part-bars scored wrong that it did not flag
        if ($g.Group[0].PSObject.Properties.Name -contains 'flagged' -and $g.Group[0].flagged -ne '') {
            $fl = ($g.Group | Measure-Object flagged -Sum).Sum; $wr = ($g.Group | Measure-Object wrongBars -Sum).Sum; $hi = ($g.Group | Measure-Object hit -Sum).Sum
            $m["ver.$($g.Name).spare"] = [int]($fl - $hi)
            $m["ver.$($g.Name).missed"] = [int]($wr - $hi)
        }
    }
    # ...and the symbol-level misses (lower is better)
    foreach ($r in Import-Csv "$($set.Dir)\symbols.csv") {
        foreach ($p in $r.PSObject.Properties | Where-Object Name -ne 'check') {
            if ($p.Value -eq '') { continue }
            $miss = [int](($p.Value -split '/')[0])
            $m["$($set.Sym).$($p.Name).$($r.check)"] = $miss
        }
    }
}
# the form's signs read (higher is better): the repeat barlines, the endings and the jumps, by bar;
# the ornaments, by note; the songs' words of expression and dynamics, by bar
foreach ($set in @(@{ Dir = $genP; Tag = 'formP'; Kinds = 'repeat|volta|nav' }, @{ Dir = $genPS; Tag = 'formPS'; Kinds = 'repeat|volta|nav' },
                   @{ Dir = $genN; Tag = 'ornN'; Kinds = 'ornament' }, @{ Dir = $genNS; Tag = 'ornNS'; Kinds = 'ornament' },
                   @{ Dir = $genK; Tag = 'scrK'; Kinds = 'scripts2' }, @{ Dir = $genKS; Tag = 'scrKS'; Kinds = 'scripts2' },
                   @{ Dir = $genA; Tag = 'textA'; Kinds = 'text|dynamic' },
                   @{ Dir = $genX; Tag = 'spanX'; Kinds = 'spanend|span' }, @{ Dir = $genXS; Tag = 'spanXS'; Kinds = 'spanend|span' },
                   @{ Dir = $genW; Tag = 'trillW'; Kinds = 'trillend|trill' }, @{ Dir = $genWS; Tag = 'trillWS'; Kinds = 'trillend|trill' },
                   @{ Dir = $genY; Tag = 'arpY'; Kinds = 'arpeggio' }, @{ Dir = $genYS; Tag = 'arpYS'; Kinds = 'arpeggio' },
                   @{ Dir = $genZ; Tag = 'glZ'; Kinds = 'glissando' }, @{ Dir = $genZS; Tag = 'glZS'; Kinds = 'glissando' })) {
    foreach ($line in (& "$PSScriptRoot\score-marks.ps1" -Out $set.Dir)) {
        if ($line -match "^($($set.Kinds))\s+truth\s+\d+\s+read\s+\d+\s+found\s+\d+\s+recall\s+([\d.]+)%\s+precision\s+([\d.]+)%") {
            $m["$($set.Tag).$($Matches[1]).recall"] = [double]$Matches[2]; $m["$($set.Tag).$($Matches[1]).precision"] = [double]$Matches[3]
        }
    }
}
# real scores (higher is better)
function Grab($name, $pattern, $key) {
    $line = ($real -split "`n") | Where-Object { $_ -match "^$name\s" }
    if ($line -match $pattern) { $m["real.$name.$key"] = [double]$Matches[1] }
}
Grab 'bohemian' 'exact events \d+ \(([\d.]+)%' 'exact'
Grab 'bohemian-knn' 'exact events \d+ \(([\d.]+)%' 'exact'
Grab 'ninetofive' 'exact events \d+ \(([\d.]+)%' 'exact'
$scans = Get-ChildItem "$root\data\print" -Directory | Where-Object { Test-Path "$($_.FullName)\scan\p01.png" } | ForEach-Object { "scan-$($_.Name)" }
foreach ($n in @('bwv610', 'fugue1') + $scans) {
    Grab $n 'recall ([\d.]+)%, precision' 'recall'
    Grab $n 'precision ([\d.]+)%' 'precision'
    Grab $n 'right voice and order \d+ \(([\d.]+)%' 'inVoice'
}

$m | ConvertTo-Json | Set-Variable json
[IO.File]::WriteAllText($lastPath, $json)
if ($Update -or -not (Test-Path $basePath)) {
    [IO.File]::WriteAllText($basePath, $json)
    "baseline written: $($m.Count) numbers -> $basePath"
    return
}
$base = Get-Content -Raw $basePath | ConvertFrom-Json -AsHashtable
$worse = @(); $better = @()
foreach ($k in $m.Keys) {
    $new = $m[$k]; $old = if ($base.ContainsKey($k)) { $base[$k] } else { $null }
    if ($null -eq $old) { $better += "  new     $k = $new"; continue }
    $lowerBetter = $k -match '^sym[^.]*\.' -or $k.StartsWith('ver.')    # (every symbol count is a count of errors)
    $d = $new - $old
    if ($lowerBetter) { if ($d -gt 0) { $worse += "  WORSE   $k $old -> $new" } elseif ($d -lt 0) { $better += "  better  $k $old -> $new" } }
    else { if ($d -lt -0.05) { $worse += "  WORSE   $k $old -> $new" } elseif ($d -gt 0.05) { $better += "  better  $k $old -> $new" } }
}
foreach ($k in $base.Keys) {
    if ($m.Contains($k)) { continue }
    # a symbol count that is gone from the table is zero now
    if ($k -match '^sym[^.]*\.') { if ($base[$k] -gt 0) { $better += "  better  $k $($base[$k]) -> 0" } }
    else { $worse += "  MISSING $k (was $($base[$k]))" }
}
$better
$worse
if ($worse.Count -eq 0) { "check: OK ($($better.Count) better, nothing worse)" }
else { "check: $($worse.Count) WORSE - look before committing (-Update accepts them as the new baseline)" }
