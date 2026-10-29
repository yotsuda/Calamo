param(
    [string]$Out = "$PSScriptRoot\..\..\out\pseudo",   # one folder per set: dev-<piece>-<kind>
    [string[]]$Kinds = @('scan'),                       # the development set's prints to export (ms, scan)
    [string]$Model = ''                                 # the detector to read with (default: data/detector.onnx)
)
# The development set's pages as the reader reads them, for pseudo.py (S50): each set read as
# run-audiveris -Dev reads it (its pages scaled as there, --align --by-order --detector) with
# --export-read. Run run-audiveris -Dev first: its out/audiveris/dev-*/pages are the pages read.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\..\.."
$omr = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
New-Item -ItemType Directory -Force $Out | Out-Null
$empty = "$Out\empty.txt"
[IO.File]::WriteAllText($empty, '')   # (stdin: nothing)
$jobs = foreach ($d in Get-ChildItem "$root\data\dev\lieder" -Directory | Sort-Object Name) {
    foreach ($kind in $Kinds) {
        $name = "dev-$($d.Name)-$kind"
        $pages = "$root\out\audiveris\$name\pages"
        if (-not (Test-Path "$pages\p01.png")) { continue }
        $dir = "$Out\$name"
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        New-Item -ItemType Directory -Force $dir | Out-Null
        $to = @(Get-ChildItem "$pages\p*.png").Count
        $a = @('--config', "$($d.FullName)\$kind\$($d.Name).score", '--truth', "$($d.FullName)\ms\truth.musicxml", '--poly',
               '--pages', $pages, '--from', '1', '--to', "$to", '--align', '--by-order', '--out', "$dir\omr", '--export-read', $dir)
        $a += if ($Model) { @('--detector', $Model) } else { @('--detector') }
        Start-Process $omr -ArgumentList ($a | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -NoNewWindow -PassThru `
            -RedirectStandardOutput "$dir\run.txt" -RedirectStandardError "$dir\err.txt" -RedirectStandardInput $empty
    }
}
$jobs | Wait-Process
foreach ($j in $jobs) { if ($j.ExitCode -ne 0) { Write-Warning "exit $($j.ExitCode)" } }
Get-ChildItem $Out -Directory -Filter 'dev-*' | ForEach-Object { '{0,-24} {1}' -f $_.Name, ((Get-Content "$($_.FullName)\run.txt" | Select-String 'exact events|notes\+rests' | Select-Object -First 1) -replace '\s+', ' ') }
