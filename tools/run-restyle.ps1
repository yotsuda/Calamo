param(
    [Parameter(Mandatory)][string]$From,  # pages MuseScore drew (run-ms.ps1's output: <font>\<set>-<suite>\<piece>\pNN.svg)
    [Parameter(Mandatory)][string]$Out,
    [int]$Seed = 1,
    [string]$Space = '13,22',           # pixels a staff space, drawn per page (a band score's scan is 13.5; -Degrade wants ~40)
    [switch]$Degrade                    # then make the pages look printed and scanned, as run-ms.ps1 -Degrade
)
# MuseScore's pages drawn again with every glyph's shape varied (B4: ms_restyle.py), then boxed (ms_boxes.py).
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$py = "$root\tools\train-model\.venv\Scripts\python.exe"
$sw = [Diagnostics.Stopwatch]::StartNew()
if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
& $py "$root\tools\train-model\ms_restyle.py" $From $Out --seed $Seed --space $Space | Out-Host
if ($Degrade) {
    $deg = "$root\src\OmrDegrade\bin\Release\net10.0-windows\OmrDegrade.exe"
    $rng = [Random]::new(1234 + $Seed)
    $args2 = foreach ($png in Get-ChildItem $Out -Recurse -Filter 'p??.png') {
        $a = @('--scale', ('{0:F2}' -f (0.4 + 0.25 * $rng.NextDouble())), '--blur', ('{0:F2}' -f (0.6 + 1.2 * $rng.NextDouble())),
               '--ink', ('{0:F2}' -f (0.05 + 0.3 * $rng.NextDouble())), '--paper', ('{0:F2}' -f (0.05 + 0.25 * $rng.NextDouble())),
               '--noise', ('{0:F0}' -f (3 + 12 * $rng.NextDouble())), '--specks', '0.0003')
        if ($rng.NextDouble() -lt 0.33) { $a += @('--binarize-auto', ('{0:F2}' -f (-0.2 + 0.3 * $rng.NextDouble()))) }
        if ($rng.NextDouble() -lt 0.7) { $a += @('--fill', ('{0:F2}' -f (0.2 + 0.6 * $rng.NextDouble())), '--fill-edge', ('{0:F1}' -f (2.5 + 3 * $rng.NextDouble()))) }
        , (@('--in', $png.FullName, '--out', $png.FullName) + $a + @('--seed', $rng.Next()))
    }
    # one degrade per core at a time
    $args2 | ForEach-Object -ThrottleLimit ([Environment]::ProcessorCount) -Parallel { & $using:deg @_ | Out-Null }
}
& $py "$root\tools\train-model\ms_boxes.py" $Out | Out-Host
"done in {0:F1}s" -f $sw.Elapsed.TotalSeconds
