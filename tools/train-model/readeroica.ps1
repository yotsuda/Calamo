param([string]$Model = '', [string]$Tag = 'now')
# Read the Scoring Notes 2024 Eroica trumpet part (Sibelius; the PDF and the phone photos of it: the cue
# bars the detector learns from OmrGen --cues) with one detector, into out\eroica\<tag>, and sum up: the
# part-bars whose length is right, the todos, the cue bars and the grace groups written. The owner's copy
# of the files is outside every repository (C:\MyProj\omr-compare, the comparison of 2026-10-26)
#   pwsh tools\train-model\readeroica.ps1 -Model tools\train-model\detector42.onnx -Tag d42
# (omr read takes the detector beside the exe: with -Model, a copy of the build with the model in its place)
$root = Resolve-Path "$PSScriptRoot\..\.."
$exe = "$root\src\Omr\bin\Release\net10.0-windows10.0.19041.0\omr.exe"
if ($Model) {
    $bin = "$root\out\eroica\omr-$Tag"
    if (Test-Path $bin) { Remove-Item -Recurse -Force $bin }
    Copy-Item -Recurse (Split-Path $exe) $bin
    Copy-Item -Force (Resolve-Path $Model).Path "$bin\detector.onnx"
    $exe = "$bin\omr.exe"
}
$src = 'C:\MyProj\omr-compare\scoringnotes-2024\files'
$out = "$root\out\eroica\$Tag"
foreach ($k in @(@('eroica-pdf', 'SymphonyNo3-1stMvt- BbTpt1.pdf'), @('eroica-scan', 'SymphonyNo3-1stMvt-SCANNED.pdf'))) {
    & $exe read (Join-Path $src $k[1]) --out "$out\$($k[0])" *> "$out-$($k[0]).log"
    $lys = Get-ChildItem "$out\$($k[0])" -Filter *.lys | Select-Object -First 1
    $cues = (Select-String -Path $lys.FullName -Pattern 'cue \{' -AllMatches).Matches.Count
    $graces = (Select-String -Path $lys.FullName -Pattern '(appoggiatura|acciaccatura|grace) \{' -AllMatches).Matches.Count
    $sum = (Select-String -Path "$out-$($k[0]).log" -Pattern '^(bar length ok|todo: \d)').Line -join '; '
    "{0} {1}: {2}; cue bars {3}, grace groups {4}" -f $Tag, $k[0], $sum, $cues, $graces
}
