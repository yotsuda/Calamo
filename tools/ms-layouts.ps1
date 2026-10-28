param(
    [string]$Eval = "$PSScriptRoot\..\out\ms-band5-eval",   # the MuseScore band evaluation set, or its scanned twin (-scan)
    [string]$Gen = "$PSScriptRoot\..\out\gen-band",           # the pieces' .score files
    [string[]]$Fonts,                                         # some of the eight (all by default)
    [string[]]$Pieces,                                        # band002 ... (all by default)
    [switch]$Trace,                                           # keep omr read's stderr (Layout.TraceJoins) beside each page as pNN.joins.txt
    [string]$Omr                                              # another omr.exe (a build of an older commit, to compare)
)
# omr read --parts-only on every page of the MuseScore band evaluation set, against the piece's .score:
# the staves of a system by kind (5 a five-line staff, T a TAB, L a one-line staff) and what joins each
# two beyond the opening line (a brace between the keyboard's staves, and over a guitar's or bass's
# staff and its TAB -- MuseScore draws one, Lily# does not; the .score's tab line hides it from the
# .lys). Prints every page that differs and the counts; writes them all to <Eval>\layouts.txt.
$ErrorActionPreference = 'Stop'
$omr = if ($Omr) { $Omr } else { "$PSScriptRoot\..\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe" }
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'ms-layouts'
New-Item -ItemType Directory -Force $tmp | Out-Null
$fontDirs = Get-ChildItem $Eval -Directory | Where-Object { -not $Fonts -or $Fonts -contains $_.Name }
$rows = @()
$pages = 0; $kindsWrong = 0; $joinsWrong = 0
foreach ($f in $fontDirs) {
    foreach ($pd in Get-ChildItem "$($f.FullName)\gen-band-band" -Directory | Where-Object { -not $Pieces -or $Pieces -contains $_.Name }) {
        $piece = $pd.Name
        $score = Get-Content "$Gen\band\$piece\$piece.score"
        $kinds = @(); $ids = @()
        foreach ($l in $score) {
            if ($l -match '^part (\S+)') { $kinds += '5'; $ids += $Matches[1] }
            elseif ($l -match '^tab\b') { $kinds += 'T'; $ids += $null }
            elseif ($l -match '^line\b') { $kinds += 'L'; $ids += $null }
        }
        $joins = @()
        for ($i = 0; $i + 1 -lt $kinds.Count; $i++) {
            $joins += if ($kinds[$i] -eq '5' -and $kinds[$i + 1] -eq 'T') { 'brace' } elseif ($ids[$i] -eq 'kbr' -and $ids[$i + 1] -eq 'kbl') { 'brace' } else { '' }
        }
        $wantLayout = $kinds -join ','
        $wantJoins = $joins -join ','
        foreach ($png in Get-ChildItem $pd.FullName -Filter 'p??.png' | Sort-Object Name) {
            $pages++
            $err = if ($Trace) { "$($pd.FullName)\$($png.BaseName).joins.txt" } else { "$tmp\err.txt" }
            $out = cmd /d /s /c "`"$omr`" read `"$($png.FullName)`" --out `"$tmp\out`" --parts-only < NUL 2> `"$err`""
            $line = ($out | Where-Object { $_ -match '^\d+( |$)' } | Select-Object -Last 1)
            if (-not $line) { $line = '(none: ' + ((Get-Content $err -Tail 1) -join '') + ')' }
            $gotLayout = ''; $gotJoins = ''
            if ($line -match '^(\d+)(?: ([5TL,]+))?(?: joins=(\S*))?$') {
                $n = [int]$Matches[1]
                $gotLayout = if ($Matches[2]) { $Matches[2] } else { (@('5') * $n) -join ',' }
                $gotJoins = if ($Matches[3]) { $Matches[3] } else { (@('') * ([Math]::Max(0, $gotLayout.Split(',').Count - 1))) -join ',' }
            }
            $okKinds = $gotLayout -eq $wantLayout
            $okJoins = $gotJoins -eq $wantJoins
            if (-not $okKinds) { $kindsWrong++ } elseif (-not $okJoins) { $joinsWrong++ }
            $mark = if (-not $okKinds) { 'KINDS' } elseif (-not $okJoins) { 'JOINS' } else { 'ok' }
            $row = "{0,-5} {1,-14} {2} {3}: {4}" -f $mark, $f.Name, $piece, $png.BaseName, $line
            if ($mark -ne 'ok') { $row += "  (want $wantLayout joins=$wantJoins)"; $row }
            $rows += $row
        }
    }
}
$rows | Set-Content "$Eval\layouts.txt"
"{0} pages: kinds wrong {1}, joins wrong {2} -> {3}" -f $pages, $kindsWrong, $joinsWrong, "$Eval\layouts.txt"
