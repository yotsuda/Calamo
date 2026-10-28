param(
    [string]$Out = "$PSScriptRoot\..\out\ms-eval",   # drawn by run-ms.ps1 (-Sets gen -PerPiece 8: the evaluation pieces in every font)
    [string[]]$Fonts = @(),                          # only these fonts (folder names, e.g. Leland, FinaleBroadway)
    [string]$Suite = '',                             # only this suite
    [switch]$Overlay,
    [switch]$Notes,                                  # score against the piece's own .notes (band pieces: the MusicXML truth knows no TAB or drums yet)
    [string[]]$OmrArgs = @()                         # more arguments for omr (e.g. '--detector')
)
# Other fonts, note by note (B4a): the pages MuseScore drew of the generated evaluation pieces
# (seed 7) are read by omr and scored against the MusicXML they were drawn from. The .score
# comes from the piece's own set (out\<set>\<suite>\<piece>). Per font and suite, as run-gen's table.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$omr = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
$Out = (Resolve-Path $Out).Path
$OmrArgs = @($OmrArgs | ForEach-Object { $_ -split ',' })   # (pwsh -File passes a list as one string)
$Fonts = @($Fonts | ForEach-Object { $_ -split ',' })
$sw = [Diagnostics.Stopwatch]::StartNew()

# <out>\<font>\<set>-<suite>\<piece>\<piece>.xml
$pieces = Get-ChildItem $Out -Recurse -Filter *.xml | Where-Object { $_.Directory.Name -ne 'omr' -and $_.BaseName -eq $_.Directory.Name } | ForEach-Object {
    $font = $_.Directory.Parent.Parent.Name
    # the suite has no dash, a set may (gen-band)
    $set, $suite = $_.Directory.Parent.Name -split '-(?=[^-]+$)'
    if (($Fonts.Count -eq 0 -or $Fonts -contains $font) -and ($Suite -eq '' -or $Suite -eq $suite)) {
        [pscustomobject]@{ Xml = $_.FullName; Dir = $_.DirectoryName; Name = $_.BaseName; Font = $font; Suite = $suite; Score = "$root\out\$set\$suite\$($_.BaseName)\$($_.BaseName).score" }
    }
}
$texts = $pieces | ForEach-Object -ThrottleLimit ([Environment]::ProcessorCount) -Parallel {
    $pages = @(Get-ChildItem $_.Dir -Filter 'p??.png').Count
    $truth = @('--truth', $_.Xml)
    if ($using:Notes) {
        $n = $_.Score -replace '\.score$', '-{part}-v*.notes'
        $truth = @('--truth', $n, '--voices', $n)
    }
    $a = @('--config', $_.Score, '--pages', $_.Dir, '--from', '1', '--to', "$pages", '--out', "$($_.Dir)\omr") + $truth + @('--poly', '--threads', '1')
    if ($using:Overlay) { $a += '--overlay' }
    $a += $using:OmrArgs
    $text = & $using:omr @a 2>&1 | Out-String
    Set-Content -LiteralPath "$($_.Dir)\omr\log.txt" $text
    [pscustomobject]@{ font = $_.Font; suite = $_.Suite; piece = $_.Name; pages = $pages; text = $text }
}
$rows = foreach ($t in $texts | Sort-Object font, suite, piece) {
    $text = $t.text
    $row = [ordered]@{ font = $t.font; suite = $t.suite; piece = $t.piece; pages = $t.pages }
    if ($text -match 'bar length ok: (\d+)/(\d+)') { $row.barsOk = [int]$Matches[1]; $row.bars = [int]$Matches[2] }
    if ($text -match 'signatures read: (\d+)/(\d+)') { $row.sigOk = [int]$Matches[1]; $row.sigAll = [int]$Matches[2] }
    if ($text -match 'notes\+rests: truth (\d+), omr (\d+); pitch\+duration found (\d+)') { $row.truth = [int]$Matches[1]; $row.omr = [int]$Matches[2]; $row.found = [int]$Matches[3] }
    if ($text -match 'in the right voice and order (\d+)') { $row.inVoice = [int]$Matches[1] }
    if ($text -match 'events in voices: truth (\d+)') { $row.voiceTruth = [int]$Matches[1] }
    if ($null -eq $row.truth) { Write-Warning "$($t.font) $($t.piece): no score`n$($text.Trim() -split "`n" | Select-Object -Last 3 | Out-String)" }
    [pscustomobject]$row
}
$rows | Export-Csv -NoTypeInformation "$Out\results.csv"

function Pct($a, $b) { if ($b -eq 0) { 0 } else { [math]::Round(100.0 * $a / $b, 1) } }
function Sum($g, $p) { ($g | Measure-Object $p -Sum).Sum }
"read {0} pieces in {1:F1}s" -f @($rows).Count, $sw.Elapsed.TotalSeconds
# recall per font (rows) and suite (columns), and each font's totals
$suites = $rows.suite | Sort-Object -Unique
$summary = $rows | Group-Object font | ForEach-Object {
    $g = $_.Group
    $r = [ordered]@{ font = $_.Name }
    foreach ($s in $suites) { $x = $g | Where-Object suite -eq $s; $r[$s] = Pct (Sum $x found) (Sum $x truth) }
    $r.recall = Pct (Sum $g found) (Sum $g truth); $r.precision = Pct (Sum $g found) (Sum $g omr)
    $r.inVoice = Pct (Sum $g inVoice) (Sum $g voiceTruth); $r.barsOk = Pct (Sum $g barsOk) (Sum $g bars); $r.sigs = Pct (Sum $g sigOk) (Sum $g sigAll)
    [pscustomobject]$r
}
$summary | Export-Csv -NoTypeInformation "$Out\summary.csv"
$summary | Format-Table -Property * -AutoSize | Out-String -Width 250
