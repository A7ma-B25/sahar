# UI tests for the engine on a real Windows desktop (GitHub windows-latest).
# Stages build on each other; later plans add stages after "smoke".
param(
    [ValidateSet('smoke', 'idle', 'gallery', 'motion', 'events', 'perf', 'life')][string[]]$Stage = @('smoke'),
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
    & agent-desktop --headed mouse-click --xy '120,300' 2>&1 | Out-Null
    Start-Sleep -Milliseconds 300
    $p
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Shell {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    public static void TaskbarCreated() { PostMessage((IntPtr)0xFFFF, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero); }
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
            $st = State
            $ox = $st.x - (Send '{"type":"cmd","name":"state"}').state.x   # 0, keeps $st fresh
            # Put her body centre 40 px right of the typing spot (canvas top-left = body centre - offset).
            $bodyCx = $st.x + $st.width / 2; $bodyCy = $st.y + $st.height / 2
            $pose = Send '{"type":"cmd","name":"state"}'
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
            $deadline = (Get-Date).AddSeconds(8)
            do {
                [System.Windows.Forms.SendKeys]::SendWait('x'); Start-Sleep -Milliseconds 300
                $st = State
                $d = [math]::Sqrt([math]::Pow($st.x + $st.width / 2 - $sx, 2) + [math]::Pow($st.y + $st.height / 2 - $sy, 2))
                if ($st.motion -notin 'Wander', 'Hover') { throw "motion '$($st.motion)' while typing" }
                if ($d -gt 150 -and $st.motion -eq 'Hover') { $far = $true }
            } while (-not $far -and (Get-Date) -lt $deadline)
            Screenshot 'life-typing.png' | Out-Null
            Write-Host "    her distance after typing $([int]$d) px"
            if (-not $far) { throw "still $([int]$d) px from the typing spot" }
        }
        finally { Stop-Process -Id $script:win.Id -Force -ErrorAction SilentlyContinue }
    }
    Step 'activity level is saved and survives a restart' {
        $r = Send '{"type":"cmd","name":"set-activity","args":{"level":"Lively"}}'
        if (-not $r.ok) { throw "set-activity not ok: $($r.error)" }
        if (-not (Test-Path $settingsPath)) { throw 'settings.json not written' }
        $json = Get-Content $settingsPath -Raw
        if ($json -notmatch '"activity"\s*:\s*"lively"') { throw "settings.json: $json" }
        Stop-Engine
        if (-not (Test-Path $lifePath)) { throw 'life.json not written on quit' }
        Start-Engine
        $level = (Life).level
        if ($level -ne 'Lively') { throw "level after restart is '$level'" }
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
