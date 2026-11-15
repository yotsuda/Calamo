param(
    [string]$From = "$PSScriptRoot\..\out\gen",       # a generated set already read once (its omr\boxes-p01.tsv)
    [string]$Out = "$PSScriptRoot\..\out\gen-retime",
    [string[]]$Suites = @('basic', 'chords', 'ledger', 'meters', 'piano', 'tuplets', 'voices2'),
    [string]$ScanOut = '',    # also the set printed and scanned (run-gen.ps1 -Degrade's settings), here
    [string]$DegradeArgs = '--scale 0.5 --blur 0.9 --ink 0.15 --paper 0.15 --noise 10 --specks 0.0003',
    [double]$Scale = 5.6,
    [string]$Lysc = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe'
)
# A time signature printed other than the measures hold (section 0's gate 1 for "the time chosen
# by what the measures fill"): each piece of a generated set with its first time signature's upper
# figure drawn as another (3 for 2, 2 for 3, 3 for 4, ...), the music, the .score and the truth as
# they were. lysc cuts a measure longer than the time down, so the figure comes from a shadow page:
# the same .lys under the other time, engraved, and its upper figures pasted over this page's
# (OmrDegrade --paste). Read it with run-gen.ps1 -Out <Out> -Reuse.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$omr = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\calamo.exe"
$deg = "$root\src\OmrDegrade\bin\Release\net10.0-windows\OmrDegrade.exe"
$swap = @{ '2' = '3'; '3' = '2'; '4' = '3'; '5' = '4'; '6' = '9'; '7' = '6'; '8' = '6'; '9' = '6' }
foreach ($d in $Out, "$Out-shadow") { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
$made = 0; $skipped = @()
foreach ($suite in $Suites) {
    foreach ($src in Get-ChildItem (Join-Path $From $suite) -Directory) {
        $name = $src.Name
        $lys = Get-Content (Join-Path $src.FullName "$name.lys")
        $line = $lys | Where-Object { $_ -match '^time (\d+)/(\d+)\s*$' } | Select-Object -First 1
        if (-not $line -or -not $swap.ContainsKey($Matches[1])) { $skipped += "${name}: $line"; continue }
        $num = $Matches[1]; $den = $Matches[2]; $to = $swap[$num]
        # (2/2 and 4/4 are printed as a cut C and a C: no figure to take)
        if ("$to/$den" -eq '2/2') { $to = '4' } elseif ("$to/$den" -eq '4/4') { $to = '6' }
        $dir = Join-Path $Out "$suite\$name"
        New-Item -ItemType Directory -Force $dir | Out-Null
        Get-ChildItem $src.FullName -File | Copy-Item -Destination $dir
        # the shadow: the same piece under the other time (its measures cut, its first system's signs as printed)
        $sh = Join-Path "$Out-shadow" "$suite\$name"    # (beside the set: run-gen reads every .lys under it)
        New-Item -ItemType Directory -Force $sh | Out-Null
        $done = $false
        $lys | ForEach-Object { if (-not $done -and $_ -eq $line) { $done = $true; "time $to/$den" } else { $_ } } |
            Set-Content -Encoding utf8 (Join-Path $sh "$name.lys")
        cmd /d /s /c "`"$Lysc`" png --scale $Scale `"$sh\$name.lys`" < NUL > `"$sh\lysc.log`" 2>&1"
        cmd /d /s /c "`"$Lysc`" svg `"$sh\$name.lys`" < NUL >> `"$sh\lysc.log`" 2>&1"
        $png = @(Get-ChildItem $sh -Filter "$name*.png" | Sort-Object Name)[0]
        $svg = @(Get-ChildItem $sh -Filter "$name*.svg" | Sort-Object Name)[0]
        Move-Item $png.FullName "$sh\p01.png"; Move-Item $svg.FullName "$sh\$name.svg"
        & $omr --config "$dir\$name.score" --pages $sh --from 1 --to 1 --out "$sh\omr" --svg "$sh\$name.svg" --export-boxes --threads 1 | Out-Null
        # the first column of figures on each page, top to bottom: upper, lower, upper, lower, ...
        function Figures($tsv) {
            $f = @(Import-Csv $tsv -Delimiter "`t" | Where-Object { $_.kind -eq 'glyph' -and $_.name -match '^\d$' } |
                ForEach-Object { [pscustomobject]@{ N = $_.name; L = [double]$_.l; T = [double]$_.t; R = [double]$_.r; B = [double]$_.b } })
            if ($f.Count -eq 0) { return @() }
            $x = ($f | Measure-Object L -Minimum).Minimum
            $w = ($f | ForEach-Object { $_.R - $_.L } | Measure-Object -Maximum).Maximum
            @($f | Where-Object { $_.L -lt $x + $w } | Sort-Object T)
        }
        $a = Figures (Join-Path $src.FullName 'omr\boxes-p01.tsv')
        $b = Figures "$sh\omr\boxes-p01.tsv"
        if ($a.Count -eq 0 -or $a.Count -ne $b.Count -or $a.Count % 2) { $skipped += "${name}: figures $($a.Count) / $($b.Count)"; Remove-Item -Recurse -Force $dir; continue }
        $inv = [Globalization.CultureInfo]::InvariantCulture
        $p = for ($i = 0; $i -lt $a.Count; $i += 2) {
            $s = $b[$i]; $d = $a[$i]
            '{0}|{1},{2},{3},{4}|{5},{6},{7},{8}' -f "$sh\p01.png", $s.L.ToString($inv), $s.T.ToString($inv), $s.R.ToString($inv), $s.B.ToString($inv), $d.L.ToString($inv), $d.T.ToString($inv), $d.R.ToString($inv), $d.B.ToString($inv)
        }
        & $deg --in "$dir\p01.png" --out "$dir\p01.png" --paste ($p -join ';') --blur 0 --ink 0 --paper 0 --noise 0 --specks 0 | Out-Null
        Add-Content -Encoding utf8 "$Out\retime.txt" "$suite\$name`t$num/$den -> $to/$den`t$($a.Count / 2) staves"
        $made++
    }
}
if ($ScanOut) {
    # (a page's seed its place in the set, as run-gen's -Degrade numbers them)
    if (Test-Path $ScanOut) { Remove-Item -Recurse -Force $ScanOut }
    Copy-Item -Recurse $Out $ScanOut
    $n = 0
    foreach ($png in Get-ChildItem $ScanOut -Recurse -Filter 'p??.png') {
        $n++
        & $deg --in $png.FullName --out $png.FullName @($DegradeArgs -split ' ') --seed $n | Out-Null
    }
}
"$made pieces retimed into $Out (read it with run-gen.ps1 -Out $Out -Reuse); skipped $($skipped.Count)"
$skipped | ForEach-Object { "  $_" }
