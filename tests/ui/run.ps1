# UI tests for the engine on a real Windows desktop (GitHub windows-latest).
# Stages build on each other; later plans add stages after "smoke".
param(
    [ValidateSet('smoke', 'idle', 'gallery', 'motion')][string[]]$Stage = @('smoke'),
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

# Expression keys 1–25 in catalog order (src/Sahar.Core/assets/expressions.json).
$ExpressionKeys = @('music', 'claude-working', 'claude-permission', 'claude-done', 'timer-done', 'celebrate', 'rain',
    'battery-low', 'charging', 'download', 'sleep', 'back', 'notification', 'dnd', 'offline', 'online', 'screenshot',
    'copied', 'reminder', 'error', 'hot', 'poked', 'dragged', 'break', 'late-night')

function Crop([string]$path, [int]$cx, [int]$cy, [int]$size, [string]$to) {
    $src = [System.Drawing.Bitmap]::new($path)
    try {
        $dst = [System.Drawing.Bitmap]::new($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($dst)
        $g.DrawImage($src, [System.Drawing.Rectangle]::new(0, 0, $size, $size),
            [System.Drawing.Rectangle]::new($cx - $size / 2, $cy - $size / 2, $size, $size), [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $dst.Save($to, [System.Drawing.Imaging.ImageFormat]::Png)
        $dst.Dispose()
    }
    finally { $src.Dispose() }
}

function Contact-Sheet([string[]]$tiles, [int]$columns, [int]$size, [string]$to) {
    $rows = [math]::Ceiling($tiles.Count / $columns)
    $sheet = [System.Drawing.Bitmap]::new($columns * $size, $rows * $size)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $font = [System.Drawing.Font]::new('Segoe UI', 11)
    for ($i = 0; $i -lt $tiles.Count; $i++) {
        $img = [System.Drawing.Image]::FromFile($tiles[$i])
        $x = ($i % $columns) * $size; $y = [math]::Floor($i / $columns) * $size
        $g.DrawImage($img, $x, $y, $size, $size)
        $g.DrawString([System.IO.Path]::GetFileNameWithoutExtension($tiles[$i]), $font, [System.Drawing.Brushes]::White, $x + 6, $y + 4)
        $img.Dispose()
    }
    $g.Dispose()
    $sheet.Save($to, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
}

Add-Type -AssemblyName System.Windows.Forms
$Work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea

$MotionKinds = @('Wander', 'Hover', 'PeekTaskbar', 'EyesFollow', 'Drag', 'Throw', 'LandAndSit', 'SleepInCorner',
    'Wake', 'FullscreenHide', 'PerchWindow', 'CelebrateLoop')

function State { (Send '{"type":"cmd","name":"state"}').state }

function Assert-TopLeftInWork($st, [string]$what) {
    if ($st.x -lt $Work.Left -or $st.y -lt $Work.Top -or $st.x -gt $Work.Right -or $st.y -gt $Work.Bottom) {
        throw "$what at ($($st.x),$($st.y)) is outside the work area $Work"
    }
}

function Assert-BodyInWork($st, [string]$what) {
    $t = 2
    if ($st.x -lt $Work.Left - $t -or $st.y -lt $Work.Top - $t -or
        $st.x + $st.width -gt $Work.Right + $t -or $st.y + $st.height -gt $Work.Bottom + $t) {
        throw "$what body ($($st.x),$($st.y) $($st.width)x$($st.height)) is not inside the work area $Work"
    }
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

function Stage-Gallery {
    $dir = Join-Path $Out 'gallery'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $script:tiles = @()
    Step 'start engine' { Start-Engine }
    Step 'pose at (400,250)' {
        $r = Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}'
        if (-not $r.ok) { throw "pose not ok: $($r.error)" }
    }
    for ($i = 0; $i -lt $ExpressionKeys.Count; $i++) {
        $key = $ExpressionKeys[$i]
        $n = '{0:D2}-{1}' -f ($i + 1), $key
        Step "expression $n" {
            $r = Send ('{"type":"cmd","name":"expression","args":{"key":"' + $key + '"}}')
            if (-not $r.ok) { throw "expression $key not ok: $($r.error)" }
            Start-Sleep -Milliseconds 700
            $st = (Send '{"type":"cmd","name":"state"}').state
            if ($st.expression -ne $key) { throw "state.expression is '$($st.expression)', expected '$key'" }
            $shot = Screenshot "gallery-full.png"
            $tile = Join-Path $dir "$n.png"
            Crop $shot ([int]($st.x + $st.width / 2)) ([int]($st.y + $st.height / 2 - 20)) 260 $tile
            $script:tiles += $tile
        }
    }
    Step 'unknown expression is rejected' {
        $r = Send '{"type":"cmd","name":"expression","args":{"key":"no-such-face"}}'
        if ($r.ok) { throw 'unknown expression answered ok' }
        if ($r.error -ne 'unknown expression') { throw "error was '$($r.error)'" }
    }
    Step 'contact sheet' { Contact-Sheet $script:tiles 5 260 (Join-Path $Out 'gallery.png') }
    Stop-Engine
}

function Stage-Motion {
    $dir = Join-Path $Out 'motion'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Step 'start engine' { Start-Engine }
    Step 'pose at (400,250)' {
        $r = Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}'
        if (-not $r.ok) { throw "pose not ok: $($r.error)" }
    }
    for ($i = 0; $i -lt $MotionKinds.Count; $i++) {
        $kind = $MotionKinds[$i]
        $n = '{0:D2}-{1}' -f ($i + 1), $kind
        Step "motion $n" {
            $r = Send ('{"type":"cmd","name":"motion","args":{"kind":"' + $kind + '"}}')
            if (-not $r.ok) { throw "motion $kind not ok: $($r.error)" }
            if ($r.state.motion -ne $kind) { throw "state.motion is '$($r.state.motion)', expected '$kind'" }
            $frames = Join-Path $dir $n
            New-Item -ItemType Directory -Force -Path $frames | Out-Null
            for ($f = 1; $f -le 6; $f++) {
                Start-Sleep -Milliseconds 250
                $st = State
                Assert-TopLeftInWork $st "$kind frame $f"
                $shot = Screenshot 'motion-full.png'
                Copy-Item $shot (Join-Path $frames "$f.png") -Force
            }
        }
    }
    Step 'unknown motion is rejected' {
        $r = Send '{"type":"cmd","name":"motion","args":{"kind":"Moonwalk"}}'
        if ($r.ok) { throw 'unknown motion answered ok' }
    }
    Step 'throw comes to rest inside the work area' {
        Send '{"type":"cmd","name":"pose","args":{"x":"300","y":"200"}}' | Out-Null
        $r = Send '{"type":"cmd","name":"motion","args":{"kind":"Throw","vx":"1600","vy":"-600"}}'
        if (-not $r.ok) { throw "throw not ok: $($r.error)" }
        $deadline = (Get-Date).AddSeconds(8)
        do { Start-Sleep -Milliseconds 200; $st = State } while ($st.motion -eq 'Throw' -and (Get-Date) -lt $deadline)
        if ($st.motion -eq 'Throw') { throw 'still flying after 8 s' }
        Screenshot 'motion-throw-rest.png' | Out-Null
        Assert-BodyInWork $st 'after throw'
    }
    Step 'click pokes her' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
        Start-Sleep -Milliseconds 300
        $st = State
        $xy = '{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)
        & agent-desktop --headed mouse-click --xy $xy | Out-Null
        Start-Sleep -Milliseconds 300
        $st = State
        if ($st.expression -ne 'poked') { throw "expression after click is '$($st.expression)'" }
    }
    Step 'dragging her' {
        Start-Sleep -Milliseconds 1800
        $st = State
        $from = '{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)
        $to = '{0},{1}' -f [int]($st.x + $st.width / 2 + 200), [int]($st.y + $st.height / 2 - 60)
        $drag = Start-Job -ScriptBlock {
            param($from, $to)
            & agent-desktop --headed drag --from-xy $from --to-xy $to --duration 1600 --drop-delay 200 --timeout-ms 8000
        } -ArgumentList $from, $to
        Start-Sleep -Milliseconds 1400
        $during = State
        Screenshot 'motion-dragging.png' | Out-Null
        Wait-Job $drag -Timeout 10 | Out-Null
        Write-Host "    drag: $(Receive-Job $drag)"
        Remove-Job $drag -Force
        Start-Sleep -Milliseconds 1500
        $after = State
        Write-Host "    during: $($during.motion), after: $($after.motion)"
        if ($during.motion -ne 'Drag') { throw "motion while dragging is '$($during.motion)'" }
        if ($after.motion -eq 'Drag') { throw 'still dragging after release' }
        Assert-BodyInWork $after 'after drag'
    }
    Stop-Engine
}

try {
    foreach ($s in $Stage) {
        Write-Host "=== stage $s"
        switch ($s) {
            'smoke' { Stage-Smoke }
            'idle' { Stage-Idle }
            'gallery' { Stage-Gallery }
            'motion' { Stage-Motion }
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
