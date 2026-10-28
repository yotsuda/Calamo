param(
    [string]$Code,          # the VS Code CLI (code.cmd); found on the PATH or in the user install when not given
    [switch]$NoPack,        # install the .vsix already in out/extension instead of packing it again
    [string]$Out = "$PSScriptRoot\..\out\extension"
)
# Packs the OMR extension (pack-extension.ps1, with the draft terms: this install is local) and
# installs it into this PC's VS Code, over any earlier one. Never publishes (that is the owner's
# `vsce publish`). Lily# finds the reader through the extension unless LILYSHARP_OMR_PATH is set:
# that variable wins, so it is reported here.
$ErrorActionPreference = 'Stop'

if (-not $NoPack) {
    & "$PSScriptRoot\pack-extension.ps1" -Out $Out
}
$vsix = Get-ChildItem $Out -Filter *.vsix -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $vsix) { throw "no .vsix in ${Out}: run without -NoPack" }

if (-not $Code) {
    $Code = (Get-Command code.cmd -ErrorAction SilentlyContinue).Source
    if (-not $Code) { $Code = "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd" }
}
if (-not (Test-Path $Code)) { throw "no VS Code CLI at ${Code}: pass -Code <path to code.cmd>" }

# (a reader still running from the installed extension holds its omr.exe: the old folder cannot go)
$running = Get-Process omr -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*yotsuda.lilysharp-omr*' }
if ($running) { throw "omr.exe is running from the installed extension (pid $($running.Id -join ', ')): let it finish first" }

"installing {0} into {1}" -f $vsix.Name, $Code
cmd /d /s /c "`"$Code`" --install-extension `"$($vsix.FullName)`" --force < NUL"
if ($LASTEXITCODE -ne 0) { throw "code --install-extension failed" }
cmd /d /s /c "`"$Code`" --list-extensions --show-versions < NUL" | Select-String '^yotsuda\.' | ForEach-Object { "  $_" }

foreach ($scope in 'Process', 'User', 'Machine') {
    $v = [Environment]::GetEnvironmentVariable('LILYSHARP_OMR_PATH', $scope)
    if ($v) { Write-Warning "LILYSHARP_OMR_PATH ($scope) = ${v}: Lily# uses it instead of the installed extension" }
}
"done: reload the VS Code windows (Developer: Reload Window) to use the new reader"
