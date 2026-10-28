param(
    [string]$Out = "$PSScriptRoot\..\out\gen-band",   # a run-gen.ps1 folder of band pieces (their rehearsal marks in boxes)
    [switch]$NoRead      # compare the readings there already (omr/omr.lys written with --header)
)
# How well the reader reads the rehearsal marks in boxes (the k-NN's "l:" family: a letter or a figure,
# else the OCR): what each piece's .lys prints in its boxes (a section's declared label, else its name)
# against what the reading's omr.lys prints, in order. Read with --header --detector unless -NoRead.
$ErrorActionPreference = 'Stop'
if (-not $NoRead) { & "$PSScriptRoot\run-gen.ps1" -Out $Out -Reuse -OmrArgs @('--header', '--detector') | Out-Null }

function Printed($path) {
    $marks = @()
    foreach ($line in Get-Content $path -Encoding utf8) {
        if ($line -match '^section\s+(\S+)(?:\s+"([^"]*)")?\s*\{') {
            $name = $Matches[1]; $label = $Matches[2]
            if ($null -ne $label) { $marks += $label } else { $marks += ($name -replace '_\d+$', '') }
        }
    }
    $marks
}
$right = 0; $all = 0; $wrong = @()
foreach ($lys in Get-ChildItem $Out -Recurse -Filter *.lys | Where-Object { $_.Directory.Name -ne 'omr' } | Sort-Object FullName) {
    $read = Join-Path $lys.DirectoryName 'omr\omr.lys'
    if (-not (Test-Path $read)) { continue }
    $want = @(Printed $lys.FullName)
    # (the reader names the music before the first mark Intro, a piece without marks Piece: no boxes)
    $got = @(Printed $read | Where-Object { $_ -ne 'Intro' -and $_ -ne 'Piece' })
    $all += $want.Count
    for ($i = 0; $i -lt $want.Count; $i++) { if ($i -lt $got.Count -and $got[$i] -ceq $want[$i]) { $right++ } }
    if (($want -join ' ') -cne ($got -join ' ')) { $wrong += "{0,-10} want: {1,-20} got: {2}" -f $lys.BaseName, ($want -join ' '), ($got -join ' ') }
}
"rehearsal marks right: $right / $all"
$wrong
