# UI tests for the engine on a real Windows desktop (GitHub windows-latest).
# Stages build on each other; later plans add stages after "smoke".
param(
    [ValidateSet('smoke', 'idle')][string[]]$Stage = @('smoke'),
    [string]$Exe = "$PSScriptRoot\..\..\artifacts\engine\Sahar.Engine.exe",
    [string]$Out = "$PSScriptRoot\..\..\artifacts\ui"
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Exe = (Resolve-Path $Exe).Path
$script:failures = @()

function Step([string]$name, [scriptblock]$body) {
    Write-Host "--- $name"
    try { & $body; Write-Host "    ok" }
    catch { Write-Host "    FAIL: $_"; $script:failures += "${name}: $_" }
}

function Send([string]$json) {
    $reply = & "$PSScriptRoot\pipe.ps1" -Line $json
    Write-Host "    -> $json`n    <- $reply"
    if (-not $reply) { throw "no reply to $json" }
    $reply | ConvertFrom-Json
}

function Wait-Pipe([int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path '\\.\pipe\sahar') { return }
        Start-Sleep -Milliseconds 100
    }
    throw "pipe 'sahar' did not appear within $seconds s"
}

function Start-Engine {
    $script:engine = Start-Process -FilePath $Exe -ArgumentList '--test-mode', '--capturable' -PassThru
    Wait-Pipe 5
}

function Stop-Engine {
    if ($script:engine -and -not $script:engine.HasExited) {
        try { Send '{"type":"cmd","name":"quit"}' | Out-Null } catch { }
        if (-not $script:engine.WaitForExit(3000)) { $script:engine.Kill() }
    }
}

function Screenshot([string]$name) {
    $path = Join-Path $Out $name
    & agent-desktop screenshot --screen 0 $path | Out-Null
    if (-not (Test-Path $path)) { throw "screenshot $name was not written" }
    $path
}

Add-Type -AssemblyName System.Drawing
function Pixel([string]$path, [int]$x, [int]$y) {
    $bmp = [System.Drawing.Bitmap]::new($path)
    try { $bmp.GetPixel($x, $y) } finally { $bmp.Dispose() }
}

# Calm body colour (palette.json "Calm".mid) and how far a sampled pixel may be from it.
$CalmMid = @{ R = 0xF1; G = 0xEC; B = 0xFF }
function Assert-Body([string]$shot, [string]$baseline, $state) {
    # Upper body, between the leaf and the eyes: only the body gradient is drawn there.
    $x = [int]($state.x + $state.width / 2)
    $y = [int]($state.y + $state.height * 0.25)
    $p = Pixel $shot $x $y
    $b = Pixel $baseline $x $y
    Write-Host "    pixel ($x,$y) = $($p.R),$($p.G),$($p.B); wallpaper = $($b.R),$($b.G),$($b.B)"
    foreach ($c in 'R', 'G', 'B') {
        if ([math]::Abs($p.$c - $CalmMid[$c]) -gt 40) { throw "body pixel $c=$($p.$c) is not near Calm mid $($CalmMid[$c])" }
    }
    $diff = [math]::Abs($p.R - $b.R) + [math]::Abs($p.G - $b.G) + [math]::Abs($p.B - $b.B)
    if ($diff -lt 30) { throw "body pixel equals the wallpaper (diff $diff): Sahar is not drawn" }
}

function Save-Log {
    $log = Join-Path $env:LOCALAPPDATA 'Sahar\engine.log'
    if (Test-Path $log) { Copy-Item $log (Join-Path $Out 'engine.log') -Force }
}

function Stage-Smoke {
    Step 'start engine' { Start-Engine }

    Step 'state replies ok' {
        $r = Send '{"type":"cmd","name":"state"}'
        if (-not $r.ok) { throw "state not ok: $($r.error)" }
        if ($null -eq $r.state) { throw 'state missing' }
        if ($r.state.workingSetBytes -le 0) { throw 'workingSetBytes not reported' }
    }

    Step 'unknown command is rejected' {
        $r = Send '{"type":"cmd","name":"no-such-command"}'
        if ($r.ok) { throw 'unknown command answered ok' }
    }

    Step 'garbage line is rejected, pipe survives' {
        $r = Send 'not json'
        if ($r.ok) { throw 'garbage answered ok' }
        $r = Send '{"type":"cmd","name":"state"}'
        if (-not $r.ok) { throw 'state not ok after garbage' }
    }

    Step 'second instance exits with code 3' {
        $second = Start-Process -FilePath $Exe -ArgumentList '--test-mode' -PassThru
        if (-not $second.WaitForExit(5000)) { $second.Kill(); throw 'second instance kept running' }
        if ($second.ExitCode -ne 3) { throw "second instance exit code $($second.ExitCode)" }
    }

    Step 'screenshot' { Screenshot 'smoke.png' | Out-Null }

    Step 'quit exits with code 0 within 3 s' {
        $r = Send '{"type":"cmd","name":"quit"}'
        if (-not $r.ok) { throw 'quit not ok' }
        if (-not $script:engine.WaitForExit(3000)) { throw 'engine still running 3 s after quit' }
        if ($script:engine.ExitCode -ne 0) { throw "engine exit code $($script:engine.ExitCode)" }
    }
}

function Stage-Idle {
    $script:baseline = $null
    $script:state = $null
    Step 'baseline screenshot (no engine)' { $script:baseline = Screenshot 'idle-baseline.png' }
    Step 'start engine' { Start-Engine }
    Step 'pose at (500,400)' {
        $r = Send '{"type":"cmd","name":"pose","args":{"x":"500","y":"400"}}'
        if (-not $r.ok) { throw "pose not ok: $($r.error)" }
        $script:state = (Send '{"type":"cmd","name":"state"}').state
        if ($script:state.width -le 0) { throw 'body has no size' }
    }
    Step 'Sahar is drawn' {
        Start-Sleep -Milliseconds 1500
        Assert-Body (Screenshot 'idle.png') $script:baseline $script:state
    }
    Step 'Sahar survives a lost graphics device' {
        $r = Send '{"type":"cmd","name":"debug-device-lost"}'
        if (-not $r.ok) { throw "debug-device-lost not ok: $($r.error)" }
        Start-Sleep -Milliseconds 1000
        Assert-Body (Screenshot 'idle-after-device-lost.png') $script:baseline $script:state
    }
    Step 'memory under 60 MB' {
        $ws = (Send '{"type":"cmd","name":"state"}').state.workingSetBytes
        Write-Host "    working set $([math]::Round($ws / 1MB, 1)) MB"
        if ($ws -gt 60MB) { throw "working set $ws bytes" }
    }
    Stop-Engine
}

try {
    foreach ($s in $Stage) {
        Write-Host "=== stage $s"
        switch ($s) {
            'smoke' { Stage-Smoke }
            'idle' { Stage-Idle }
        }
    }
}
finally {
    if ($script:engine -and -not $script:engine.HasExited) { $script:engine.Kill() }
    Save-Log
}

if ($script:failures.Count) {
    Write-Host "`n$($script:failures.Count) failure(s):"
    $script:failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "`nstages '$($Stage -join ',')' passed"
