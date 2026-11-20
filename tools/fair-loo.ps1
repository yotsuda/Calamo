# Leave-one-out over the fair set: does the sign of (B - A) found hold when any one set is left out?
# Usage: pwsh tools/fair-loo.ps1 <A fair-sets.tsv> <B fair-sets.tsv>   (e.g. tables from run-audiveris.ps1 -Fair with two detectors)
param([Parameter(Mandatory)][string]$FileA, [Parameter(Mandatory)][string]$FileB)
Set-Location C:\MyProj\Calamo
$a = @{}; $b = @{}; $t = @{}
Import-Csv $FileA -Delimiter "`t" | ForEach-Object { $a[$_.set] = [int]$_.found; $t[$_.set] = [int]$_.truth }
Import-Csv $FileB -Delimiter "`t" | ForEach-Object { $b[$_.set] = [int]$_.found }
$sets = $a.Keys | Where-Object { $b.ContainsKey($_) } | Sort-Object
$sa = ($sets | ForEach-Object { $a[$_] } | Measure-Object -Sum).Sum
$sb = ($sets | ForEach-Object { $b[$_] } | Measure-Object -Sum).Sum
$st = ($sets | ForEach-Object { $t[$_] } | Measure-Object -Sum).Sum
'{0} sets, truth {1}: A {2} ({3:P1})  B {4} ({5:P1})  B-A {6}' -f $sets.Count, $st, $sa, ($sa / $st), $sb, ($sb / $st), ($sb - $sa)
''
'set                                   truth     A     B   B-A   B-A without it'
foreach ($s in $sets) {
    $d = $b[$s] - $a[$s]
    '{0,-36} {1,6} {2,5} {3,5} {4,5} {5,8}' -f $s, $t[$s], $a[$s], $b[$s], $d, (($sb - $sa) - $d)
}
$loo = $sets | ForEach-Object { ($sb - $sa) - ($b[$_] - $a[$_]) }
$signs = $loo | ForEach-Object { [Math]::Sign($_) } | Sort-Object -Unique
''
'leave-one-out B-A: min {0}  max {1}  signs {2}' -f ($loo | Measure-Object -Minimum).Minimum, ($loo | Measure-Object -Maximum).Maximum, ($signs -join ',')
# Per-set change in points (each set weighs the same): mean and median, and their leave-one-out range.
$pp = $sets | ForEach-Object { 100.0 * ($b[$_] - $a[$_]) / $t[$_] }
function Get-FairMedian([double[]]$v) { $s = $v | Sort-Object; $n = $s.Count; if ($n % 2) { $s[($n - 1) / 2] } else { ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }
$looMean = 0..($pp.Count - 1) | ForEach-Object { $i = $_; ($pp | Select-Object -Index (0..($pp.Count - 1) | Where-Object { $_ -ne $i }) | Measure-Object -Average).Average }
$looMed = 0..($pp.Count - 1) | ForEach-Object { $i = $_; Get-FairMedian ($pp | Select-Object -Index (0..($pp.Count - 1) | Where-Object { $_ -ne $i })) }
'per-set points: mean {0:N2} (leave-one-out {1:N2} .. {2:N2}), median {3:N2} (leave-one-out {4:N2} .. {5:N2}), sets B>A {6}, B<A {7}' -f ($pp | Measure-Object -Average).Average, ($looMean | Measure-Object -Minimum).Minimum, ($looMean | Measure-Object -Maximum).Maximum, (Get-FairMedian $pp), ($looMed | Measure-Object -Minimum).Minimum, ($looMed | Measure-Object -Maximum).Maximum, @($pp | Where-Object { $_ -gt 0 }).Count, @($pp | Where-Object { $_ -lt 0 }).Count

