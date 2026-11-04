param(
    [switch]$List    # only show what would be stopped
)
# Stops a running check or benchmark of this repository, in the order that leaves nothing behind: the scripts first,
# so that nothing new is started, then the readers, then the lysc they started. A taskkill /T of the chain alone leaves
# readers running that start lysc with their console gone (0xc0000142, a dialog that holds lysc.exe against Lily#'s
# build; 2026-11-04). Only processes of this repository: a lysc of Lily#'s own sessions, its command line naming no
# file here, is left alone, and so is a script of theirs: the scripts are matched by this repository's own script names, as
# a command line has them relative to its folder ("tools\any.ps1" stopped Lily#'s tools\Session-Check.ps1 -End; 2026-11-04).
$root = (Resolve-Path "$PSScriptRoot\..").Path
$own = (Get-ChildItem $PSScriptRoot -Filter *.ps1 | ForEach-Object { [regex]::Escape($_.BaseName) }) -join '|'
function Mine([string[]]$names, [string]$pattern) {
    $names | ForEach-Object { Get-CimInstance Win32_Process -Filter "Name='$_'" } |
        Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -match $pattern -and $_.CommandLine -notmatch 'stop-run' }
}
$steps = @(
    @{ What = 'scripts'; Names = @('cmd.exe', 'pwsh.exe'); Pattern = "tools\\($own)\.ps1" }
    @{ What = 'readers and tools'; Names = @('omr.exe', 'OmrGen.exe', 'OmrDegrade.exe'); Pattern = [regex]::Escape($root) }
    @{ What = 'lysc'; Names = @('lysc.exe'); Pattern = [regex]::Escape($root) }
)
foreach ($step in $steps) {
    # (again until none is left: a reader still starting a lysc as the one before is stopped)
    for ($i = 0; $i -lt 10; $i++) {
        $found = @(Mine $step.Names $step.Pattern)
        if ($found.Count -eq 0) { break }
        foreach ($p in $found) {
            "$($step.What): $($p.ProcessId) $($p.CommandLine.Substring(0, [Math]::Min(110, $p.CommandLine.Length)))"
            if (-not $List) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
        }
        if ($List) { break }
        Start-Sleep -Milliseconds 500
    }
}
