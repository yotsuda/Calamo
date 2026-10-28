param(
    [string]$Out = "$PSScriptRoot\..\out\gen-marks",   # a run-gen.ps1 folder of pieces made with OmrGen --marks
    [switch]$Detail                                    # list every miss and extra
)
# How well the reader reads dynamics, hairpins and the pedal (S14), articulations (S15), octave
# lines (S18), grace notes (S21), the form (S22), ornaments (S23: trill, mordent, prall, turn,
# reverseTurn, by name like the articulations), the rarer scripts (portato, stopped, pralltriller), texts (S24), text spanners (S25), trill lines, arpeggios and glissandi: each
# piece's .lys (the truth) and the reader's omr/omr.lys, part by part and bar by bar. Counted per bar:
# the dynamics (the word, placed above or below alike), the hairpins' starts (cresc, decresc), the
# pedal's presses and releases, and the articulations by name (above or below alike). A hairpin's end
# is not counted: the truth ends many at the next dynamic, the reader at its last note (both draw the same).
$ErrorActionPreference = 'Stop'
$kinds = 'dynamic', 'hairpin', 'pedal', 'artic', 'ottava', 'grace', 'gracekind', 'repeat', 'volta', 'nav', 'ornament', 'scripts2', 'text', 'span', 'spanend', 'trill', 'trillend', 'arpeggio', 'glissando'
$tot = @{}; foreach ($k in $kinds) { $tot[$k] = @{ truth = 0; omr = 0; found = 0 } }
$lines = @()

# the marks of each part's bars: part -> list of bars -> list of "dynamic:mf", "hairpin:cresc", "pedal:down"
function Marks([string]$path) {
    $parts = @{}; $part = $null; $depth = 0
    foreach ($line in Get-Content $path) {
        $t = $line.Trim()
        if ($t -match '^section ') { $depth = 1; continue }
        if ($depth -eq 1 -and $t -match '^(\w+) \{$') { $part = $Matches[1]; if (-not $parts.ContainsKey($part)) { $parts[$part] = [Collections.Generic.List[object]]::new() }; continue }
        if ($t -eq '}') { if ($part) { $part = $null } else { $depth = 0 }; continue }
        if (-not $part) { continue }
        # one bar a line (the reader's: its line comment cut off)
        $music = ($t -replace '//.*$', '')
        $m = [Collections.Generic.List[string]]::new()
        # grace notes (S21): the group as written, "grace:acciaccatura{d''8}" (its kind, pitches and values
        # all alike), and the looser "gracekind:acciaccatura/1" (the kind and the count)
        foreach ($x in [regex]::Matches($music, '\b(acciaccatura|appoggiatura|grace)\s*\{([^}]*)\}')) {
            $notes = @($x.Groups[2].Value.Trim() -split '\s+' | Where-Object { $_ })
            $m.Add("grace:$($x.Groups[1].Value){$($notes -join ' ')}")
            $m.Add("gracekind:$($x.Groups[1].Value)/$($notes.Count)")
        }
        foreach ($x in [regex]::Matches($music, '@(!?)([A-Za-z0-9]+)(?:\.(?:up|down))?(?:\((?:"([^"]*)"|(\w+))\))?')) {
            $neg = $x.Groups[1].Value -eq '!'; $name = $x.Groups[2].Value
            if ($name -in 'cresc', 'decresc', 'dim') { if (-not $neg) { $m.Add("hairpin:$name") } }
            # (an octave line's start, by kind: 8va, 8vb, 15ma, 15mb; its end is not counted, as a hairpin's)
            elseif ($name -in '8va', '8vb', '15ma', '15mb') { if (-not $neg) { $m.Add("ottava:$name") } }
            # (a text by its words, over or under alike)
            elseif ($name -eq 'text') { $m.Add("text:$($x.Groups[3].Value)") }
            # (a text spanner (S25) by its words, @rit the same as @textSpan("rit."), at the bar it starts;
            # and the bar it ends, the note its @!rit stands on)
            elseif ($name -in 'rit', 'accel', 'rall', 'textSpan') {
                if ($neg) { $m.Add('spanend:') } else { $m.Add("span:$(if ($name -eq 'textSpan') { $x.Groups[3].Value } else { "$name." })") }
            }
            # (a trill line's start and the bar it ends, as a text spanner's)
            elseif ($name -eq 'trillSpan') { if ($neg) { $m.Add('trillend:') } else { $m.Add('trill:') } }
            elseif ($name -eq 'arpeggio') { $m.Add('arpeggio:') }
            elseif ($name -eq 'glissando') { $m.Add('glissando:') }
            elseif ($name -eq 'sustain') { $m.Add($(if ($neg) { 'pedal:up' } else { 'pedal:down' })) }
            elseif ($name -match '^(p+|f+|mp|mf|sf|sfz|sffz|fp|fz|rf|rfz)$' -and -not $neg) { $m.Add("dynamic:$name") }
            elseif ($name -in 'staccato', 'accent', 'tenuto', 'marcato', 'staccatissimo', 'fermata') { $m.Add("artic:$name") }
            elseif ($name -in 'trill', 'mordent', 'prall', 'turn', 'reverseTurn') { $m.Add("ornament:$name") }
            # (the rarer scripts of OmrGen --scripts2, by name likewise)
            elseif ($name -in 'portato', 'stopped', 'pralltriller') { $m.Add("scripts2:$name") }
        }
        $parts[$part].Add($m)
    }
    $parts
}

# the form's signs (S22), each at its bar in the written order: "repeat:open@5" (the bar a |: opens),
# "repeat:close@8" (the bar a :| closes), "volta:1@7" (the bar an ending begins), "volta:end@8" (the bar
# it ends), "nav:segno@3", "nav:coda@9" (the bar the sign opens), "nav:tocoda@6", "nav:dsalcoda@8" (the
# bar the words follow). From the form line and the sections' lengths (the first part's bars, a
# multi-bar rest's counted)
function FormEvents([string]$path) {
    $lengths = @{}; $section = $null; $part = $null; $depth = 0; $form = $null; $counted = @{}
    foreach ($line in Get-Content $path) {
        $t = $line.Trim()
        if ($t -match '^form\s*\{(.*)\}') { $form = $Matches[1]; continue }
        if ($t -match '^section (\S+)') { $section = $Matches[1]; $depth = 1; $part = $null; $lengths[$section] = 0; continue }
        if ($depth -eq 1 -and $t -match '^(\w+) \{$') { if ($part) { $depth = 2 } else { $part = $Matches[1]; if ($counted.ContainsKey($section)) { $part = $null; $depth = 3 } }; continue }
        if ($t -eq '}') { if ($depth -ge 2) { $depth = 1 } elseif ($part) { $part = $null; $counted[$section] = $true; $depth = 1 } else { $depth = 0; $section = $null }; continue }
        if (-not $part -or $depth -ne 1 -or -not $section) { continue }
        $music = ($t -replace '//.*$', '')
        if ($music -match '\|') { $lengths[$section] += $(if ($music -match '\bR[\d.]*\*(\d+)') { [int]$Matches[1] } else { 1 }) }
    }
    $events = [Collections.Generic.List[string]]::new()
    if (-not $form) { return @() }
    $f = $form -replace 'to coda', 'tocoda' -replace 'ds al coda', 'dsalcoda' -replace 'dc al coda', 'dcalcoda' -replace 'ds al fine', 'dsalfine' -replace 'dc al fine', 'dcalfine'
    $f = $f -replace '-?\]', ' ] ' -replace '\[', ' [ '
    $bar = 0
    foreach ($tok in ($f -split '\s+' | Where-Object { $_ })) {
        if ($tok -eq '|:') { $events.Add("repeat:open@$($bar + 1)") }
        elseif ($tok -match '^:\|') { $events.Add("repeat:close@$bar") }
        elseif ($tok -eq '[') { }
        elseif ($tok -match '^(\d+(?:[-,]\d+)*)\.$') {
            # (the passes the ending plays on, spelt out and sorted: 1-2 and 1,2 are one ending)
            $passes = @(foreach ($r in $Matches[1] -split ',') { $ab = $r -split '-'; ([int]$ab[0])..([int]$ab[-1]) }) | Sort-Object -Unique
            $events.Add("volta:$($passes -join ',')@$($bar + 1)")
        }
        elseif ($tok -eq ']') { $events.Add("volta:end@$bar") }
        elseif ($tok -in 'segno', 'coda') { $events.Add("nav:$tok@$($bar + 1)") }
        elseif ($tok -in 'tocoda', 'dsalcoda', 'dcalcoda', 'dsalfine', 'dcalfine', 'fine', 'ds', 'dc') { $events.Add("nav:$tok@$bar") }
        elseif ($tok -match '^~?(\w+)') { $n = $Matches[1]; if ($lengths.ContainsKey($n)) { $bar += $lengths[$n] } }
    }
    # (unrolled into strings: a List returned whole would be one item)
    $events.ToArray()
}

foreach ($dir in Get-ChildItem $Out -Recurse -Directory | Where-Object { Test-Path "$($_.FullName)\omr\omr.lys" }) {
    $name = $dir.Name
    $truth = Marks "$($dir.FullName)\$name.lys"
    $read = Marks "$($dir.FullName)\omr\omr.lys"
    # the form's signs, the piece's as a whole
    $want = [Collections.Generic.List[string]]::new([string[]]@(FormEvents "$($dir.FullName)\$name.lys"))
    $got = [Collections.Generic.List[string]]::new([string[]]@(FormEvents "$($dir.FullName)\omr\omr.lys"))
    foreach ($k in 'repeat', 'volta', 'nav') {
        $tot[$k].truth += @($want | Where-Object { $_.StartsWith("${k}:") }).Count
        $tot[$k].omr += @($got | Where-Object { $_.StartsWith("${k}:") }).Count
    }
    foreach ($w in @($want)) {
        $k = $w.Split(':')[0]
        if ($got.Remove($w)) { $tot[$k].found++ } elseif ($Detail) { $lines += "$name form: missed $w" }
    }
    if ($Detail) { foreach ($g in $got) { $lines += "$name form: extra $g" } }
    foreach ($p in $truth.Keys) {
        $tb = $truth[$p]; $rb = $read[$p]
        for ($b = 0; $b -lt $tb.Count; $b++) {
            $want = [Collections.Generic.List[string]]::new($tb[$b])
            # (not an if expression: PowerShell would unroll an empty list to $null)
            $got = [Collections.Generic.List[string]]::new()
            if ($rb -and $b -lt $rb.Count) { $got.AddRange([string[]]@($rb[$b])) }
            foreach ($k in $kinds) {
                $tot[$k].truth += @($want | Where-Object { $_.StartsWith("${k}:") }).Count
                $tot[$k].omr += @($got | Where-Object { $_.StartsWith("${k}:") }).Count
            }
            foreach ($w in @($want)) {
                $k = $w.Split(':')[0]
                if ($got.Remove($w)) { $tot[$k].found++ } elseif ($Detail) { $lines += "$name $p bar $($b + 1): missed $w" }
            }
            if ($Detail) { foreach ($g in $got) { $lines += "$name $p bar $($b + 1): extra $g" } }
        }
    }
}
foreach ($k in $kinds) {
    $t = $tot[$k]
    "{0,-8} truth {1,5}  read {2,5}  found {3,5}  recall {4,5:F1}%  precision {5,5:F1}%" -f $k, $t.truth, $t.omr, $t.found,
        (100.0 * $t.found / [Math]::Max(1, $t.truth)), (100.0 * $t.found / [Math]::Max(1, $t.omr))
}
if ($Detail) { $lines }
