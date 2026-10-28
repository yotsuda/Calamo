param(
    [string]$Out = "$PSScriptRoot\..\out\lab-sections",
    [switch]$Reuse      # the pages engraved last time
)
# The Lab's pieces with sections (rehearsal marks: words or a letter in a box): each engraved by lysc at
# scale 5.6 and read with omr read; its form line against the plays the .lys's form prints. (05-form's form
# repeats Verse, read as Verse_2; variations' marks are printed "Var. I": the names differ, the reading is right.)
$ErrorActionPreference = 'Stop'
$lysc = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe'
$omr = "$PSScriptRoot\..\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
$lab = 'C:\MyProj\LilySharp-Lab\corpora\dogfood'
$pieces = [ordered]@{
    'quartet-collide' = "$lab\collide\quartet-collide.lys"; 'piano-collide' = "$lab\collide\piano-collide.lys"
    'leadsheet-collide' = "$lab\collide\leadsheet-collide.lys"; 'variations' = "$lab\variations.lys"; 'roadsong' = "$lab\roadsong.lys"
    'sarabande' = "$lab\sarabande.lys"; '05-form' = "$lab\uxeval2\05-form.lys"; '05-song' = "$lab\uxeval\05-song.lys"
    'piano-sonatina' = "$lab\big\piano-sonatina.lys"; 'choir-chorale' = "$lab\big\choir-chorale.lys"
}
# a form line's signs in order, its section names left out ("|: [1-2. ] :| segno to coda ds al coda coda fine ...")
function Signs([string]$form) {
    $f = $form -replace '\bto coda\b', 'tocoda' -replace '\b(ds|dc) al (coda|fine)\b', '$1al$2' -replace '\[', ' [' -replace '\]', ' ] '
    # (not the final barline |., which the reading does not write)
    (($f -split '\s+') | Where-Object { $_ -match '^(\|:|:\||\[[\d,\-]+\.|\]|segno|coda|tocoda|fine|ds|dc|dsalcoda|dcalcoda|dsalfine|dcalfine)$' }) -join ' '
}
foreach ($n in $pieces.Keys) {
    $d = "$Out\$n"
    New-Item -ItemType Directory -Force $d | Out-Null
    if (-not $Reuse -or -not (Get-ChildItem $d -Filter "$n*.png")) {
        Copy-Item $pieces[$n] "$d\$n.lys" -Force
        cmd /d /s /c "`"$lysc`" png --scale 5.6 `"$d\$n.lys`" < NUL > `"$d\lysc.log`" 2>&1"
    }
    # (a piece with a named score: <piece>-<score>.png; several pages: <base>-pageN.png)
    $base = if ((Test-Path "$d\$n.png") -or (Test-Path "$d\$n-page1.png")) { $n } else { (Get-ChildItem $d -Filter "$n-*.png" | Sort-Object Name | Select-Object -First 1).BaseName -replace '-page\d+$', '' }
    $pngs = @(Get-ChildItem $d -Filter "$base*.png" | Where-Object { $_.BaseName -eq $base -or $_.BaseName -match "^$base-page\d+$" } | Sort-Object { if ($_.BaseName -match 'page(\d+)$') { [int]$Matches[1] } else { 1 } } | ForEach-Object { "`"$($_.FullName)`"" })
    if (Test-Path "$d\omr") { Remove-Item -Recurse -Force "$d\omr" }
    cmd /d /s /c "`"$omr`" read $($pngs -join ' ') --out `"$d\omr`" < NUL > `"$d\omr.log`" 2>&1"
    # (what the page prints: the form's plays in order, less the '~' ones -- a tilde on a play hides its
    # label, as leadsheet-collide's [1-2. ~Bridge] and [3. ~Tag]; with no form, every section labels itself)
    $sections = @(Select-String -Path $pieces[$n] -Pattern '^section (\S+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
    $src = Get-Content $pieces[$n] -Raw
    # (the volta's brackets split off: '[1. First]'; the names' case kept: the keyword coda is no section Coda)
    $plays = if ($src -match '(?ms)^form\s*\{(.*?)\}') { @(($Matches[1] -split '[\s\[\]]+') | Where-Object { $sections -ccontains $_ }) } else { $sections }
    $want = $plays -join ' '
    # (the reading's form less its repeat signs, brackets and jumps (S22): its section names only)
    $got = if (Test-Path "$d\omr") { ((Select-String -Path "$d\omr\*.lys" -Pattern '^form \{ (.*) \}' | ForEach-Object { $_.Matches[0].Groups[1].Value }) -join ' ' -split '[\s\[\]]+' | Where-Object { $_ -match '^~?[A-Za-z]' -and $_ -cnotin 'segno', 'coda', 'to', 'ds', 'dc', 'al', 'fine' }) -join ' ' } else { '(no reading: ' + (Get-Content "$d\omr.log" -Tail 1) + ')' }
    "{0,-18} want: {1}`n{2,-18} got:  {3}" -f $n, $want, '', $got
    # the form's signs in order (repeats, endings, jumps; S22), the names left out: what roadsong's lost segno
    # would have shown. The source's [1-2. ~Bridge] and the reading's [1-2. ~A] alike as "[1-2."
    $srcForm = if ($src -match '(?ms)^form\s*\{(.*?)\}') { $Matches[1] } else { '' }
    $omrForm = if (Test-Path "$d\omr") { (Select-String -Path "$d\omr\*.lys" -Pattern '^form \{ (.*) \}' | ForEach-Object { $_.Matches[0].Groups[1].Value }) -join ' ' } else { '' }
    $wantSigns = Signs $srcForm; $gotSigns = Signs $omrForm
    if ($wantSigns -or $gotSigns) { "{0,-18} signs {1}: want {2} / got {3}" -f '', $(if ($wantSigns -eq $gotSigns) { 'ok  ' } else { 'DIFF' }), $wantSigns, $gotSigns }
    # the words of expression (@text), in order, against the reading's
    $wantTexts = (Select-String -Path $pieces[$n] -Pattern '@text\("([^"]*)"\)' -AllMatches | ForEach-Object { $_.Matches | ForEach-Object { $_.Groups[1].Value } }) -join ' | '
    $gotTexts = if (Test-Path "$d\omr") { (Select-String -Path "$d\omr\*.lys" -Pattern '@text\("([^"]*)"\)' -AllMatches | ForEach-Object { $_.Matches | ForEach-Object { $_.Groups[1].Value } }) -join ' | ' } else { '' }
    if ($wantTexts -or $gotTexts) { "{0,-18} texts {1}: want {2} / got {3}" -f '', $(if ($wantTexts -eq $gotTexts) { 'ok  ' } else { 'DIFF' }), $wantTexts, $gotTexts }
}
