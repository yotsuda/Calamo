param([string]$Model, [string]$Tag)
# Read the evaluation and development sets with one detector (a candidate from a retraining), keeping the
# tables under out\pseudo2\<tag>-eval-sets.tsv and <tag>-dev-sets.tsv and the logs beside them, to compare
# with docs\eval-sets.tsv and docs\dev-sets.tsv (the detector in data\): ROADMAP §7.4
#   pwsh tools\train-model\evalmodel.ps1 -Model tools\train-model\detector42.onnx -Tag d42      (about 20 minutes)
$root = Resolve-Path "$PSScriptRoot\..\.."
Set-Location $root
$det = (Resolve-Path $Model).Path
New-Item -ItemType Directory -Force out\pseudo2 | Out-Null
foreach ($k in 'Eval', 'Dev') {
    $a = @{ Reuse = $true; OmrArgs = @('--detector', $det) }
    $a[$k] = $true
    & .\tools\run-audiveris.ps1 @a *> "out\pseudo2\$Tag-$k.log"
    Copy-Item ("out\audiveris\{0}-sets.tsv" -f $k.ToLower()) ("out\pseudo2\$Tag-{0}-sets.tsv" -f $k.ToLower())
}
