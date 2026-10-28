param(
    [switch]$Snapshot,   # take the snapshots (rules, det, eval) with the code as it is, before refactoring
    [switch]$NoEval      # leave the held-out set out (6 minutes)
)
# A refactoring's gate (no change in behaviour): build, run every set as the checks do (rules, then the detector),
# seed 11's octave set and the held-out set, and compare every omr.lys / .omr.json with the snapshots
# (tools\snap-outputs.ps1) to the byte. About 35 minutes. Run it detached: cmd /d /s /c "pwsh -File ... < NUL > log 2>&1"
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
Set-Location $root
$b = dotnet build src\Omr -c Release -nologo -v q 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { $b; throw 'build failed' }
$log = "$root\out\same-outputs"
New-Item -ItemType Directory -Force $log | Out-Null
$t = Get-Date
pwsh -NoProfile -File tools\check.ps1 *> "$log\check-rules.log"
"rules check: " + (Get-Content "$log\check-rules.log" -Tail 1)
if ($Snapshot) { & "$PSScriptRoot\snap-outputs.ps1" -Name rules } else { & "$PSScriptRoot\snap-outputs.ps1" -Name rules -Compare -Since $t }
$t = Get-Date
pwsh -NoProfile -File tools\check.ps1 -Detector *> "$log\check-det.log"
"detector check: " + (Get-Content "$log\check-det.log" -Tail 1)
& "$PSScriptRoot\run-gen.ps1" -Out "$root\out\gen-ott-s11" -Reuse -OmrArgs @('--detector') | Out-Null
if ($Snapshot) { & "$PSScriptRoot\snap-outputs.ps1" -Name det } else { & "$PSScriptRoot\snap-outputs.ps1" -Name det -Compare -Since $t }
if (-not $NoEval) {
    $t = Get-Date
    pwsh -NoProfile -File tools\run-audiveris.ps1 -Eval -Reuse *> "$log\eval.log"
    if ($Snapshot) { & "$PSScriptRoot\snap-outputs.ps1" -Name eval } else { & "$PSScriptRoot\snap-outputs.ps1" -Name eval -Compare -Since $t }
    Get-Content "$log\eval.log" | Select-String '^total' | ForEach-Object Line
}
