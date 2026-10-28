param(
    [string[]]$Songs = @('Fantasy', 'Time After Time'),
    [string[]]$OmrArgs = @('--detector'),   # more arguments for omr ('--overlay', '--sigs', ...)
    [switch]$NoDetector,                    # the rules alone
    [switch]$Overlay                        # omr/overlay/pNNsK[LR].png: what was read, over the page
)
# The band scores (M2b, data/scans): each song's scanned pages read with its .score, and the layout
# read (systems, their staves by kind, their measures) checked against the one counted by eye
# (data/scans/<song>/layout.txt; B2). The pages are the JPEGs as PNG under out/band/<song>/pages.
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path "$PSScriptRoot\.."
$omr = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
$OmrArgs = @($OmrArgs | ForEach-Object { $_ -split ',' })   # (pwsh -File passes a list as one string)
if ($NoDetector) { $OmrArgs = @($OmrArgs | Where-Object { $_ -ne '--detector' }) }
if ($Overlay) { $OmrArgs += '--overlay' }
$totals = [ordered]@{ systems = 0; systemsOk = 0; staves = 0; stavesOk = 0; measures = 0; measuresOk = 0 }
foreach ($song in $Songs) {
    $src = "$root\data\scans\$song"
    $name = ($song -replace '\s', '').ToLower()
    $out = "$root\out\band\$name"
    $pages = "$out\pages"
    New-Item -ItemType Directory -Force $pages | Out-Null
    $jpgs = Get-ChildItem "$src\*.jpg" | Sort-Object Name
    for ($i = 0; $i -lt $jpgs.Count; $i++) {
        $png = "$pages\p{0:D2}.png" -f ($i + 1)
        if (Test-Path $png) { continue }
        $b = [Drawing.Bitmap]::new($jpgs[$i].FullName); $b.Save($png, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    }
    $a = @('--config', "$src\$name.score", '--pages', $pages, '--from', '1', '--to', "$($jpgs.Count)", '--out', "$out\omr", '--poly') + $OmrArgs
    $text = & $omr @a 2>&1 | Out-String
    $text | Set-Content "$out\omr\log.txt"
    $got = @{}
    foreach ($m in [regex]::Matches($text, '(?m)^layout (\d+): (.*)$')) { $got[[int]$m.Groups[1].Value] = $m.Groups[2].Value.Trim() }
    $want = @{}
    foreach ($l in Get-Content "$src\layout.txt") { if ($l -match '^(\d+): (.*)$') { $want[[int]$Matches[1]] = $Matches[2].Trim() } }
    $diffs = @()
    foreach ($p in ($want.Keys | Sort-Object)) {
        $ws = @($want[$p] -split '\s*\|\s*'); $gs = @(if ($got[$p]) { $got[$p] -split '\s*\|\s*' })
        for ($k = 0; $k -lt $ws.Count; $k++) {
            $wk, $wm = $ws[$k] -split ' '; $wkinds = $wk -split ','
            $totals.systems++; $totals.staves += $wkinds.Count; $totals.measures += [int]$wm
            if ($k -ge $gs.Count) { $diffs += "p$p s$($k + 1): not found (want $($ws[$k]))"; continue }
            $gk, $gm = $gs[$k] -split ' '
            $totals.systemsOk++
            if ($gk -eq $wk) { $totals.stavesOk += $wkinds.Count } else { $diffs += "p$p s$($k + 1): staves $gk, want $wk" }
            if ([int]$gm -eq [int]$wm) { $totals.measuresOk += [int]$wm } else { $diffs += "p$p s$($k + 1): measures $gm, want $wm" }
        }
        if ($gs.Count -gt $ws.Count) { $diffs += "p$p`: $($gs.Count - $ws.Count) systems too many" }
    }
    $len = ([regex]::Match($text, 'bar length ok: (\S+)')).Groups[1].Value
    "{0,-16} {1} systems on {2} pages, bar length ok {3}" -f $song, ($want.Values | ForEach-Object { ($_ -split '\|').Count } | Measure-Object -Sum).Sum, $want.Count, $len
    $diffs | ForEach-Object { "  $_" }
}
"layout: systems {0}/{1}, staves {2}/{3}, measures {4}/{5} (in systems whose count is right)" -f $totals.systemsOk, $totals.systems, $totals.stavesOk, $totals.staves, $totals.measuresOk, $totals.measures
