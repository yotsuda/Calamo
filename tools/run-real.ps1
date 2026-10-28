param([string[]]$OmrArgs = @())    # more arguments for omr (e.g. '--detector')
# The real-score benchmarks: the scores the rules were first worked out on. Run after any change.
$root = Resolve-Path "$PSScriptRoot\.."
$omr = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
$out = "$root\out\real"
function Run($name, [string[]]$a, $pattern) {
    $a += $OmrArgs
    $text = & $omr @a 2>&1 | Out-String
    $lines = $text -split "`n" | Where-Object { $_ -match $pattern }
    "{0,-10} {1}" -f $name, (($lines | ForEach-Object { $_.Trim() }) -join ' | ')
}
Run 'bohemian' @('--config', "$root\data\bohemian\bohemian.score", '--pages', 'C:\MyProj\LilySharp-Lab\sessions\p661\bohemian\hi4', '--from', '1', '--to', '19', '--out', "$out\bohemian", '--truth', "$root\data\bohemian\truth.md") 'exact events|signatures'
# the same score read by the classifier alone, after it learnt the score's own font (M2's path)
Run 'bohemian-knn' @('--config', "$root\data\bohemian\bohemian.score", '--pages', 'C:\MyProj\LilySharp-Lab\sessions\p661\bohemian\hi4', '--from', '1', '--to', '19', '--out', "$out\bohemian-knn", '--truth', "$root\data\bohemian\truth.md", '--calibrate', '--knn-only') 'exact events|calibrated'
# generated pieces printed on paper and scanned (300 dpi, black-and-white): the real degradation, with its truth
foreach ($scan in Get-ChildItem "$root\data\print" -Directory | Where-Object { Test-Path "$($_.FullName)\scan\p01.png" }) {
    $p = $scan.Name; $dir = $scan.FullName
    Run "scan-$p" @('--config', "$dir\$p.score", '--pages', "$dir\scan", '--from', '1', '--to', '1', '--out', "$out\scan-$p", '--truth', "$dir\$p-{part}-v*.notes", '--voices', "$dir\$p-{part}-v*.notes", '--poly') 'notes\+rests|events in|signatures'
}
Run 'ninetofive' @('--config', "$root\data\ninetofive\ninetofive.score", '--pages', "$root\data\ninetofive", '--from', '1', '--to', '3', '--out', "$out\ninetofive", '--truth', "$root\data\ninetofive\page-Bass.notes") 'exact events|signatures'
Run 'bwv610' @('--config', "$root\data\bwv610\bwv610.score", '--pages', "$root\data\bwv610", '--from', '1', '--to', '2', '--out', "$out\bwv610", '--truth', "$root\data\bwv610\page-{part}.notes", '--voices', "$root\data\bwv610\voices-{part}-v*.notes", '--poly') 'notes\+rests|events in|signatures'
Run 'fugue1' @('--config', "$root\data\wtk1\fugue1.score", '--pages', "$root\data\wtk1", '--from', '1', '--to', '2', '--out', "$out\fugue1", '--truth', "$root\data\wtk1\page-{part}-v*.notes", '--voices', "$root\data\wtk1\page-{part}-v*.notes", '--poly') 'notes\+rests|events in|signatures'
