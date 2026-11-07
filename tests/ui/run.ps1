# UI tests for the engine on a real Windows desktop (GitHub windows-latest).
# Stages build on each other; later plans add stages after "smoke".
param(
    [ValidateSet('smoke', 'idle', 'gallery', 'motion', 'events', 'perf', 'life', 'menu', 'senses', 'prayer')][string[]]$Stage = @('smoke'),
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

function Start-Engine([string[]]$Extra = @()) {
    $script:engine = Start-Process -FilePath $Exe -ArgumentList (@('--test-mode', '--capturable') + $Extra) -PassThru
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

function Post([string]$name) {
    $r = Send ('{"type":"event","name":"' + $name + '"}')
    if (-not $r.ok) { throw "event $name not ok: $($r.error)" }
    $r
}

function Wait-State([scriptblock]$cond, [int]$ms, [string]$what) {
    $deadline = (Get-Date).AddMilliseconds($ms)
    do {
        $st = State
        if (& $cond $st) { return $st }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "$what not seen within $ms ms (expression '$($st.expression)', hidden $($st.hidden))"
}

# A window in its own process: 'borderless' covers the whole monitor (like a game), 'maximized' has a caption.
function Start-TestWindow([string]$kind) {
    $script = @"
Add-Type -AssemblyName System.Windows.Forms
`$f = New-Object System.Windows.Forms.Form
`$f.Text = 'Sahar test $kind'
`$f.BackColor = 'DarkSlateBlue'
if ('$kind' -eq 'borderless') { `$f.FormBorderStyle = 'None'; `$f.TopMost = `$true }
if ('$kind' -eq 'typing') {
    `$f.StartPosition = 'Manual'; `$f.Location = New-Object System.Drawing.Point(150, 120); `$f.Size = New-Object System.Drawing.Size(560, 380)
    `$t = New-Object System.Windows.Forms.TextBox; `$t.Multiline = `$true; `$t.Dock = 'Fill'; `$t.Font = New-Object System.Drawing.Font('Segoe UI', 14)
    `$f.Controls.Add(`$t)
} else { `$f.WindowState = 'Maximized' }
`$f.Add_Shown({ `$f.Activate() })
[System.Windows.Forms.Application]::Run(`$f)
"@
    $p = Start-Process -FilePath 'pwsh' -ArgumentList '-NoProfile', '-Command', $script -PassThru
    # Wait until the window really exists (a cold pwsh + WinForms start can take several seconds).
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 200; $p.Refresh() } while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($p.MainWindowHandle -eq 0) { throw "test window '$kind' did not appear within 15 s" }
    Start-Sleep -Milliseconds 300
    # Windows refuses a new process the foreground while the runner's terminal has it; a real click on the
    # window takes it (Sahar's overlay is click-through, and she sits bottom-right, away from this point).
    $click = if ($kind -eq 'typing') { '430,320' } else { '120,300' }   # inside the window
    & agent-desktop --headed mouse-click --xy $click 2>&1 | Out-Null
    Start-Sleep -Milliseconds 300
    $p
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Shell {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    public static void TaskbarCreated() { PostMessage((IntPtr)0xFFFF, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero); }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    /// Tells Sahar's tray window that Windows is shutting down (WM_ENDSESSION, wParam TRUE).
    public static bool EndSession() { var h = FindWindow("Sahar.Engine", "Sahar tray"); return h != IntPtr.Zero && PostMessage(h, 0x16, (IntPtr)1, IntPtr.Zero); }
}
"@

# Test-mode diagnostics: "generation=N overlayVisible=true|false".
function Info {
    $r = Send '{"type":"cmd","name":"debug-info"}'
    if (-not $r.ok) { throw "debug-info not ok: $($r.error)" }
    $h = @{}
    foreach ($pair in ($r.error -split ' ')) { $k, $v = $pair -split '=', 2; $h[$k] = $v }
    $h
}

function Wait-Overlay([string]$visible, [int]$ms, [string]$what) {
    $deadline = (Get-Date).AddMilliseconds($ms)
    do {
        if ((Info).overlayVisible -eq $visible) { return }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw $what
}

# Test-mode life diagnostics: "level=… next=… last=… typing=… video=… actions=N weights=Wander:1.00,…".
function Life {
    $r = Send '{"type":"cmd","name":"debug-life"}'
    if (-not $r.ok) { throw "debug-life not ok: $($r.error)" }
    $h = @{}
    foreach ($pair in ($r.error -split ' ')) { $k, $v = $pair -split '=', 2; $h[$k] = $v }
    $h
}

function Weight($life, [string]$action) {
    foreach ($w in ($life.weights -split ',')) { $n, $v = $w -split ':'; if ($n -eq $action) { return [double]$v } }
    1.0
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

    Step 'an exception in a motion tick does not end the engine' {
        $r = Send '{"type":"cmd","name":"debug-throw-in-tick"}'
        if (-not $r.ok) { throw "debug-throw-in-tick not ok: $($r.error)" }
        Start-Sleep -Milliseconds 500
        if ($script:engine.HasExited) { throw "engine exited with $($script:engine.ExitCode)" }
        if (-not (Send '{"type":"cmd","name":"state"}').ok) { throw 'state not ok after the exception' }
    }

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
    Step 'a GPU removed at rest is noticed and she is redrawn' {
        $before = [int](Info).generation
        $r = Send '{"type":"cmd","name":"debug-simulate-device-removed"}'
        if (-not $r.ok) { throw "debug-simulate-device-removed not ok: $($r.error)" }
        Start-Sleep -Milliseconds 2500
        $after = [int](Info).generation
        if ($after -le $before) { throw "device generation stayed ${before}: removal not detected without a draw" }
        Assert-Body (Screenshot 'idle-after-device-removed.png') $script:baseline $script:state
    }
    Step 'after a display change she is back inside the work area' {
        Send '{"type":"cmd","name":"pose","args":{"x":"5000","y":"3000"}}' | Out-Null
        $r = Send '{"type":"cmd","name":"debug-display-changed"}'
        if (-not $r.ok) { throw "debug-display-changed not ok: $($r.error)" }
        Start-Sleep -Milliseconds 300
        Assert-BodyInWork (State) 'after display change'
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
    foreach ($kind in 'PlayWithMoon', 'Curious', 'Dance', 'Breathe', 'OrbitCursor') {
        Step "motion $kind" {
            Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
            Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
            $r = Send ('{"type":"cmd","name":"motion","args":{"kind":"' + $kind + '"}}')
            if (-not $r.ok) { throw "motion $kind not ok: $($r.error)" }
            if ($r.state.motion -ne $kind) { throw "state.motion is '$($r.state.motion)', expected '$kind'" }
            $frames = Join-Path $dir "new-$kind"
            New-Item -ItemType Directory -Force -Path $frames | Out-Null
            for ($f = 1; $f -le 4; $f++) {
                Start-Sleep -Milliseconds 400
                Assert-BodyInWork (State) "$kind frame $f"
                Copy-Item (Screenshot 'motion-full.png') (Join-Path $frames "$f.png") -Force
            }
        }
    }
    $script:moveTimes = @{ FollowCursor = 6; HideBehindWindow = 7.5; Shy = 2.5; StartupEntry = 3; Goodbye = 2; HopIcons = 3.5; Attention = 3.6; NapOnIcon = 3
        Dizzy = 3.6; Angry = 3.2; WalkTaskbar = 10; Stretch = 2.6; Yawn = 2.3; ScratchHead = 2.3; WatchYou = 7.5; HopWindows = 4.5; MoonToss = 2.6; ChaseButterfly = 6.5 }
    foreach ($kind in 'FollowCursor', 'HideBehindWindow', 'Shy', 'StartupEntry', 'Goodbye', 'HopIcons', 'Attention', 'NapOnIcon',
        'Dizzy', 'Angry', 'WalkTaskbar', 'Stretch', 'Yawn', 'ScratchHead', 'WatchYou', 'HopWindows', 'MoonToss', 'ChaseButterfly') {
        Step "motion $kind" {
            $w = $null
            if ($kind -in 'HideBehindWindow', 'WatchYou', 'HopWindows') { $w = Start-TestWindow 'typing' }
            try {
                Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
                Send '{"type":"cmd","name":"pose","args":{"x":"700","y":"250"}}' | Out-Null
                [Mouse]::Move(5, 5)   # pointer away from her
                if ($w) { & agent-desktop --headed mouse-click --xy '430,320' 2>&1 | Out-Null; Start-Sleep -Milliseconds 300 }
                $r = Send ('{"type":"cmd","name":"motion","args":{"kind":"' + $kind + '"}}')
                if (-not $r.ok) { throw "motion $kind not ok: $($r.error)" }
                if ($r.state.motion -ne $kind) { throw "state.motion is '$($r.state.motion)', expected '$kind'" }
                $frames = Join-Path $dir "move-$kind"
                New-Item -ItemType Directory -Force -Path $frames | Out-Null
                $t0 = Get-Date
                $f = 0
                $end = $t0.AddSeconds($script:moveTimes[$kind] + 2)
                $back = $false
                while ((Get-Date) -lt $end) {
                    Start-Sleep -Milliseconds 200
                    $st = State
                    if ($kind -ne 'StartupEntry' -or ((Get-Date) - $t0).TotalSeconds -gt 1.8) { Assert-BodyInWork $st "$kind" }
                    if ($f -lt 2 -and ((Get-Date) - $t0).TotalSeconds -gt (0.4 + $f * 1.2)) {
                        $f++
                        Copy-Item (Screenshot 'motion-full.png') (Join-Path $frames "$f.png") -Force
                    }
                    if ($kind -notin 'Goodbye', 'NapOnIcon' -and $st.motion -eq 'Hover') { $back = $true; break }
                }
                if ($kind -notin 'Goodbye', 'NapOnIcon' -and -not $back) { throw "$kind did not end in Hover (motion '$((State).motion)')" }
                if ($kind -in 'Goodbye', 'NapOnIcon') {
                    if ((State).motion -ne $kind) { throw "$kind ended early: '$((State).motion)'" }
                    Send '{"type":"cmd","name":"motion","args":{"kind":"Wake"}}' | Out-Null
                    Start-Sleep -Seconds 1
                }
            }
            finally { if ($w) { $w.Kill() } }
        }
    }
    Step 'no hello in test mode unless asked' {
        Stop-Engine
        Start-Engine
        Start-Sleep -Milliseconds 800
        if ((State).motion -eq 'StartupEntry') { throw 'she said hello without --simulate-boot' }
    }
    Step 'the PC just started: she flies in and says hello' {
        Stop-Engine
        Start-Engine @('--simulate-boot')
        Wait-State { param($st) $st.motion -eq 'StartupEntry' } 3000 'hello at start' | Out-Null
        Wait-State { param($st) $st.motion -eq 'Hover' } 6000 'hovering after hello' | Out-Null
    }
    Step 'every flight stays on screen, from every corner' {
        $patterns = 'Zigzag', 'FigureEight', 'SpiralUp', 'Dive', 'Wave', 'GlideHigh', 'BigCircle', 'DashAndStop', 'FallingLeaf', 'TaskbarPatrol'
        $st = State
        $right = $Work.Right - $st.width; $bottom = $Work.Bottom - $st.height
        $corners = @(@($Work.Left, $Work.Top), @($right, $Work.Top), @($Work.Left, $bottom), @($right, $bottom))
        foreach ($p in $patterns) {
            $c = $corners[[array]::IndexOf($patterns, $p) % 4]
            Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
            # pose takes the canvas top-left: shift by the body's offset inside the canvas.
            $i = Info; $s0 = State
            $dx = $s0.x - [double]$i.canvasX; $dy = $s0.y - [double]$i.canvasY
            Send ('{"type":"cmd","name":"pose","args":{"x":"' + [int]($c[0] - $dx) + '","y":"' + [int]($c[1] - $dy) + '"}}') | Out-Null
            $r = Send ('{"type":"cmd","name":"motion","args":{"kind":"Wander","flight":"' + $p + '"}}')
            if (-not $r.ok) { throw "flight $p not ok: $($r.error)" }
            $end = (Get-Date).AddSeconds(8)
            do {
                Start-Sleep -Milliseconds 300
                $st = State
                Assert-BodyInWork $st "flight $p"
            } while ($st.motion -ne 'Hover' -and (Get-Date) -lt $end)
            if ($st.motion -ne 'Hover') { throw "flight $p did not end" }
        }
        Copy-Item (Screenshot 'motion-full.png') (Join-Path $dir 'flights-end.png') -Force
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
    Step 'her eyes follow the mouse while she hovers' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
        $st = State
        $cy = [int]($st.y + $st.height / 2)
        & agent-desktop --headed mouse-move --xy ('{0},{1}' -f [int]($st.x - 200), $cy) | Out-Null
        Start-Sleep -Milliseconds 700
        $left = (Info).faceOffsetX
        & agent-desktop --headed mouse-move --xy ('{0},{1}' -f [int]($st.x + $st.width + 200), $cy) | Out-Null
        Start-Sleep -Milliseconds 700
        $right = (Info).faceOffsetX
        Write-Host "    face offset: left $left, right $right"
        if ($null -eq $left -or [double]$left -ge 0) { throw "eyes did not look left ($left)" }
        if ($null -eq $right -or [double]$right -le 0) { throw "eyes did not look right ($right)" }
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
        $script:dragExpr = $null
        $drag = Start-Job -ScriptBlock {
            param($from, $to)
            & agent-desktop --headed drag --from-xy $from --to-xy $to --duration 1600 --drop-delay 200 --timeout-ms 8000
        } -ArgumentList $from, $to
        $seen = @()
        $shot = $false
        while ($drag.State -eq 'Running') {
            Start-Sleep -Milliseconds 150
            $cur = State
            $m = $cur.motion
            if ($seen.Count -eq 0 -or $seen[-1] -ne $m) { $seen += $m }
            if ($m -eq 'Drag') { $script:dragExpr = $cur.expression }
            if ($m -eq 'Drag' -and -not $shot) { Screenshot 'motion-dragging.png' | Out-Null; $shot = $true }
        }
        Write-Host "    drag: $(Receive-Job $drag)"
        Remove-Job $drag -Force
        Start-Sleep -Milliseconds 1500
        $after = State
        Write-Host "    motions seen while dragging: $($seen -join ' > '); after: $($after.motion)"
        $during = [pscustomobject]@{ motion = $(if ($seen -contains 'Drag') { 'Drag' } else { $seen -join ',' }) }
        if ($during.motion -ne 'Drag') { throw "motion while dragging is '$($during.motion)'" }
        if ($after.motion -eq 'Drag') { throw 'still dragging after release' }
        if ($script:dragExpr -ne 'dragged') { throw "expression while dragging was '$($script:dragExpr)'" }
        if ($after.expression -eq 'dragged') { throw 'still showing dragged after release' }
        Assert-BodyInWork $after 'after drag'
    }
    Step 'a few slow swings are not a shake' {
        Send '{"type":"cmd","name":"pose","args":{"x":"700","y":"400"}}' | Out-Null
        Send '{"type":"cmd","name":"shake","args":{"swings":"3"}}' | Out-Null
        Start-Sleep -Milliseconds 400
        $k = Kv 'debug-shake'
        if ($k.last -ne 'None' -or (State).motion -in 'Dizzy', 'Angry') { throw "three swings made a reaction: $($k.last)" }
    }
    Step 'shaken: dizzy, then back to normal' {
        Send '{"type":"cmd","name":"pose","args":{"x":"700","y":"400"}}' | Out-Null
        Send '{"type":"cmd","name":"shake","args":{"swings":"6","force":"dizzy"}}' | Out-Null
        Wait-State { param($st) $st.expression -eq 'dizzy' -and $st.motion -eq 'Dizzy' } 1500 'the dizzy face' | Out-Null
        Screenshot 'shake-dizzy.png' | Out-Null
        Wait-State { param($st) $st.motion -eq 'Hover' -and $st.expression -ne 'dizzy' } 6000 'back to normal after dizzy' | Out-Null
        Assert-BodyInWork (State) 'after dizzy'
    }
    Step 'shaken hard: sick, a drop, then a little embarrassed' {
        Send '{"type":"cmd","name":"pose","args":{"x":"700","y":"400"}}' | Out-Null
        Send '{"type":"cmd","name":"shake","args":{"swings":"10","force":"sick"}}' | Out-Null
        Wait-State { param($st) $st.expression -eq 'sick' } 3000 'the sick face' | Out-Null
        Start-Sleep -Milliseconds 400
        Screenshot 'shake-sick.png' | Out-Null
        Wait-State { param($st) $st.motion -eq 'Hover' } 6000 'back to normal after sick' | Out-Null
    }
    Step 'shaken again: angry and sulking; "هِم!" then three pokes make up' {
        Send '{"type":"cmd","name":"pose","args":{"x":"700","y":"400"}}' | Out-Null
        Send '{"type":"cmd","name":"shake","args":{"swings":"6","force":"angry"}}' | Out-Null
        Wait-State { param($st) $st.expression -eq 'angry' } 1500 'the angry face' | Out-Null
        Screenshot 'shake-angry.png' | Out-Null
        if ((Kv 'debug-shake').sulking -ne 'true') { throw 'not sulking after an angry shake' }
        Wait-State { param($st) $st.motion -eq 'Hover' } 4000 'resting while sulking' | Out-Null
        if ((State).expression -ne 'angry') { throw 'the angry face did not stay while sulking' }
        for ($i = 1; $i -le 3; $i++) {
            $st = State
            $xy = '{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)
            & agent-desktop --headed mouse-click --xy $xy | Out-Null
            Start-Sleep -Milliseconds 300
            $said = Text64 (Kv 'debug-comment').text64
            Start-Sleep -Milliseconds 900   # she may drift away from the pointer while sulking
            if ($i -lt 3 -and $said -ne 'هِم! 😤') { throw "poke $i while sulking said '$said'" }
        }
        if ((Kv 'debug-shake').sulking -ne 'false') { throw 'three pokes did not end the sulk' }
        if ($said -ne 'خلص سامحتك 💜') { throw "making up said '$said'" }
    }
    Stop-Engine
}

function Stage-Events {
    Step 'start engine' { Start-Engine }
    Step 'unknown event is rejected' {
        $r = Send '{"type":"event","name":"no.such.event"}'
        if ($r.ok) { throw 'unknown event answered ok' }
    }
    Step 'claude.permission shows at once' {
        $r = Post 'claude.permission'
        if ($r.state.expression -ne 'claude-permission') { throw "expression is '$($r.state.expression)'" }
    }
    Step 'permission interrupts music, then music comes back' {
        Start-Sleep -Milliseconds 8500
        Post 'music' | Out-Null
        $r = Post 'claude.permission'
        if ($r.state.expression -ne 'claude-permission') { throw "expression is '$($r.state.expression)'" }
        Start-Sleep -Milliseconds 8500
        Wait-State { param($st) $st.expression -eq 'music' } 1500 'music after the permission expired' | Out-Null
    }
    Step 'an equally important event waits' {
        Post 'screenshot' | Out-Null
        $r = Post 'clipboard.copied'
        if ($r.state.expression -ne 'screenshot') { throw "expression is '$($r.state.expression)'" }
        Post 'music.stop' | Out-Null
    }
    Step 'a borderless window covering the screen hides her completely' {
        $script:pre = State
        $script:win = Start-TestWindow 'borderless'
        try {
            try { Wait-State { param($st) $st.hidden } 2000 'hidden' | Out-Null }
            catch {
                Write-Host "    foreground: $((Send '{"type":"cmd","name":"debug-foreground"}').error)"
                throw
            }
            finally { Screenshot 'events-fullscreen.png' | Out-Null }
            Wait-Overlay 'false' 2000 'overlay still visible while hidden (a dot over the fullscreen app)'
        }
        finally { Stop-Process -Id $script:win.Id -Force -ErrorAction SilentlyContinue }
        $back = Wait-State { param($st) -not $st.hidden } 2000 'shown again'
        if ((Info).overlayVisible -ne 'true') { throw 'overlay not shown again' }
        Start-Sleep -Milliseconds 300
        $back = State
        if ([math]::Abs($back.x - $script:pre.x) -gt 2 -or [math]::Abs($back.y - $script:pre.y) -gt 2) {
            throw "came back at ($($back.x),$($back.y)), was at ($($script:pre.x),$($script:pre.y))"
        }
    }
    Step 'after coming back she returns to her own face and hovers' {
        Start-Sleep -Milliseconds 3000
        $st = State
        if ($st.expression -eq 'back') { throw "still showing 'back'" }
        if ($st.motion -ne 'Hover') { throw "motion is '$($st.motion)' after waking" }
    }
    Step 'hide for an hour, then show from the tray' {
        $r = Send '{"type":"cmd","name":"hide","args":{"minutes":"60"}}'
        if (-not $r.ok) { throw "hide not ok: $($r.error)" }
        Wait-State { param($st) $st.hidden } 1000 'hidden by hide' | Out-Null
        try { Wait-Overlay 'false' 2000 'overlay still visible after hide' }
        finally { $r = Send '{"type":"cmd","name":"show"}' }
        if (-not $r.ok) { throw "show not ok: $($r.error)" }
        Wait-State { param($st) -not $st.hidden } 1000 'shown by show' | Out-Null
    }
    Step 'a maximized window does not hide her' {
        $script:win = Start-TestWindow 'maximized'
        try {
            Start-Sleep -Milliseconds 1000
            if ((State).hidden) { throw 'hidden behind a maximized window' }
            Screenshot 'events-maximized.png' | Out-Null
        }
        finally { Stop-Process -Id $script:win.Id -Force -ErrorAction SilentlyContinue }
    }
    Step 'tray icon comes back after TaskbarCreated' {
        [Shell]::TaskbarCreated()
        Start-Sleep -Milliseconds 1000
        $found = $false
        # Visible tray icons are named on the taskbar; agent-desktop 1.2.0 cannot open the Windows 11 overflow
        # on the runner, so Explorer itself is asked where the icon is (Shell_NotifyIconGetRect).
        $json = (& agent-desktop snapshot --surface taskbar -i --compact 2>&1) -join "`n"
        Set-Content -Path (Join-Path $Out 'tray-taskbar.json') -Value $json
        if ($json -match 'Sahar') { $found = $true; Write-Host '    named on the taskbar' }
        $r = Send '{"type":"cmd","name":"debug-tray"}'
        if ($r.ok) { $found = $true; Write-Host "    Explorer has the icon at ($($r.state.x),$($r.state.y) $($r.state.width)x$($r.state.height))" }
        if (-not $found) { throw "tray icon missing: $($r.error)" }
    }
    Stop-Engine
}

function Stage-Perf {
    Step 'start engine' { Start-Engine }
    Step 'memory at rest under 60 MB' {
        Start-Sleep -Seconds 30
        $ws = (State).workingSetBytes
        Write-Host ("    working set after 30 s at rest: {0:N1} MB" -f ($ws / 1MB))
        if ($ws -ge 60MB) { throw "working set $ws bytes is over 60 MB" }
    }
    Step 'cpu at rest (reported only)' {
        $p = Get-Process -Id $script:engine.Id
        $before = $p.TotalProcessorTime
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        Start-Sleep -Seconds 20
        $p.Refresh()
        $used = ($p.TotalProcessorTime - $before).TotalMilliseconds
        $pct = 100 * $used / $clock.Elapsed.TotalMilliseconds / [Environment]::ProcessorCount
        Write-Host ("    cpu over 20 s: {0:N2} % of the machine ({1:N0} ms on {2} cores)" -f $pct, $used, [Environment]::ProcessorCount)
        if ($pct -ge 0.5) { Write-Host '::warning::engine CPU at rest is over 0.5 % on the runner (software rendering)' }
    }
    Step 'the log never grows past 1 MB' {
        $r = Send '{"type":"cmd","name":"debug-log-fill","args":{"kb":"1200"}}'
        if (-not $r.ok) { throw "debug-log-fill not ok: $($r.error)" }
        $size = (Get-Item (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log')).Length
        Write-Host ("    engine.log after writing 1.2 MB: {0:N0} KB" -f ($size / 1KB))
        if ($size -gt 1MB) { throw "engine.log is $size bytes" }
        if (-not ((Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Tail 5) -match 'fill')) { throw 'the newest lines were not kept' }
    }
    Stop-Engine
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mouse {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    public static void Click(int x, int y, bool right) {
        SetCursorPos(x, y); System.Threading.Thread.Sleep(60);
        mouse_event(right ? 0x0008u : 0x0002u, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(30);
        mouse_event(right ? 0x0010u : 0x0004u, 0, 0, 0, UIntPtr.Zero);
    }
    public static void DoubleClick(int x, int y) { Click(x, y, false); System.Threading.Thread.Sleep(120); Click(x, y, false); }
    public static void Move(int x, int y) { SetCursorPos(x, y); }
}
'@

# debug-menu: "open=true level=main buttons=id:x,y,r;id:x,y,r" (screen px)
function Menu {
    $r = Send '{"type":"cmd","name":"debug-menu"}'
    if (-not $r.ok) { throw "debug-menu not ok: $($r.error)" }
    $h = @{ buttons = @() }
    foreach ($pair in ($r.error -split ' ')) {
        $k, $v = $pair -split '=', 2
        if ($k -eq 'buttons') {
            if ($v) {
                $h.buttons = @($v -split ';' | ForEach-Object {
                    $id, $xyr = $_ -split ':', 2; $p = $xyr -split ','
                    $b = [pscustomobject]@{ id = $id; x = [double]$p[0]; y = [double]$p[1]; r = [double]$p[2]; lx = $null; ly = $null; lw = $null; lh = $null }
                    if ($p.Count -ge 7) { $b.lx = [double]$p[3]; $b.ly = [double]$p[4]; $b.lw = [double]$p[5]; $b.lh = [double]$p[6] }
                    $b
                })
            }
        }
        else { $h[$k] = $v }
    }
    $h
}
function Body-Centre { $st = State; @([int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)) }
function Pose-Body([double]$bx, [double]$by) {
    # pose moves her canvas; shift it so her body's top-left lands on (bx, by)
    $st = State
    $p = Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"300"}}'
    $st = $p.state
    $x = 400 + ($bx - $st.x); $y = 300 + ($by - $st.y)
    (Send ('{"type":"cmd","name":"pose","args":{"x":"' + [int]$x + '","y":"' + [int]$y + '"}}')).state
}
function Wait-Menu([string]$open, [int]$ms, [string]$what) {
    $deadline = (Get-Date).AddMilliseconds($ms)
    do { $m = Menu; if ($m.open -eq $open) { return $m }; Start-Sleep -Milliseconds 100 } while ((Get-Date) -lt $deadline)
    throw "$what not seen within $ms ms (open=$($m.open))"
}
function Press([string]$id) {
    $r = Send ('{"type":"cmd","name":"menu-press","args":{"id":"' + $id + '"}}')
    if (-not $r.ok) { throw "menu-press $id not ok: $($r.error)" }
}

function Kv([string]$cmd) {
    $r = Send ('{"type":"cmd","name":"' + $cmd + '"}')
    if (-not $r.ok) { throw "$cmd not ok: $($r.error)" }
    $h = @{}
    foreach ($pair in ($r.error -split ' ')) { $k, $v = $pair -split '=', 2; $h[$k] = $v }
    $h
}
function Type-InBubble([string]$text, [switch]$Enter) {
    # Real focus by a click, then the characters straight to the edit box (SendKeys can't type every Arabic letter).
    $b = Kv 'debug-bubble'
    if ($b.open -ne 'true') { throw 'no typing bubble' }
    [Mouse]::Click([int]$b.x + 20, [int]$b.y + [int]([int]$b.h / 2), $false)
    Start-Sleep -Milliseconds 300
    $edit = [IntPtr][long]$b.edit
    foreach ($ch in $text.ToCharArray()) { [Shell]::PostMessage($edit, 0x0102, [IntPtr][int]$ch, [IntPtr]1) | Out-Null }
    Start-Sleep -Milliseconds 200
    if ($Enter) { [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x0D, [IntPtr]1) | Out-Null; Start-Sleep -Milliseconds 300 }
}

function Stage-Menu {
    Step 'start engine' { Start-Engine }
    Step 'right-click opens the menu with the six buttons' {
        Pose-Body 900 450 | Out-Null
        Start-Sleep -Milliseconds 300
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $m = Wait-Menu 'true' 2000 'menu open after a right-click'
        $ids = ($m.buttons | ForEach-Object id) -join ','
        if ($ids -ne 'timer,listen,prayer,note,clipboard,activity,dnd,hide,play') { throw "buttons: $ids" }
        if (-not (State).menuOpen) { throw 'state.menuOpen is false' }
        Start-Sleep -Milliseconds 600
        Screenshot 'menu-open.png' | Out-Null
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'menu closed' | Out-Null
    }
    Step 'Esc closes it' {
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        Wait-Menu 'true' 2000 'menu open after a right-click' | Out-Null
        Start-Sleep -Milliseconds 300
        if ((Menu).open -ne 'true') { throw 'the menu closed by itself' }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-Menu 'false' 1000 'menu closed by Esc' | Out-Null
    }
    Step 'a double-click opens it' {
        $c = Body-Centre
        [Mouse]::DoubleClick($c[0], $c[1])
        Wait-Menu 'true' 2000 'menu open after a double-click' | Out-Null
    }
    Step 'a click outside closes it and nothing else happens' {
        $before = (State).expression
        $c = Body-Centre
        [Mouse]::Click($c[0] - 400, $c[1] + 250, $false)
        Wait-Menu 'false' 1000 'menu closed by a click outside' | Out-Null
        if ((State).expression -ne $before) { throw "expression changed to $((State).expression)" }
    }
    Step 'buttons work by real clicks' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $m = Wait-Menu 'true' 1000 'menu open'
        Start-Sleep -Milliseconds 600
        $b = $m.buttons | Where-Object id -eq 'activity'
        [Mouse]::Click([int]$b.x, [int]$b.y, $false)
        Start-Sleep -Milliseconds 700
        $m = Menu
        if ($m.level -ne 'activity') { throw "level is '$($m.level)' after clicking activity" }
        $b = $m.buttons | Where-Object id -eq 'activity-lively'
        if (-not $b) { throw "no activity-lively button: $(($m.buttons | ForEach-Object id) -join ',')" }
        Screenshot 'menu-activity.png' | Out-Null
        [Mouse]::Click([int]$b.x, [int]$b.y, $false)
        Wait-Menu 'false' 1500 'menu closed after choosing a level' | Out-Null
        if ((Life).level -ne 'Lively') { throw "level is $((Life).level)" }
    }
    Step 'from every corner every button is on screen' {
        $n = 0
        foreach ($corner in @(@($Work.Left, $Work.Top), @(($Work.Right - 80), $Work.Top), @($Work.Left, ($Work.Bottom - 80)), @(($Work.Right - 80), ($Work.Bottom - 80)))) {
            $n++
            Pose-Body $corner[0] $corner[1] | Out-Null
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            $m = Wait-Menu 'true' 1000 "menu open in corner $n"
            foreach ($b in $m.buttons) {
                if ($b.x - $b.r -lt $Work.Left -or $b.y - $b.r -lt $Work.Top -or $b.x + $b.r -gt $Work.Right -or $b.y + $b.r -gt $Work.Bottom) {
                    throw "corner ${n}: $($b.id) at ($($b.x),$($b.y)) is off the work area $Work"
                }
                if ($null -eq $b.lx) { throw "corner ${n}: debug-menu gives no label rectangle for $($b.id)" }
                if ($b.lx -lt $Work.Left -or $b.ly -lt $Work.Top -or $b.lx + $b.lw -gt $Work.Right -or $b.ly + $b.lh -gt $Work.Bottom) {
                    throw "corner ${n}: the label of $($b.id) is off the work area"
                }
            }
            Start-Sleep -Milliseconds 600
            Screenshot "menu-corner-$n.png" | Out-Null
            Send '{"type":"cmd","name":"menu-close"}' | Out-Null
            Wait-Menu 'false' 1000 'menu closed' | Out-Null
        }
        Pose-Body 900 450 | Out-Null
    }
    Step 'hide closes the menu and hides her' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'hide'
        Wait-Menu 'false' 1000 'menu closed after hide' | Out-Null
        Wait-State { param($st) $st.hidden } 2000 'hidden' | Out-Null
        Send '{"type":"cmd","name":"show"}' | Out-Null
        Wait-State { param($st) -not $st.hidden } 3000 'shown again' | Out-Null
        Start-Sleep -Seconds 2
    }
    Step 'play makes her move' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'play'
        Wait-Menu 'false' 1000 'menu closed after play' | Out-Null
        Wait-State { param($st) $st.motion -notin 'Hover', 'EyesFollow' } 1500 'a move' | Out-Null
        Start-Sleep -Seconds 9
    }
    Step 'the menu closes when she hides' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Send '{"type":"cmd","name":"hide","args":{"minutes":"1"}}' | Out-Null
        Wait-Menu 'false' 1000 'menu closed by hiding' | Out-Null
        Send '{"type":"cmd","name":"show"}' | Out-Null
        Start-Sleep -Seconds 2
    }
    Step 'no drag while the menu is open' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        $before = State
        $c = Body-Centre
        & agent-desktop --headed drag --from-xy ('{0},{1}' -f $c[0], $c[1]) --to-xy ('{0},{1}' -f ($c[0] + 200), ($c[1] + 60)) --duration 600 --drop-delay 200 --timeout-ms 8000 | Out-Null
        $after = State
        if ([math]::Abs($after.x - $before.x) -gt 4 -or [math]::Abs($after.y - $before.y) -gt 4) { throw "she moved from ($($before.x),$($before.y)) to ($($after.x),$($after.y))" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'a 25-minute timer runs with its ring' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'timer'
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'timer-5,timer-15,timer-25,timer-45,timer-custom,back') { throw "timer options: $ids" }
        Press 'timer-25'
        $t = Kv 'debug-timer'
        if ($t.running -ne 'true' -or [int]$t.remaining -lt 1490 -or [int]$t.remaining -gt 1500) { throw "timer: running=$($t.running) remaining=$($t.remaining)" }
        Start-Sleep -Milliseconds 800
        Screenshot 'timer-ring.png' | Out-Null
    }
    Step 'the timer menu offers to stop it' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'timer'
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'timer-cancel,back') { throw "running timer options: $ids" }
        Press 'timer-cancel'
        if ((Kv 'debug-timer').running -ne 'false') { throw 'timer still running' }
    }
    Step 'a typed time in Arabic digits' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'timer'
        Press 'timer-custom'
        Start-Sleep -Milliseconds 400
        Type-InBubble ([string][char]0x0662) -Enter
        Start-Sleep -Milliseconds 300
        $t = Kv 'debug-timer'
        if ($t.running -ne 'true' -or [int]$t.remaining -lt 110 -or [int]$t.remaining -gt 120) { throw "typed timer: running=$($t.running) remaining=$($t.remaining)" }
        if ((Kv 'debug-bubble').open -ne 'false') { throw 'the bubble is still open' }
    }
    Step 'when it ends she shows it and a poke dismisses it' {
        Send '{"type":"cmd","name":"timer","args":{"seconds":"3"}}' | Out-Null
        Wait-State { param($st) $st.expression -eq 'timer-done' } 6000 'timer-done face' | Out-Null
        Wait-State { param($st) $st.motion -eq 'Attention' } 2500 'her attention move' | Out-Null
        Start-Sleep -Seconds 4
        if ((Kv 'debug-timer').running -ne 'false') { throw 'timer still running after it ended' }
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $false)
        Wait-State { param($st) $st.expression -notin 'timer-done' } 2500 'timer face dismissed by a poke' | Out-Null
        Start-Sleep -Seconds 2
    }
    Step 'do not disturb for 30 minutes' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'dnd'
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'dnd-30,dnd-60,dnd-120,dnd-on,back') { throw "dnd options: $ids" }
        Press 'dnd-30'
        Wait-State { param($st) $st.expression -eq 'dnd' } 2000 'dnd face' | Out-Null
        if ((Kv 'debug-dnd').on -ne 'true') { throw 'dnd is off' }
        Send '{"type":"cmd","name":"life","args":{"gap":"2"}}' | Out-Null
        $end = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $end) {
            Start-Sleep -Milliseconds 400
            $m = (State).motion
            if ($m -notin 'Hover', 'Breathe') { throw "big move '$m' during do-not-disturb" }
        }
        Send '{"type":"cmd","name":"life","args":{"gap":"600"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'dnd'
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'dnd-off,back') { throw "dnd-on options: $ids" }
        Press 'dnd-off'
        Wait-State { param($st) $st.expression -eq 'idle' } 2000 'dnd face gone' | Out-Null
    }
    Step 'fullscreen while DND is on keeps both' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'dnd'
        Press 'dnd-on'
        Wait-State { param($st) $st.expression -eq 'dnd' } 2000 'dnd face' | Out-Null
        $w = Start-TestWindow 'borderless'
        try { Wait-State { param($st) $st.hidden } 4000 'hidden over fullscreen' | Out-Null }
        finally { $w.Kill() }
        Wait-State { param($st) -not $st.hidden } 5000 'back after fullscreen' | Out-Null
        Wait-State { param($st) $st.expression -eq 'dnd' } 4000 'dnd face after fullscreen (after her hello-back face)' | Out-Null
        if ((Kv 'debug-dnd').on -ne 'true') { throw 'dnd turned off by fullscreen' }
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'dnd'
        Press 'dnd-off'
    }
    Step 'a quick note is saved and shown next time' {
        $notes = Join-Path $env:APPDATA 'Sahar\notes.json'
        Remove-Item $notes -ErrorAction SilentlyContinue
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'note'
        Start-Sleep -Milliseconds 400
        $text = -join ([char[]](0x0627, 0x0634, 0x062A, 0x0631, 0x064A, 0x0020, 0x062E, 0x0628, 0x0632))   # اشتري خبز
        Type-InBubble $text -Enter
        Wait-State { param($st) $st.expression -eq 'notification' } 2000 'the note face' | Out-Null
        if (-not (Test-Path $notes)) { throw 'notes.json not written' }
        $saved = @((Get-Content $notes -Raw -Encoding UTF8) | ConvertFrom-Json)
        if ($saved.Count -ne 1 -or $saved[0].text -ne $text) { throw "notes.json: $(Get-Content $notes -Raw)" }
        Start-Sleep -Seconds 2
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'note'
        Start-Sleep -Milliseconds 400
        $lines = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((Kv 'debug-bubble').lines64)) -split "`n"
        if ($lines[0] -ne $text) { throw "bubble lines: $($lines -join ' | ')" }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        $edit = [IntPtr][long](Kv 'debug-bubble').edit
        [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x1B, [IntPtr]1) | Out-Null
        Start-Sleep -Milliseconds 400
        if ((Kv 'debug-bubble').open -ne 'false') { throw 'Esc did not close the bubble' }
        $count = ((Get-Content $notes -Raw -Encoding UTF8) | ConvertFrom-Json).Count
        if ($count -ne 1) { throw "$count notes after Esc" }
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'note'
        Start-Sleep -Milliseconds 400
        Screenshot 'note-bubble.png' | Out-Null
        $edit = [IntPtr][long](Kv 'debug-bubble').edit
        if ($edit -ne [IntPtr]::Zero) { [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x1B, [IntPtr]1) | Out-Null }
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'timer'
        Press 'timer-custom'
        Start-Sleep -Milliseconds 400
        Screenshot 'timer-bubble.png' | Out-Null
        $edit = [IntPtr][long](Kv 'debug-bubble').edit
        if ($edit -ne [IntPtr]::Zero) { [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x1B, [IntPtr]1) | Out-Null }
    }
    Step 'four notes: the bubble shows the last three, newest first' {
        foreach ($n in 2, 3, 4) {
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Wait-Menu 'true' 1000 'menu open' | Out-Null
            Press 'note'
            Start-Sleep -Milliseconds 300
            Type-InBubble "note $n" -Enter
            Start-Sleep -Milliseconds 300
        }
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'note'
        Start-Sleep -Milliseconds 300
        $lines = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((Kv 'debug-bubble').lines64)) -split "`n"
        if (($lines -join ',') -ne 'note 4,note 3,note 2') { throw "bubble lines: $($lines -join ',')" }
        $edit = [IntPtr][long](Kv 'debug-bubble').edit
        [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x1B, [IntPtr]1) | Out-Null
    }
    Step 'the show plays every step with a caption' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"life","args":{"gap":"2"}}' | Out-Null
        $actions = [int](Life).actions
        $r = Send '{"type":"cmd","name":"showcase","args":{"from":"46"}}'   # the expressions (motions 26–36 made 30+ slow)
        if (-not $r.ok) { throw "showcase not ok: $($r.error)" }
        $seen = @{}
        $deadline = (Get-Date).AddSeconds(35)
        $shot = $false
        do {
            Start-Sleep -Milliseconds 500
            $s = Kv 'debug-showcase'
            if ($s.running -ne 'true') { throw "the show stopped early at $($s.step)" }
            $seen[$s.step] = $s.id
            if (-not $shot -and $seen.Count -ge 2) { Start-Sleep -Milliseconds 400; Screenshot 'showcase.png' | Out-Null; $shot = $true }
        } while ($seen.Count -lt 8 -and (Get-Date) -lt $deadline)
        Write-Host "    steps seen: $(($seen.Values) -join ', ')"
        if ($seen.Count -lt 8) { throw "only $($seen.Count) steps in 35 s" }
        if ([int](Life).actions -ne $actions) { throw 'she did her own things during the show' }
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $false)
        Start-Sleep -Milliseconds 600
        if ((Kv 'debug-showcase').running -ne 'false') { throw 'a click did not stop the show' }
        Send '{"type":"cmd","name":"life","args":{"gap":"600"}}' | Out-Null
        Start-Sleep -Seconds 2
    }
    Step 'clicking a label presses its button' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Press 'timer'
        Start-Sleep -Milliseconds 500
        $b = (Menu).buttons | Where-Object id -eq 'timer-15'
        if ($null -eq $b.lx) { throw 'debug-menu gives no label rectangle' }
        [Mouse]::Click([int]($b.lx + $b.lw / 2), [int]($b.ly + $b.lh / 2), $false)
        Start-Sleep -Milliseconds 500
        $t = Kv 'debug-timer'
        if ($t.running -ne 'true' -or [int]$t.remaining -lt 890) { throw "a click on the label '15 د' did not start the timer (running=$($t.running))" }
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'timer'
        Press 'timer-cancel'
    }
    Step 'a typed time she does not understand is not written to the log' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'timer'
        Press 'timer-custom'
        Start-Sleep -Milliseconds 400
        Type-InBubble 'abc' -Enter
        Start-Sleep -Milliseconds 400
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -match 'not understood[^\r\n]*0061') { throw 'the log holds what was typed' }
        if ($log -notmatch 'typed time not understood \(3 characters\)') { throw 'the log does not say a typed time was not understood' }
        Start-Sleep -Seconds 2
    }
    Step 'a right-click during the show keeps the menu around her' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"showcase","args":{"from":"1"}}' | Out-Null
        Start-Sleep -Seconds 2
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $m = Wait-Menu 'true' 2000 'menu open during the show'
        if ((Kv 'debug-showcase').running -ne 'false') { throw 'the show is still running with the menu open' }
        $c = Body-Centre
        foreach ($b in $m.buttons) {
            $d = [math]::Sqrt([math]::Pow($b.x - $c[0], 2) + [math]::Pow($b.y - $c[1], 2))
            if ($d -gt 560) { throw "button $($b.id) is $([int]$d) px from her" }   # nine buttons: a corner's quarter ring reaches ~525 px
        }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Start-Sleep -Seconds 2
    }
    Step 'stopping the show does not make her jump' {
        Send '{"type":"cmd","name":"showcase","args":{"from":"1"}}' | Out-Null
        Start-Sleep -Seconds 2
        $s0 = Kv 'debug-showcase'
        if ([double]$s0.captionMinX -lt $Work.Left -or [double]$s0.captionMaxX -gt $Work.Right) { throw "caption spans $($s0.captionMinX)..$($s0.captionMaxX), outside $Work" }
        $c = Body-Centre
        $p0 = State
        [Mouse]::Click($c[0], $c[1], $false)
        Start-Sleep -Milliseconds 80
        $p1 = State
        $jump = [math]::Sqrt([math]::Pow($p1.x - $p0.x, 2) + [math]::Pow($p1.y - $p0.y, 2))
        if ($jump -gt 60) { throw "she jumped $([int]$jump) px when the show stopped" }
        Start-Sleep -Seconds 2
    }
    Step 'an interrupted breath does not keep pulsing' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Breathe"}}' | Out-Null
        Start-Sleep -Milliseconds 700
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Start-Sleep -Milliseconds 300
        if ((Info).bodyScale -ne 'free') { throw "body scale after an interrupted breath: '$((Info).bodyScale)'" }
    }
    Step 'the menu and the bubble cost nothing, again and again' {
        # Absolute RAM at rest is checked by the perf stage; on the runner's software renderer, memory after this
        # busy stage is not "at rest", so here the check is what the menu adds and that repeating it does not grow.
        $before = (State).workingSetBytes
        Write-Host ("    working set before: {0:N1} MB (managed heap {1} MB)" -f ($before / 1MB), (Info).gcHeapMB)
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'menu open' | Out-Null
        Start-Sleep -Seconds 3
        $open = (State).workingSetBytes
        Write-Host ("    with the menu open: {0:N1} MB" -f ($open / 1MB))
        if ($open - $before -gt 5MB) { throw ("the open menu adds {0:N1} MB" -f (($open - $before) / 1MB)) }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        $first = (State).workingSetBytes
        foreach ($i in 1..15) {
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Press 'activity'
            Press 'back'
            Press 'note'
            Start-Sleep -Milliseconds 150
            $edit = [IntPtr][long](Kv 'debug-bubble').edit
            if ($edit -ne [IntPtr]::Zero) { [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x1B, [IntPtr]1) | Out-Null }
            Start-Sleep -Milliseconds 150
        }
        Start-Sleep -Seconds 3
        $after = (State).workingSetBytes
        Write-Host ("    after 15 more rounds: {0:N1} MB" -f ($after / 1MB))
        if ($after - $first -gt 5MB) { throw ("15 rounds of menu and bubble grew memory by {0:N1} MB" -f (($after - $first) / 1MB)) }
    }
    Stop-Engine
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Clip {
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint f, IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string n);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint f, UIntPtr n);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    static IntPtr Text(string t) {
        var h = GlobalAlloc(2, (UIntPtr)((t.Length + 1) * 2));
        var p = GlobalLock(h); var b = System.Text.Encoding.Unicode.GetBytes(t + "\0"); Marshal.Copy(b, 0, p, b.Length); GlobalUnlock(h); return h;
    }
    /// Copies text the way a password manager does: marked "don't monitor".
    public static bool CopyPrivate(string t) {
        for (int i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) System.Threading.Thread.Sleep(50);
        EmptyClipboard();
        SetClipboardData(13, Text(t));
        SetClipboardData(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), GlobalAlloc(2, (UIntPtr)4));
        return CloseClipboard();
    }
}
'@

function Clip-Count { [int](Kv 'debug-clipboard').count }

function Stage-Senses {
    $clipJson = Join-Path $env:APPDATA 'Sahar\clipboard.json'
    Remove-Item $clipJson -ErrorAction SilentlyContinue
    Step 'start engine' { Start-Engine }
    Step 'copying text shows the copied face and lands in the history' {
        Set-Clipboard -Value 'salam sahar'
        Wait-State { param($st) $st.expression -eq 'copied' } 2500 'copied face' | Out-Null
        $c = Kv 'debug-clipboard'
        if ([int]$c.count -ne 1) { throw "history count $($c.count)" }
        if ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($c.first)) -ne 'salam sahar') { throw 'first item is wrong' }
    }
    Step 'a password-manager style copy is never kept' {
        Start-Sleep -Seconds 1
        [Clip]::CopyPrivate('secret-123') | Out-Null
        Start-Sleep -Milliseconds 800
        if ((Clip-Count) -ne 1) { throw 'a private copy was kept' }
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -match 'secret-123') { throw 'the private text reached the log' }
    }
    Step 'thirty quick copies keep twenty and one face' {
        foreach ($i in 1..30) { Set-Clipboard -Value "item $i"; Start-Sleep -Milliseconds 60 }
        Start-Sleep -Milliseconds 800
        if ((Clip-Count) -ne 20) { throw "history count $(Clip-Count)" }
        $faces = @(Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') | Where-Object { $_ -match 'clipboard: copied face' }).Count
        if ($faces -gt 2) { throw "$faces copied faces" }
    }
    Step 'picking an item puts it back on the clipboard' {
        Send '{"type":"cmd","name":"clipboard-pick","args":{"index":"3"}}' | Out-Null
        Start-Sleep -Milliseconds 500
        if ((Get-Clipboard) -ne 'item 27') { throw "clipboard holds '$(Get-Clipboard)'" }
        if ((Clip-Count) -ne 20) { throw 'picking added a new item' }
    }
    Step 'the menu has the clipboard button and its list' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'timer,listen,prayer,note,clipboard,activity,dnd,hide,play') { throw "buttons: $ids" }
        Press 'clipboard'
        Start-Sleep -Milliseconds 500
        $l = Kv 'debug-list'
        if ($l.open -ne 'true' -or [int]$l.lines -ne 20) { throw "list open=$($l.open) lines=$($l.lines)" }
        Screenshot 'clipboard-list.png' | Out-Null
        Send '{"type":"cmd","name":"list-close"}' | Out-Null
    }
    Step 'downloads: a finished browser download shows its face' {
        Stop-Engine
        $script:dl = Join-Path $env:TEMP 'sahar-downloads'
        Remove-Item $script:dl -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $script:dl | Out-Null
        Start-Engine @('--downloads', $script:dl)
        Start-Sleep -Seconds 1
        $tmp = Join-Path $script:dl 'a.zip.crdownload'
        [IO.File]::WriteAllBytes($tmp, (New-Object byte[] 4096))
        Rename-Item $tmp 'a.zip'
        Wait-State { param($st) $st.expression -eq 'download' } 3000 'download face' | Out-Null
        Wait-State { param($st) $st.expression -eq 'idle' } 12000 'back to idle' | Out-Null
        Start-Sleep -Seconds 3
    }
    Step 'downloads: a cancelled one shows nothing' {
        $tmp = Join-Path $script:dl 'b.zip.part'
        [IO.File]::WriteAllBytes($tmp, (New-Object byte[] 4096))
        Start-Sleep -Milliseconds 500
        Remove-Item $tmp
        $end = (Get-Date).AddSeconds(4)
        while ((Get-Date) -lt $end) { if ((State).expression -eq 'download') { throw 'a cancelled download showed the face' }; Start-Sleep -Milliseconds 300 }
    }
    Step 'downloads: a file written straight to its name counts once it stops growing' {
        $f = Join-Path $script:dl 'c.pdf'
        $s = [IO.File]::OpenWrite($f)
        foreach ($i in 1..3) { $s.Write((New-Object byte[] 1024), 0, 1024); $s.Flush(); Start-Sleep -Milliseconds 500 }
        $s.Close()
        Wait-State { param($st) $st.expression -eq 'download' } 6000 'download face for a direct write' | Out-Null
        Wait-State { param($st) $st.expression -eq 'idle' } 12000 'back to idle' | Out-Null
    }
    Step 'downloads: a Firefox placeholder beside its .part shows nothing' {
        New-Item -ItemType File -Path (Join-Path $script:dl 'd.mp4') | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $script:dl 'd.mp4.part'), (New-Object byte[] 2048))
        $end = (Get-Date).AddSeconds(5)
        while ((Get-Date) -lt $end) { if ((State).expression -eq 'download') { throw 'the face showed when the download only started' }; Start-Sleep -Milliseconds 300 }
    }
    Step 'a comment that names an app reads right to left' {
        $text = 'صار لك ساعتين على VS Code، كيف ماشي؟'
        $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text))
        Send ('{"type":"cmd","name":"say","args":{"text64":"' + $b64 + '"}}') | Out-Null
        Start-Sleep -Milliseconds 600
        Screenshot 'comment-latin.png' | Out-Null
    }
    Step 'a screenshot key shows her screenshot face' {
        [System.Windows.Forms.SendKeys]::SendWait('{PRTSC}')
        Start-Sleep -Milliseconds 300
        Write-Host "    Print Screen key events seen: $((Kv 'debug-keys').printScreen)"
        Wait-State { param($st) $st.expression -eq 'screenshot' } 3000 'screenshot face' | Out-Null
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -match 'vk=|key ') { throw 'keys reached the log' }
        Wait-State { param($st) $st.expression -eq 'idle' } 12000 'back to idle' | Out-Null
    }
    Step 'a busy CPU for two minutes makes her hot' {
        $r = Send '{"type":"cmd","name":"debug-cpu","args":{"percent":"92","count":"13"}}'
        if (-not $r.ok) { throw "debug-cpu not ok: $($r.error)" }
        Wait-State { param($st) $st.expression -eq 'hot' } 3000 'hot face' | Out-Null
    }
    Step 'camera or microphone on: quiet, only small things' {
        $r = Send '{"type":"cmd","name":"debug-cameramic","args":{"on":"true"}}'
        if (-not $r.ok) { throw "debug-cameramic not ok: $($r.error)" }
        if ((Life).quiet -ne 'true') { throw 'not quiet with the camera on' }
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"life","args":{"gap":"2"}}' | Out-Null
        $end = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $end) {
            Start-Sleep -Milliseconds 400
            $m = (State).motion
            if ($m -notin 'Hover', 'Breathe') { throw "big move '$m' while the camera is on" }
        }
        Send '{"type":"cmd","name":"life","args":{"gap":"600"}}' | Out-Null
        Send '{"type":"cmd","name":"debug-cameramic","args":{"on":"false"}}' | Out-Null
        if ((Life).quiet -ne 'false') { throw 'still quiet after the camera went off' }
    }
    Step 'no comment while you type' {
        $w = Start-TestWindow 'typing'
        try {
            $end = (Get-Date).AddSeconds(2)
            while ((Get-Date) -lt $end) { [System.Windows.Forms.SendKeys]::SendWait('abc '); Start-Sleep -Milliseconds 150 }
            $r = Send '{"type":"cmd","name":"comment-now"}'
            if ($r.ok) { throw 'she commented while you typed' }
            Start-Sleep -Seconds 4   # typing ends
            & agent-desktop --headed mouse-click --xy '430,320' 2>&1 | Out-Null   # the test window in front again
            Start-Sleep -Milliseconds 500
            Write-Host "    app in front: $((Kv 'debug-apptime').app)"
            Send '{"type":"cmd","name":"apptime-skip","args":{"minutes":"125"}}' | Out-Null
            $r = Send '{"type":"cmd","name":"comment-now"}'
            if (-not $r.ok) { throw "no comment after a long session: $($r.error)" }
            $c = Kv 'debug-comment'
            $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($c.text64))
            Write-Host "    comment: $text"
            if ($c.showing -ne 'true' -or $text -notmatch 'ساعتين|ساعة كاملة|صباح') { throw "comment: showing=$($c.showing) '$text'" }
            Start-Sleep -Milliseconds 500
            Screenshot 'comment.png' | Out-Null
            $b = Body-Centre
            [Mouse]::Click($b[0], $b[1], $false)
            Start-Sleep -Milliseconds 400
            $c = Kv 'debug-comment'
            if ($c.showing -ne 'false') { throw 'a click did not close her comment' }
            if ([int]$c.gapMinutes -le 45) { throw "the next comment waits only $($c.gapMinutes) min after being clicked away" }
            $r = Send '{"type":"cmd","name":"comment-now"}'
            if ($r.ok) { throw 'a second comment came right away' }
        }
        finally { $w.Kill() }
    }
    Step 'weather: a city with rain coming shows the rain face' {
        Stop-Engine
        $script:wx = Join-Path $env:TEMP 'sahar-weather'
        Remove-Item $script:wx -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $script:wx | Out-Null
        '{"results":[{"name":"Damascus","latitude":33.51,"longitude":36.29}]}' | Set-Content (Join-Path $script:wx 'geocode.json')
        '{"hourly":{"time":["2026-11-06T20:00","2026-11-06T21:00"],"weather_code":[61,63],"precipitation_probability":[80,90]}}' | Set-Content (Join-Path $script:wx 'forecast.json')
        Start-Engine @('--weather-stub', $script:wx)
        Send '{"type":"cmd","name":"weather-open"}' | Out-Null
        Start-Sleep -Milliseconds 400
        Type-InBubble 'Damascus' -Enter
        Wait-State { param($st) $st.expression -eq 'rain' } 5000 'rain face' | Out-Null
        if ((Kv 'debug-weather').on -ne 'true') { throw 'weather is not on' }
        $json = Get-Content (Join-Path $env:APPDATA 'Sahar\settings.json') -Raw
        if ($json -notmatch '"weatherEnabled"\s*:\s*true') { throw "settings.json: $json" }
    }
    Step 'weather: no network keeps it off and says so' {
        Stop-Engine
        $settings = Join-Path $env:APPDATA 'Sahar\settings.json'
        Remove-Item $settings -ErrorAction SilentlyContinue
        $empty = Join-Path $env:TEMP 'sahar-weather-empty'
        New-Item -ItemType Directory -Force -Path $empty | Out-Null
        Start-Engine @('--weather-stub', $empty)
        Send '{"type":"cmd","name":"weather-open"}' | Out-Null
        Start-Sleep -Milliseconds 400
        Type-InBubble 'Damascus' -Enter
        Start-Sleep -Seconds 2
        $b = Kv 'debug-bubble'
        $title = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b.title64))
        if ($b.open -ne 'true' -or $title -notmatch 'نت') { throw "bubble: open=$($b.open) '$title'" }
        if ((Kv 'debug-weather').on -ne 'false') { throw 'weather turned on without a city' }
        $edit = [IntPtr][long]$b.edit
        [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x1B, [IntPtr]1) | Out-Null
    }
    Step 'clear all empties it' {
        Send '{"type":"cmd","name":"clipboard-clear"}' | Out-Null
        if ((Clip-Count) -ne 0) { throw 'not cleared' }
        if (@((Get-Content $clipJson -Raw) | ConvertFrom-Json).Count -ne 0) { throw 'clipboard.json still holds items' }
    }
    Stop-Engine
}

function Stage-Life {
    $settingsPath = Join-Path $env:APPDATA 'Sahar\settings.json'
    $lifePath = Join-Path $env:APPDATA 'Sahar\life.json'
    Remove-Item $settingsPath, $lifePath -ErrorAction SilentlyContinue
    Step 'start engine' { Start-Engine }
    Step 'she acts on her own (10 actions in 90 s, gap 2 s)' {
        Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
        $r = Send '{"type":"cmd","name":"life","args":{"gap":"2"}}'
        if (-not $r.ok) { throw "life not ok: $($r.error)" }
        $start = [int](Life).actions
        $seen = @{}
        $deadline = (Get-Date).AddSeconds(90)
        do {
            Start-Sleep -Milliseconds 500
            $m = (State).motion
            $seen[$m] = 1 + [int]$seen[$m]
            $done = [int](Life).actions - $start
        } while ($done -lt 10 -and (Get-Date) -lt $deadline)
        Write-Host "    own actions: $done; motions seen: $(($seen.Keys | Sort-Object) -join ', ')"
        Screenshot 'life-acting.png' | Out-Null
        if ($done -lt 10) { throw "only $done own actions in 90 s" }
        Assert-TopLeftInWork (State) 'after her own actions'
    }
    Step 'she does many different things (≥ 6 kinds in 120 s)' {
        Send '{"type":"cmd","name":"life","args":{"gap":"2"}}' | Out-Null
        $kinds = @{}
        $deadline = (Get-Date).AddSeconds(120)
        do {
            Start-Sleep -Milliseconds 500
            $kinds[(Life).last] = 1
        } while ($kinds.Count -lt 7 -and (Get-Date) -lt $deadline)
        $kinds.Remove('None')
        Write-Host "    kinds: $(($kinds.Keys | Sort-Object) -join ', ')"
        if ($kinds.Count -lt 6) { throw "only $($kinds.Count) kinds" }
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        $named = @($kinds.Keys | Where-Object { $log -match "life: $_" })
        if ($named.Count -lt $kinds.Count) { throw "the log names only $($named -join ', ') of her actions" }
    }
    Step 'music from a browser in front: headphones, no big moves' {
        $r = Send '{"type":"cmd","name":"debug-media","args":{"playing":"true","video":"false","browser":"true","front":"true"}}'
        if (-not $r.ok) { throw "debug-media not ok: $($r.error)" }
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Wait-State { param($st) $st.expression -eq 'music' } 4000 'music face' | Out-Null
        $small = @{}
        $end = (Get-Date).AddSeconds(25)   # a Breathe keeps her busy 8 s; two in a row happen
        while ((Get-Date) -lt $end) {
            Start-Sleep -Milliseconds 400
            $m = (State).motion
            if ($m -notin 'Hover', 'Breathe') { throw "big move '$m' while a browser plays in front" }
            $small[(Life).last] = 1
        }
        Write-Host "    small things seen: $(($small.Keys | Sort-Object) -join ', ')"
        if (-not $small.ContainsKey('Moment')) { throw 'no face moment while the browser plays in front: she just sits there' }
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -notmatch 'media: .*browser') { throw 'the log does not say what is playing' }
    }
    Step 'she stops dancing when the music stops' {
        Send '{"type":"cmd","name":"debug-media","args":{"playing":"true","video":"false","browser":"false","front":"false"}}' | Out-Null
        Send '{"type":"cmd","name":"life","args":{"gap":"600","now":"Dance"}}' | Out-Null
        Wait-State { param($st) $st.motion -eq 'Dance' } 3000 'dancing' | Out-Null
        Send '{"type":"cmd","name":"debug-media","args":{"playing":"false","video":"false","browser":"false","front":"false"}}' | Out-Null
        Wait-State { param($st) $st.motion -ne 'Dance' } 4000 'dance ending with the music' | Out-Null
    }
    Step 'a face moment never covers an event face' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Post 'claude.permission' | Out-Null
        Start-Sleep -Milliseconds 500
        $face = (Info).face
        if (-not $face) { throw 'debug-info does not report her face' }
        Send '{"type":"cmd","name":"life","args":{"gap":"600","now":"Moment"}}' | Out-Null
        Start-Sleep -Milliseconds 2500
        if ((State).expression -ne 'claude-permission') { throw "expression is '$((State).expression)'" }
        if ((Life).last -eq 'Moment') { throw 'a moment played over the permission face' }
        if ((Info).face -ne $face) { throw "her face changed from $face to $((Info).face) over the permission face" }
    }
    Step 'her own moves never change an event face' {
        $face = (Info).face
        if (-not $face) { throw 'debug-info does not report her face' }
        foreach ($kind in 'Breathe', 'Curious', 'PlayWithMoon') {
            Send ('{"type":"cmd","name":"motion","args":{"kind":"' + $kind + '"}}') | Out-Null
            Start-Sleep -Milliseconds 1500
            if ((Info).face -ne $face) { throw "$kind changed her face from $face to $((Info).face) over the permission face" }
        }
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
    }
    Step 'grabbing her mid-action teaches her and pauses her' {
        Send '{"type":"cmd","name":"life","args":{"gap":"30"}}' | Out-Null
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"pose","args":{"x":"300","y":"250"}}' | Out-Null
        $r = Send '{"type":"cmd","name":"life","args":{"now":"Wander"}}'
        if (-not $r.ok) { throw "life now not ok: $($r.error)" }
        Wait-State { param($st) $st.motion -eq 'Wander' } 3000 'her own Wander' | Out-Null
        $before = (Life)
        $st = State
        $from = '{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)
        $to = '{0},{1}' -f [int]($st.x + $st.width / 2 + 120), [int]($st.y + $st.height / 2 + 60)
        & agent-desktop --headed drag --from-xy $from --to-xy $to --duration 600 --drop-delay 200 --timeout-ms 8000 | Out-Null
        Start-Sleep -Milliseconds 2000
        $after = (Life)
        $w = Weight $after 'Wander'
        Write-Host "    Wander weight $(Weight $before 'Wander') -> $w; actions $($before.actions) -> $($after.actions)"
        if ($w -ge 1.0) { throw "Wander weight did not drop ($w)" }
        if ([int]$after.actions -ne [int]$before.actions) { throw 'she started another own action right after being grabbed' }
    }
    Step 'she steps away from where you type' {
        Send '{"type":"cmd","name":"life","args":{"gap":"600"}}' | Out-Null
        $script:win = Start-TestWindow 'typing'
        try {
            [System.Windows.Forms.SendKeys]::SendWait('a')
            Start-Sleep -Milliseconds 400
            $spot = (Life).spot
            if (-not $spot -or $spot -eq 'none') { throw "no typing spot ($spot)" }
            $sx, $sy = ($spot -split ',') | ForEach-Object { [double]$_ }
            # The typing window is at (150,120) 560×380: a spot inside it is the text box's real caret.
            if ($sx -lt 150 -or $sx -gt 710 -or $sy -lt 120 -or $sy -gt 500) { throw "typing spot $spot is not the text box caret" }
            $st = State
            # Put her body centre 40 px right of the typing spot (canvas top-left = body centre - offset).
            $bodyCx = $st.x + $st.width / 2; $bodyCy = $st.y + $st.height / 2
            $canvasDx = $bodyCx - [double](Info).canvasX; $canvasDy = $bodyCy - [double](Info).canvasY
            $px = [int]($sx + 40 - $canvasDx); $py = [int]($sy - $canvasDy)
            Send ('{"type":"cmd","name":"pose","args":{"x":"' + $px + '","y":"' + $py + '"}}') | Out-Null
            Send '{"type":"cmd","name":"life","args":{"gap":"600"}}' | Out-Null
            $st = State
            $d0 = [math]::Sqrt([math]::Pow($st.x + $st.width / 2 - $sx, 2) + [math]::Pow($st.y + $st.height / 2 - $sy, 2))
            Write-Host "    typing spot $spot, her distance before typing $([int]$d0) px"
            # Type for 3 s.
            $end = (Get-Date).AddSeconds(3)
            while ((Get-Date) -lt $end) { [System.Windows.Forms.SendKeys]::SendWait('salam '); Start-Sleep -Milliseconds 150 }
            if ((Life).typing -ne 'true') { throw 'typing not detected' }
            $far = $false
            $deadline = (Get-Date).AddSeconds(12)   # a long wander away takes up to 8 s after the first tick
            do {
                [System.Windows.Forms.SendKeys]::SendWait('x'); Start-Sleep -Milliseconds 300
                $st = State
                $d = [math]::Sqrt([math]::Pow($st.x + $st.width / 2 - $sx, 2) + [math]::Pow($st.y + $st.height / 2 - $sy, 2))
                if ($st.motion -notin 'Wander', 'Hover') { throw "motion '$($st.motion)' while typing" }
                if ($d -gt 120 -and $st.motion -eq 'Hover') { $far = $true }
            } while (-not $far -and (Get-Date) -lt $deadline)
            Screenshot 'life-typing.png' | Out-Null
            Write-Host "    her distance after typing $([int]$d) px"
            if (-not $far) { throw "still $([int]$d) px from the typing spot" }
        }
        finally { Stop-Process -Id $script:win.Id -Force -ErrorAction SilentlyContinue }
    }
    Step 'she stays put when you poke, hover over or drop her (no-caret app in front)' {
        Send '{"type":"cmd","name":"life","args":{"gap":"600"}}' | Out-Null
        $script:win = Start-TestWindow 'maximized'
        try {
            Send '{"type":"cmd","name":"pose","args":{"x":"300","y":"250"}}' | Out-Null
            function Centre { $st = State; [pscustomobject]@{ x = $st.x + $st.width / 2; y = $st.y + $st.height / 2; motion = $st.motion } }
            function Assert-Still($before, [string]$what) {
                for ($i = 0; $i -lt 10; $i++) {
                    Start-Sleep -Milliseconds 500
                    $c = Centre
                    if ([math]::Abs($c.x - $before.x) -gt 4 -or [math]::Abs($c.y - $before.y) -gt 4 -or $c.motion -eq 'Wander') {
                        throw "${what}: she moved from ($([int]$before.x),$([int]$before.y)) to ($([int]$c.x),$([int]$c.y)), motion $($c.motion)"
                    }
                }
            }
            $c = Centre
            & agent-desktop --headed mouse-click --xy ('{0},{1}' -f [int]$c.x, [int]$c.y) | Out-Null
            Assert-Still $c 'after a poke'
            & agent-desktop --headed mouse-move --xy ('{0},{1}' -f [int]$c.x, [int]($c.y - 10)) | Out-Null
            Assert-Still $c 'with the cursor resting on her'
            & agent-desktop --headed drag --from-xy ('{0},{1}' -f [int]$c.x, [int]$c.y) --to-xy ('{0},{1}' -f [int]($c.x + 150), [int]($c.y + 40)) --duration 600 --drop-delay 300 --timeout-ms 8000 | Out-Null
            Start-Sleep -Milliseconds 800
            Assert-Still (Centre) 'after being dropped'
        }
        finally { Stop-Process -Id $script:win.Id -Force -ErrorAction SilentlyContinue }
    }
    Step 'she leaves a window edge when that window is maximized' {
        Send '{"type":"cmd","name":"life","args":{"gap":"600"}}' | Out-Null
        $script:win = Start-TestWindow 'typing'
        try {
            $r = Send '{"type":"cmd","name":"motion","args":{"kind":"PerchWindow"}}'
            Start-Sleep -Milliseconds 2000
            if ((State).motion -ne 'PerchWindow') { throw "she did not perch ($((State).motion))" }
            $script:win.Refresh()
            [Shell]::ShowWindow($script:win.MainWindowHandle, 3) | Out-Null   # SW_MAXIMIZE
            Wait-State { param($st) $st.motion -ne 'PerchWindow' } 2500 'leaving the maximized window' | Out-Null
        }
        finally { Stop-Process -Id $script:win.Id -Force -ErrorAction SilentlyContinue }
    }
    Step 'her own moves now include living ones (life picks them)' {
        foreach ($act in 'WalkTaskbar', 'Stretch', 'MoonToss', 'ChaseButterfly') {
            Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
            Send '{"type":"cmd","name":"pose","args":{"x":"700","y":"400"}}' | Out-Null
            Send ('{"type":"cmd","name":"life","args":{"gap":"600","now":"' + $act + '"}}') | Out-Null
            Wait-State { param($st) $st.motion -eq $act } 4000 "life playing $act" | Out-Null
            Wait-State { param($st) $st.motion -eq 'Hover' } 15000 "$act ending" | Out-Null
        }
    }
    Step 'when you start typing she sometimes types along beside your window' {
        $w = Start-TestWindow 'typing'
        try {
            Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
            Send '{"type":"cmd","name":"pose","args":{"x":"1100","y":"300"}}' | Out-Null
            Send '{"type":"cmd","name":"typealong-next"}' | Out-Null
            & agent-desktop --headed mouse-click --xy '430,320' 2>&1 | Out-Null
            Start-Sleep -Milliseconds 300
            $seen = $false
            $end = (Get-Date).AddSeconds(4)
            while ((Get-Date) -lt $end) {
                [System.Windows.Forms.SendKeys]::SendWait('abc ')
                Start-Sleep -Milliseconds 150
                if ((State).motion -eq 'TypeAlong') { $seen = $true }
            }
            if (-not $seen) { throw 'she did not type along' }
            Screenshot 'type-along.png' | Out-Null
            Wait-State { param($st) $st.motion -eq 'Hover' } 5000 'typing along ends when you stop' | Out-Null
        }
        finally { Stop-Process -Id $w.Id -Force -ErrorAction SilentlyContinue }
    }
    Step 'what she learned is saved when Windows shuts down' {
        Remove-Item $lifePath -ErrorAction SilentlyContinue
        if (-not [Shell]::EndSession()) { throw 'tray window not found' }
        $deadline = (Get-Date).AddSeconds(3)
        while (-not (Test-Path $lifePath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path $lifePath)) { throw 'life.json not written on WM_ENDSESSION' }
    }
    Step 'activity level is saved and survives a restart' {
        $r = Send '{"type":"cmd","name":"set-activity","args":{"level":"Lively"}}'
        if (-not $r.ok) { throw "set-activity not ok: $($r.error)" }
        if (-not (Test-Path $settingsPath)) { throw 'settings.json not written' }
        $json = Get-Content $settingsPath -Raw
        if ($json -notmatch '"activity"\s*:\s*"lively"') { throw "settings.json: $json" }
        if ((Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw) -notmatch 'activity: Lively') { throw 'the log does not say the activity level changed' }
        Stop-Engine
        if (-not (Test-Path $lifePath)) { throw 'life.json not written on quit' }
        Start-Engine
        $level = (Life).level
        if ($level -ne 'Lively') { throw "level after restart is '$level'" }
    }
    Stop-Engine
}

function Text64([string]$b64) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64)) }

function Stage-Prayer {
    $settings = Join-Path $env:APPDATA 'Sahar\settings.json'
    Step 'the clean version removes old data once' {
        Stop-Engine
        $roaming = Join-Path $env:APPDATA 'Sahar'
        New-Item -ItemType Directory -Force -Path $roaming | Out-Null
        Remove-Item (Join-Path $roaming 'data-version') -ErrorAction SilentlyContinue
        '["old note"]' | Set-Content (Join-Path $roaming 'notes.json')
        'x' | Set-Content (Join-Path $roaming 'clip-old.bmp')
        Start-Engine
        if (Test-Path (Join-Path $roaming 'notes.json')) { throw 'old notes survived the clean start' }
        if (Test-Path (Join-Path $roaming 'clip-old.bmp')) { throw 'an old clipboard image survived' }
        if ((Get-Content (Join-Path $roaming 'data-version') -Raw).Trim() -ne '2') { throw 'no data-version marker' }
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -notmatch 'clean start: old data removed') { throw 'the clean start is not in the log' }
        Stop-Engine
        '["kept"]' | Set-Content (Join-Path $roaming 'notes.json')
        Start-Engine
        if (-not (Test-Path (Join-Path $roaming 'notes.json'))) { throw 'the second start wiped again' }
    }
    Step 'start engine with a stubbed city search' {
        Stop-Engine
        Remove-Item $settings -ErrorAction SilentlyContinue
        $script:px = Join-Path $env:TEMP 'sahar-prayer'
        Remove-Item $script:px -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $script:px | Out-Null
        '{"results":[{"name":"Damascus","latitude":33.51,"longitude":36.29}]}' | Set-Content (Join-Path $script:px 'geocode.json')
        Start-Engine @('--weather-stub', $script:px)
        Pose-Body 900 450 | Out-Null
    }
    Step 'a test machine never gets her own comments (her morning hello hid a click in the motion stage)' {
        # Her clock moved to 06:30, when she says good morning; on a test machine only comment-now may make her talk.
        $now = Get-Date
        $m = [int]((([TimeSpan]'06:30') - $now.TimeOfDay).TotalMinutes)
        if ($m -lt 0) { $m += 1440 }
        Send "{`"type`":`"cmd`",`"name`":`"apptime-skip`",`"args`":{`"minutes`":`"$m`"}}" | Out-Null
        Start-Sleep -Seconds 65   # her own check runs every minute
        $c = Kv 'debug-comment'
        if ($c.showing -ne 'false') { throw "she commented by herself: '$(Text64 $c.text64)'" }
    }
    Step 'the first press asks the city, then shows the next prayer and six times' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Send '{"type":"cmd","name":"menu-press","args":{"id":"prayer"}}' | Out-Null
        Start-Sleep -Milliseconds 500
        $b = Kv 'debug-bubble'
        if ($b.open -ne 'true' -or (Text64 $b.title64) -notmatch 'مدينت') { throw "no city question (open=$($b.open))" }
        Type-InBubble 'Damascus' -Enter
        $deadline = (Get-Date).AddSeconds(5)
        do { Start-Sleep -Milliseconds 200; $l = Kv 'debug-list' } while ($l.open -ne 'true' -and (Get-Date) -lt $deadline)
        if ($l.open -ne 'true') { throw 'the prayer list did not open' }
        $lines = (Text64 $l.lines64) -split "`n"
        if ($lines.Count -ne 8) { throw "list lines: $($lines.Count)" }
        if ($lines[0] -notmatch '^الجاية: ') { throw "first line: $($lines[0])" }
        foreach ($name in 'الفجر', 'الشروق', 'الظهر', 'العصر', 'المغرب', 'العشاء') {
            if (-not ($lines | Where-Object { $_ -match "^$name — \d\d:\d\d$" })) { throw "no time line for $name" }
        }
        if ($lines[7] -notmatch 'طريقة الحساب: أم القرى') { throw "method line: $($lines[7])" }
        if ((Text64 $l.button64) -notmatch 'Damascus') { throw "city button: $(Text64 $l.button64)" }
        if ((Get-Content $settings -Raw) -notmatch '"prayerCity"\s*:\s*"Damascus"') { throw 'city not saved' }
    }
    Step 'the method line opens the methods and a pick is saved' {
        Send '{"type":"cmd","name":"list-pick","args":{"index":"7"}}' | Out-Null
        Start-Sleep -Milliseconds 300
        $l = Kv 'debug-list'
        if ($l.open -ne 'true' -or [int]$l.lines -ne 5) { throw "methods list: open=$($l.open) lines=$($l.lines)" }
        Send '{"type":"cmd","name":"list-pick","args":{"index":"1"}}' | Out-Null
        Start-Sleep -Milliseconds 300
        if ((Get-Content $settings -Raw) -notmatch '"prayerMethod"\s*:\s*"muslimWorldLeague"') { throw "method not saved: $(Get-Content $settings -Raw)" }
        $l = Kv 'debug-list'
        if ($l.open -ne 'true' -or (Text64 $l.lines64) -notmatch 'رابطة العالم الإسلامي') { throw 'the prayer list did not come back with the new method' }
        Send '{"type":"cmd","name":"list-close"}' | Out-Null
    }
    Step 'ten minutes before Asr: the reminder face and words' {
        $p = Kv 'debug-prayer'
        $script:asr = [DateTimeOffset]::Parse($p.asr, [Globalization.CultureInfo]::InvariantCulture)
        $at = $script:asr.AddMinutes(-9.5).ToString('o')
        Send "{`"type`":`"cmd`",`"name`":`"prayer-at`",`"args`":{`"at`":`"$at`"}}" | Out-Null
        Wait-State { param($st) $st.expression -eq 'reminder' } 5000 'reminder face' | Out-Null
        $c = Kv 'debug-comment'
        if ($c.showing -ne 'true' -or (Text64 $c.text64) -ne 'بعد 10 دقايق العصر') { throw "reminder words: '$(Text64 $c.text64)'" }
        if ((Kv 'debug-prayer').next -ne 'Asr') { throw 'next prayer is not Asr' }
    }
    Step 'at Asr: quiet for the adhan, small things only' {
        $at = $script:asr.AddSeconds(5).ToString('o')
        Send "{`"type`":`"cmd`",`"name`":`"prayer-at`",`"args`":{`"at`":`"$at`"}}" | Out-Null
        Start-Sleep -Milliseconds 500
        if ((Kv 'debug-prayer').quiet -ne 'true') { throw 'no adhan quiet' }
        if ((Kv 'debug-life').quiet -ne 'true') { throw 'life is not quiet' }
        if ((State).expression -eq 'reminder') { throw 'the reminder face stayed into the adhan' }
        $r = Send '{"type":"cmd","name":"comment-now"}'
        if ($r.ok) { throw 'a comment during the adhan' }
    }
    Step 'six minutes later the quiet ends' {
        $at = $script:asr.AddMinutes(6).ToString('o')
        Send "{`"type`":`"cmd`",`"name`":`"prayer-at`",`"args`":{`"at`":`"$at`"}}" | Out-Null
        Start-Sleep -Milliseconds 500
        if ((Kv 'debug-prayer').quiet -ne 'false') { throw 'still quiet' }
        if ((Kv 'debug-life').quiet -ne 'false') { throw 'life still quiet' }
    }
    Step 'listening: music and Quran, then the music controls and what is playing' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Send '{"type":"cmd","name":"menu-press","args":{"id":"listen"}}' | Out-Null
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'music,quran,back') { throw "listen ring: $ids" }
        Send '{"type":"cmd","name":"menu-press","args":{"id":"music"}}' | Out-Null
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'music-prev,music-toggle,music-next,back') { throw "music ring: $ids" }
        if ((Kv 'debug-music').session -ne 'false') { throw 'a media session on the test machine' }
        $deadline = (Get-Date).AddSeconds(3)
        do { Start-Sleep -Milliseconds 200; $c = Kv 'debug-comment' } while ($c.showing -ne 'true' -and (Get-Date) -lt $deadline)
        if ((Text64 $c.text64) -ne 'ما في شي عم يشتغل') { throw "music bubble: '$(Text64 $c.text64)'" }
        $gap = (Kv 'debug-apptime').gapMinutes
        Send '{"type":"cmd","name":"menu-press","args":{"id":"music-toggle"}}' | Out-Null
        Start-Sleep -Milliseconds 300
        if ((Menu).open -ne 'false') { throw 'the menu stayed open after a control' }
        if (-not (State)) { throw 'no state after a control with nothing playing' }
        # Her tool lines are not comments: letting them time out never makes her comment less.
        Start-Sleep -Seconds 9
        if ((Kv 'debug-comment').showing -ne 'false') { throw 'the music bubble did not go away' }
        if ((Kv 'debug-apptime').gapMinutes -ne $gap) { throw "the comment gap grew from $gap to $((Kv 'debug-apptime').gapMinutes) minutes" }
    }
    Step 'Quran: no network gives a gentle message and no stuck playing' {
        Stop-Engine
        Start-Engine @('--weather-stub', $script:px, '--quran-base', 'http://127.0.0.1:9/')
        Send '{"type":"cmd","name":"quran-play","args":{"surah":"1","reciter":"ar.alafasy"}}' | Out-Null
        # A refused connection or a hang: either way she gives up within 15 s, once.
        $deadline = (Get-Date).AddSeconds(25)
        do { Start-Sleep -Milliseconds 300; $q = Kv 'debug-quran' } while ($q.state -ne 'failed' -and (Get-Date) -lt $deadline)
        if ($q.playing -ne 'false' -or $q.state -ne 'failed') { throw "quran: playing=$($q.playing) state=$($q.state)" }
        $c = Kv 'debug-comment'
        if ((Text64 $c.text64) -ne 'ما قدرت شغّل التلاوة هلق') { throw "message: '$(Text64 $c.text64)'" }
        if ((Kv 'debug-life').quiet -ne 'false') { throw 'still quiet after the failure' }
        if ((State).expression -eq 'quran') { throw 'the quran face stayed after the failure' }
    }
    # CI's Windows Server has no MP3 decoder (SourceNotSupported), so these steps use a stand-in player ("fake" seconds);
    # real playback is a manual check (README-test-build.txt).
    Step 'Quran: playing pauses the dance, is quiet and wears its face' {
        Stop-Engine
        Start-Engine @('--weather-stub', $script:px)
        Pose-Body 900 450 | Out-Null
        Send '{"type":"cmd","name":"debug-media","args":{"playing":"true","video":"false","browser":"false","front":"false"}}' | Out-Null
        Send '{"type":"cmd","name":"life","args":{"gap":"600","now":"Dance"}}' | Out-Null
        Wait-State { param($st) $st.motion -eq 'Dance' } 3000 'dancing to the music' | Out-Null
        Send '{"type":"cmd","name":"quran-play","args":{"surah":"112","reciter":"ar.alafasy","fake":"30"}}' | Out-Null
        $deadline = (Get-Date).AddSeconds(8)
        do { Start-Sleep -Milliseconds 300; $q = Kv 'debug-quran' } while ($q.state -ne 'playing' -and (Get-Date) -lt $deadline)
        if ($q.playing -ne 'true' -or $q.state -ne 'playing' -or $q.surah -ne '112') { throw "quran: playing=$($q.playing) state=$($q.state) surah=$($q.surah)" }
        Wait-State { param($st) $st.motion -ne 'Dance' } 4000 'the dance ending for the Quran' | Out-Null
        Wait-State { param($st) $st.expression -eq 'quran' } 2000 'the quran face' | Out-Null
        if ((Kv 'debug-life').quiet -ne 'true') { throw 'life is not quiet during the Quran' }
        Start-Sleep -Seconds 3
        if ((State).motion -eq 'Dance') { throw 'she danced during the Quran' }
        # Listening without touching the PC is not being away: no sleepy face, no nap in the corner.
        # (The music stops first: a playing song outranks "away" in her brain and would hide the case.)
        Send '{"type":"cmd","name":"debug-media","args":{"playing":"false","video":"false","browser":"false","front":"false"}}' | Out-Null
        Start-Sleep -Milliseconds 500
        Post 'user.away' | Out-Null
        Start-Sleep -Seconds 3
        $st = State
        if ($st.expression -ne 'quran') { throw "face during the Quran while 'away': $($st.expression)" }
        if ($st.motion -in 'SleepInCorner', 'NapOnIcon') { throw "she went to nap during the Quran ($($st.motion))" }
        Post 'user.back' | Out-Null
    }
    Step 'Quran: the listen ring offers stop, and stop restores her' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Send '{"type":"cmd","name":"menu-press","args":{"id":"listen"}}' | Out-Null
        $ids = ((Menu).buttons | ForEach-Object id) -join ','
        if ($ids -ne 'quran-stop,music,back') { throw "listen ring while playing: $ids" }
        Send '{"type":"cmd","name":"menu-press","args":{"id":"quran-stop"}}' | Out-Null
        Start-Sleep -Milliseconds 500
        if ((Kv 'debug-quran').playing -ne 'false') { throw 'still playing after stop' }
        if ((Kv 'debug-life').quiet -ne 'false') { throw 'still quiet after stop' }
        if ((State).expression -eq 'quran') { throw 'the quran face stayed after stop' }
        Send '{"type":"cmd","name":"debug-media","args":{"playing":"false","video":"false","browser":"false","front":"false"}}' | Out-Null
    }
    Step 'Quran: the surah list puts the last one first, and the reciter can change' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Send '{"type":"cmd","name":"menu-press","args":{"id":"listen"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-press","args":{"id":"quran"}}' | Out-Null
        Start-Sleep -Milliseconds 400
        $l = Kv 'debug-list'
        $lines = (Text64 $l.lines64) -split "`n"
        if ($l.open -ne 'true' -or $lines.Count -ne 115) { throw "surah list: open=$($l.open) lines=$($lines.Count)" }
        if ($lines[0] -notmatch 'الإخلاص' -or $lines[1] -notmatch 'الفاتحة' -or $lines[114] -notmatch 'الناس') { throw "list order: $($lines[0]) / $($lines[1]) / $($lines[114])" }
        if ((Text64 $l.button64) -ne 'القارئ: مشاري العفاسي') { throw "reciter button: $(Text64 $l.button64)" }
        Send '{"type":"cmd","name":"list-button"}' | Out-Null
        Start-Sleep -Milliseconds 400
        $l = Kv 'debug-list'
        if ([int]$l.lines -ne 6) { throw "reciter list lines: $($l.lines)" }
        Send '{"type":"cmd","name":"list-pick","args":{"index":"2"}}' | Out-Null
        Start-Sleep -Milliseconds 400
        if ((Get-Content $settings -Raw) -notmatch '"quranReciter"\s*:\s*"ar.husary"') { throw 'reciter not saved' }
        $l = Kv 'debug-list'
        if ((Text64 $l.button64) -notmatch 'الحصري') { throw "reciter button after the pick: $(Text64 $l.button64)" }
        Send '{"type":"cmd","name":"list-close"}' | Out-Null
    }
    Step 'Quran: the end of the surah brings her back' {
        Send '{"type":"cmd","name":"quran-play","args":{"surah":"113","reciter":"ar.husary","fake":"2"}}' | Out-Null
        $deadline = (Get-Date).AddSeconds(12)
        do { Start-Sleep -Milliseconds 300; $q = Kv 'debug-quran' } while ($q.state -ne 'ended' -and (Get-Date) -lt $deadline)
        if ($q.playing -ne 'false' -or $q.state -ne 'ended') { throw "quran: playing=$($q.playing) state=$($q.state)" }
        if ((Kv 'debug-life').quiet -ne 'false') { throw 'still quiet after the surah ended' }
    }
    Step 'the first hello asks your name and whether you are a man or a woman; her words follow' {
        Send '{"type":"cmd","name":"hello-new"}' | Out-Null
        Start-Sleep -Milliseconds 500
        $b = Kv 'debug-bubble'
        if ($b.open -ne 'true' -or (Text64 $b.title64) -notmatch 'شو بدك ناديك') { throw "no name question: '$(Text64 $b.title64)'" }
        Type-InBubble 'Sami' -Enter
        $deadline = (Get-Date).AddSeconds(4)
        do { Start-Sleep -Milliseconds 200; $l = Kv 'debug-list' } while ($l.open -ne 'true' -and (Get-Date) -lt $deadline)
        if ($l.open -ne 'true' -or [int]$l.lines -ne 2 -or (Text64 $l.lines64) -notmatch 'بنت') { throw "no man/woman question (open=$($l.open))" }
        Send '{"type":"cmd","name":"list-pick","args":{"index":"1"}}' | Out-Null
        Start-Sleep -Milliseconds 500
        $raw = Get-Content $settings -Raw
        if ($raw -notmatch '"userGender"\s*:\s*"female"' -or $raw -notmatch '"userName"\s*:\s*"Sami"' -or $raw -notmatch '"helloDone"\s*:\s*true') { throw "not saved: $raw" }
        if ((Text64 (Kv 'debug-comment').text64) -ne 'تشرفنا يا Sami 💜') { throw "greeting: '$(Text64 (Kv 'debug-comment').text64)'" }
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'note'
        Start-Sleep -Milliseconds 400
        $title = Text64 (Kv 'debug-bubble').title64
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        if ($title -ne 'اكتبي ملاحظة…') { throw "the note bubble to a woman: '$title'" }
        # Only her own words change: what you copied stays exactly as you copied it.
        $copied = -join ([char[]](0x0631, 0x062C, 0x0639, 0x062A, 0x0020, 0x0645, 0x0646, 0x0020, 0x0627, 0x0644, 0x0634, 0x063A, 0x0644))   # رجعت من الشغل
        Set-Clipboard -Value $copied
        Start-Sleep -Milliseconds 800
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'clipboard'
        Start-Sleep -Milliseconds 400
        $l = Kv 'debug-list'
        $first = ((Text64 $l.lines64) -split "`n")[0]
        $button = Text64 $l.button64
        Send '{"type":"cmd","name":"list-close"}' | Out-Null
        if ($first -ne "📄 $copied") { throw "the copied text changed in the list: '$first'" }
        if ($button -ne 'امسحي الكل') { throw "her button to a woman: '$button'" }
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -match 'Sami') { throw 'the name is in engine.log' }
    }
    Step 'the tray can ask who you are again' {
        Send '{"type":"cmd","name":"tray-pick","args":{"item":"who"}}' | Out-Null
        Start-Sleep -Milliseconds 500
        $b = Kv 'debug-bubble'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        if ($b.open -ne 'true' -or (Text64 $b.title64) -notmatch 'شو بدك ناديك') { throw 'the tray did not ask again' }
    }
    Step 'the city never reaches the log' {
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -match 'Damascus') { throw 'the city name is in engine.log' }
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
            'events' { Stage-Events }
            'perf' { Stage-Perf }
            'life' { Stage-Life }
            'menu' { Stage-Menu }
            'senses' { Stage-Senses }
            'prayer' { Stage-Prayer }
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
