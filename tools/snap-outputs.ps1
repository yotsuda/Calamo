param(
    [Parameter(Mandatory)][string]$Name,   # the snapshot's name: out\snap-<name>
    [switch]$Compare,                      # compare the outputs now with the snapshot instead of taking it
    [datetime]$Since                       # (with -Compare) only the files written since then: the others are counted apart
)
# A refactoring's gate: every omr.lys and .omr.json the checks, seed 11's octave set and the held-out set write
# under out\, copied before (out\snap-<name>), then compared to the byte after (tools\same-outputs.ps1 runs it all).
$root = Resolve-Path "$PSScriptRoot\..\out"
$snap = Join-Path $root "snap-$Name"
$dirs = 'gen', 'gen-s3', 'gen-scan', 'gen-lied', 'gen-lied-all', 'gen-mmrests', 'gen-mmrests-scan', 'gen-tup', 'gen-tup-scan',
    'gen-ott', 'gen-ott-scan', 'gen-grace', 'gen-grace-scan', 'gen-rep', 'gen-rep-scan', 'gen-orn', 'gen-orn-scan', 'gen-span',
    'gen-span-scan', 'gen-trill', 'gen-trill-scan', 'gen-arp', 'gen-arp-scan', 'gen-gliss', 'gen-gliss-scan', 'gen-scan-fill',
    'gen-band', 'real', 'gen-ott-s11', 'audiveris'
# (the reading's own files only: a generated piece's .lys stands beside its omr folder)
$files = foreach ($d in $dirs) { Get-ChildItem (Join-Path $root $d) -Recurse -File -Include '*.lys', '*.omr.json' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\omr[^\\]*\\|omr\.lys$|\.omr\.json$' } }
if (-not $Compare) {
    if (Test-Path $snap) { Remove-Item $snap -Recurse -Force }
    foreach ($f in $files) {
        $dst = Join-Path $snap $f.FullName.Substring($root.Path.Length + 1)
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        Copy-Item $f.FullName $dst
    }
    "snapshot $Name : $($files.Count) files"
    return
}
$diff = @(); $n = 0; $stale = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($root.Path.Length + 1)
    $old = Join-Path $snap $rel
    if (-not (Test-Path $old)) { continue }
    if ($Since -and $f.LastWriteTime -lt $Since) { $stale++; continue }
    $n++
    if ((Get-FileHash $old).Hash -ne (Get-FileHash $f.FullName).Hash) { $diff += $rel }
}
$missing = (Get-ChildItem $snap -Recurse -File).Count - $n - $stale
"compared $n files with snap-$Name ($stale not rewritten since, $missing gone): $($diff.Count) differ"
$diff | Select-Object -First 40
