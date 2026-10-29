param(
    [string]$Out = "$PSScriptRoot\..\out\extension"   # the staging folder and the .vsix
)
# The OMR as a VS Code extension (docs/repro/lilysharp-handoff-2026-10-12.md §1.3): omr.exe
# published self-contained for win-x64 into extension/bin, its `omr --version --json` beside it
# (bin/omr.json, which extension.js hands to Lily#), and `vsce package --target win32-x64`.
# Packages only. Publishing is the tag-driven workflow's (.github/workflows/release.yml: this script,
# then the GitHub Release and `vsce publish` with the VSCE_PAT secret); a push of v0.3.0 releases 0.3.0.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$ext = "$root\extension"
if (-not (Test-Path "$ext\LICENSE")) { throw "extension/LICENSE is missing" }

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
$stage = "$Out\stage"
Copy-Item $ext $stage -Recurse
$bin = "$stage\bin"
cmd /d /s /c "dotnet publish `"$root\src\Omr`" -c Release -r win-x64 --self-contained -o `"$bin`" -v q -nologo < NUL"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Get-ChildItem $bin -Filter *.pdb | Remove-Item

# the version in package.json is the reader's own, and the contract it speaks goes with it
$info = & "$bin\omr.exe" --version --json | ConvertFrom-Json
$pkg = Get-Content "$stage\package.json" -Raw | ConvertFrom-Json
if ($pkg.version -ne $info.version) { throw "extension/package.json is $($pkg.version), the reader $($info.version): make them one" }
[IO.File]::WriteAllText("$bin\omr.json", (& "$bin\omr.exe" --version --json))

# (the owner's seeds, their readings and tools/seeds.ps1 stay on the owner's machine: never in a release.
# docs/repro/lilysharp-requests-2026-10-29-seed-from-fixed-bars.md)
$seedFiles = Get-ChildItem $stage -Recurse -File | Where-Object { $_.FullName.Substring($stage.Length) -match 'seed' }
if ($seedFiles) { throw "seed files in the package: $($seedFiles.FullName -join ', ')" }
$size = (Get-ChildItem $bin -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"reader {0} (protocol {1}), bin {2:F1} MB" -f $info.version, $info.protocol, $size
Push-Location $stage
try {
    cmd /d /s /c "npx --yes @vscode/vsce package --target win32-x64 --out `"$Out`" < NUL"
    if ($LASTEXITCODE -ne 0) { throw "vsce package failed" }
} finally { Pop-Location }
Get-ChildItem $Out -Filter *.vsix | ForEach-Object { "{0} {1:F1} MB" -f $_.Name, ($_.Length / 1MB) }
