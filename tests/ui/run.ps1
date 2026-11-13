# UI tests for the engine on a real Windows desktop (GitHub windows-latest).
# Stages build on each other; later plans add stages after "smoke".
param(
    [ValidateSet('smoke', 'idle', 'gallery', 'motion', 'events', 'perf', 'life', 'menu', 'senses', 'prayer', 'games', 'shell', 'face', 'install')][string[]]$Stage = @('smoke'),
    [string]$Exe = "$PSScriptRoot\..\..\artifacts\engine\Sahar.Engine.exe",
    [string]$Out = "$PSScriptRoot\..\..\artifacts\ui",
    [string]$Setup = "$PSScriptRoot\..\..\artifacts\installer"
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

# Expression keys 1–29 in catalog order (src/Sahar.Core/assets/expressions.json).
$ExpressionKeys = @('music', 'claude-working', 'claude-permission', 'claude-done', 'timer-done', 'celebrate', 'rain',
    'battery-low', 'charging', 'download', 'sleep', 'back', 'notification', 'dnd', 'offline', 'online', 'screenshot',
    'copied', 'reminder', 'error', 'hot', 'poked', 'dragged', 'break', 'late-night', 'quran', 'dizzy', 'sick', 'angry')

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

    Step 'a run that ends without exit is noticed at the next start' {
        $log = Join-Path $env:LOCALAPPDATA 'Sahar\engine.log'
        Start-Engine
        Start-Sleep -Milliseconds 300
        $lines = Get-Content $log; $i = ($lines | Select-String '^\S+ \S+ start ' | Select-Object -Last 1).LineNumber
        if (($lines[($i - 1)..($i + 2)] -join "`n") -match 'previous run ended without exit') { throw 'a clean quit was reported as a crash' }
        $script:engine.Kill(); $script:engine.WaitForExit(3000) | Out-Null
        Start-Engine
        Start-Sleep -Milliseconds 300
        $tail = (Get-Content $log)[-15..-1] -join "`n"
        if ($tail -notmatch 'previous run ended without exit \(last line \d{4}-\d{2}-\d{2} [0-9:.]+\)') { throw "no crash note after a kill:`n$tail" }
        Stop-Engine
    }
    Step 'Rubik is loaded' {
        Start-Engine
        $f = Kv 'debug-font'
        Stop-Engine
        if ($f.rubik -ne 'true' -or $f.families -ne '1' -or $f.gdi -ne 'true') { throw "fonts: rubik=$($f.rubik) families=$($f.families) gdi=$($f.gdi)" }
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
    Step 'the night sky shows inside her (and goes when turned off)' {
        function Sum3([string]$shot, [int]$x, [int]$y) {
            $t = 0; $blue = 0; $red = 0
            foreach ($dx in -1..1) { foreach ($dy in -1..1) { $p = Pixel $shot ($x + $dx) ($y + $dy); $t += $p.R + $p.G + $p.B; $blue += $p.B; $red += $p.R } }
            @{ Sum = [int]($t / 9); Blue = $blue / 9; Red = $red / 9 }
        }
        $st = $script:state
        # Rig point (96,190): well inside the wash and her lower body, clear of the hem's dip at the centre and of the
        # inner stars, so a float or bob of a few pixels never moves the sample off her.
        $lx = [int]($st.x + $st.width * 0.35); $ly = [int]($st.y + $st.height * 0.857)
        $ux = [int]($st.x + $st.width / 2); $uy = [int]($st.y + $st.height * 0.25)
        $shot = Screenshot 'idle-night.png'
        $low = Sum3 $shot $lx $ly; $up = Sum3 $shot $ux $uy
        Write-Host "    lower body $($low.Sum) (R $([int]$low.Red) B $([int]$low.Blue)); upper body $($up.Sum)"
        if ($low.Sum -gt $up.Sum - 70) { throw "no night inside her: lower $($low.Sum) vs upper $($up.Sum)" }
        if ($low.Blue -lt $low.Red) { throw 'the night is not lavender (Calm)' }
        $settings = Join-Path $env:APPDATA 'Sahar\settings.json'
        $saved = if (Test-Path $settings) { Get-Content $settings -Raw } else { $null }
        try {
            Stop-Engine
            New-Item -ItemType Directory -Force -Path (Split-Path $settings) | Out-Null
            Set-Content $settings '{"innerStars": false}'
            Start-Engine
            Send '{"type":"cmd","name":"pose","args":{"x":"500","y":"400"}}' | Out-Null
            Start-Sleep -Milliseconds 1500
            $off = Sum3 (Screenshot 'idle-night-off.png') $lx $ly
            Write-Host "    lower body without the sky $($off.Sum)"
            if ($off.Sum -lt $low.Sum + 25) { throw "turning the stars off did not remove the night: $($off.Sum) vs $($low.Sum)" }
        }
        finally {
            Stop-Engine
            if ($null -ne $saved) { Set-Content $settings $saved -NoNewline } else { Remove-Item $settings -ErrorAction SilentlyContinue }
            Start-Engine
            Send '{"type":"cmd","name":"pose","args":{"x":"500","y":"400"}}' | Out-Null
            Start-Sleep -Milliseconds 800
        }
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

# ---- 0.10a soul: motion seen in pixels (owner ruling 2026-11-10) ----
# Her body's bottom edge in a central column band: the lowest row (from just above her shadow upward) whose pixel is
# at least 250 away (R+G+B) from the same pixel with her moved away (her faint halo stays under 100; her body is ~600). The body bottom is her pivot, so it rises exactly
# as much as she does, whatever her stretch.
function Bottom-Edge([System.Drawing.Bitmap]$bmp, [System.Drawing.Bitmap]$base, $st, [int]$dx = 0, [int]$dy = 0) {
    $x0 = [int]($st.x + $st.width * 0.36) - $dx; $x1 = [int]($st.x + $st.width * 0.64) - $dx
    $yb = [int]($st.y + $st.height + 2) - $dy; $yt = [int]($st.y + $st.height * 0.4) - $dy
    for ($y = $yb; $y -ge $yt; $y--) {
        for ($x = $x0; $x -le $x1; $x += 2) {
            $p = $bmp.GetPixel($x, $y); $b = $base.GetPixel($x + $dx, $y + $dy)
            if ([math]::Abs($p.R - $b.R) + [math]::Abs($p.G - $b.G) + [math]::Abs($p.B - $b.B) -ge 250) { return $y + $dy }
        }
    }
    $null
}
# A capture of a screen rectangle (fast, for a live burst); its pixels are addressed like a full screenshot via $dx/$dy.
function Grab([int]$x, [int]$y, [int]$w, [int]$h) {
    $bmp = [System.Drawing.Bitmap]::new($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, [System.Drawing.Size]::new($w, $h), [System.Drawing.CopyPixelOperation]::SourceCopy)
    $g.Dispose()
    $bmp
}
function Soul-Freeze([string]$kv) { Send ('{"type":"cmd","name":"debug-soul-freeze","args":{' + $kv + '}}') }
# A screenshot of her place with her moved away (the backdrop), then her back where she was.
function Backdrop([string]$name) {
    $i = Info
    Send '{"type":"cmd","name":"pose","args":{"x":"1200","y":"650"}}' | Out-Null
    Start-Sleep -Milliseconds 400
    $path = Screenshot $name
    Send ('{"type":"cmd","name":"pose","args":{"x":"' + [int]$i.canvasX + '","y":"' + [int]$i.canvasY + '"}}') | Out-Null
    Start-Sleep -Milliseconds 300
    $path
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
    Step 'she floats on one clock (her body really moves)' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Milliseconds 600
        if ((Kv 'debug-soul').floating -ne 'true') { throw 'not floating' }
        $st = State
        $base = [System.Drawing.Bitmap]::new((Backdrop 'soul-backdrop.png'))
        Start-Sleep -Milliseconds 500
        $edges = @()
        try {
            foreach ($i in 0..2) {
                $shot = [System.Drawing.Bitmap]::new((Screenshot "soul-float-$i.png"))
                try { $edges += Bottom-Edge $shot $base $st } finally { $shot.Dispose() }
                Start-Sleep -Milliseconds 1050
            }
        }
        finally { $base.Dispose() }
        Write-Host "    body bottom edge over 2.1 s: $($edges -join ', ')"
        if ($edges.Count -lt 3 -or ($edges | Where-Object { $null -eq $_ })) { throw "her body was not found in every frame ($($edges -join ','))" }
        $range = ($edges | Measure-Object -Maximum -Minimum)
        if ($range.Maximum - $range.Minimum -lt 2) { throw "she did not float: bottom edge stayed at $($edges -join ',')" }
    }
    Step 'her leaf sways (two frozen float times, same height)' {
        $st = State
        Soul-Freeze '"floatT":"0"' | Out-Null; Start-Sleep -Milliseconds 300
        $a = [System.Drawing.Bitmap]::new((Screenshot 'soul-leaf-a.png'))
        Soul-Freeze '"floatT":"2.1"' | Out-Null; Start-Sleep -Milliseconds 300
        $b = [System.Drawing.Bitmap]::new((Screenshot 'soul-leaf-b.png'))
        Soul-Freeze '' | Out-Null
        try {
            # Leaf box: rig x 92–152, y 8–60 → canvas +30; the body rect starts at canvas (70,88), 160 units wide.
            $u = $st.width / 160
            $x0 = [int]($st.x + (122 - 70) * $u); $x1 = [int]($st.x + (182 - 70) * $u)
            $y0 = [int]($st.y + (38 - 88) * $u); $y1 = [int]($st.y + (90 - 88) * $u)
            $n = 0
            for ($y = $y0; $y -le $y1; $y++) { for ($x = $x0; $x -le $x1; $x++) {
                $p = $a.GetPixel($x, $y); $q = $b.GetPixel($x, $y)
                if ([math]::Abs($p.R - $q.R) + [math]::Abs($p.G - $q.G) + [math]::Abs($p.B - $q.B) -gt 60) { $n++ } } }
            Write-Host "    leaf pixels changed: $n"
            if ($n -lt 30) { throw "the leaf did not move ($n pixels)" }
        }
        finally { $a.Dispose(); $b.Dispose() }
    }
    Step 'a hop and back' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        $before = State
        $r = Send '{"type":"cmd","name":"soul-hop","args":{"power":"1"}}'
        if ($r.error -notmatch 'hopped=true') { throw "no hop: $($r.error)" }
        if ((Kv 'debug-soul').hopping -ne 'true') { throw 'not hopping right after the hop' }
        Start-Sleep -Milliseconds 1600
        if ((Kv 'debug-soul').hopping -ne 'false') { throw 'still hopping after 1.6 s' }
        if ((Info).bodyScale -ne 'free') { throw 'body scale still busy' }
        $after = State
        if ([math]::Abs($after.x - $before.x) -gt 2 -or [math]::Abs($after.y - $before.y) -gt 2) { throw "she moved: ($($before.x),$($before.y)) -> ($($after.x),$($after.y))" }
    }
    Step 'she really rises (frozen at +0.29 s, and live)' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
        $st = State
        $dpi = [double](Info).dpi
        if (-not $dpi) { $dpi = 1 }
        $base = [System.Drawing.Bitmap]::new((Backdrop 'soul-hop-backdrop.png'))
        $dir = Join-Path $Out 'hop-strip'; New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $times = @(0, 0.045, 0.15, 0.29, 0.45, 0.6, 0.8, 1.1)
        $rises = @(); $tiles = @()
        $size = [int]($st.width * 2.4)   # her whole hop fits: leaf at the peak to the shadow
        try {
            Soul-Freeze '"floatT":"0","hopT":"0"' | Out-Null; Start-Sleep -Milliseconds 300
            $rest = $null
            foreach ($t in $times) {
                Soul-Freeze ('"floatT":"0","hopT":"' + $t + '"') | Out-Null
                Start-Sleep -Milliseconds 250
                $path = Screenshot ('hop-{0:000}.png' -f [int]($t * 1000))
                $bmp = [System.Drawing.Bitmap]::new($path)
                try { $edge = Bottom-Edge $bmp $base $st } finally { $bmp.Dispose() }
                if ($null -eq $rest) { $rest = $edge }
                $rises += [math]::Round(($rest - $edge) / $dpi, 1)
                $tile = Join-Path $dir ('{0:000}.png' -f [int]($t * 1000))
                Crop $path ([int]($st.x + $st.width / 2)) ([int]($st.y + $st.height * 0.1)) $size $tile
                $tiles += $tile
            }
            # The strip for the owner: eight moments of one hop, left to right.
            $strip = [System.Drawing.Bitmap]::new($size * $tiles.Count, $size)
            $g = [System.Drawing.Graphics]::FromImage($strip)
            $font = [System.Drawing.Font]::new('Segoe UI', 10)
            for ($i = 0; $i -lt $tiles.Count; $i++) {
                $img = [System.Drawing.Image]::FromFile($tiles[$i]); $g.DrawImage($img, $i * $size, 0, $size, $size); $img.Dispose()
                $g.DrawString(('+{0:0.000} s' -f $times[$i]), $font, [System.Drawing.Brushes]::Gray, $i * $size + 4, 4)
            }
            $g.Dispose(); $strip.Save((Join-Path $Out 'hop-strip.png'), [System.Drawing.Imaging.ImageFormat]::Png); $strip.Dispose()
        }
        finally { Soul-Freeze '' | Out-Null }
        Write-Host "    rise (DIP) at $($times -join ', ') s: $($rises -join ', ')"
        if ([math]::Abs($rises[3] - 19) -gt 4) { throw "at +0.29 s she rose $($rises[3]) DIP, not about 19" }
        if ([math]::Abs($rises[0]) -gt 2 -or [math]::Abs($rises[1]) -gt 2 -or [math]::Abs($rises[7]) -gt 2) { throw "she was off the ground outside the air phase: $($rises -join ',')" }
        if ($rises[2] -le 2 -or $rises[4] -le 2) { throw "no rise and fall around the peak: $($rises -join ',')" }
        # Live: a burst of quick captures during a real hop finds her high.
        $gx = [int]($st.x - $st.width * 0.1); $gy = [int]($st.y - $st.height * 0.5); $gw = [int]($st.width * 1.2); $gh = [int]($st.height * 1.6)
        $restGrab = Grab $gx $gy $gw $gh
        $restEdge = Bottom-Edge $restGrab $base $st -dx $gx -dy $gy
        $restGrab.Dispose()
        if ($null -eq $restEdge) { $base.Dispose(); throw 'the quick capture does not see her (CopyFromScreen)' }
        Start-Sleep -Milliseconds 800
        Send '{"type":"cmd","name":"soul-hop","args":{"power":"1"}}' | Out-Null
        $best = 0; $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.ElapsedMilliseconds -lt 700) {
            $f = Grab $gx $gy $gw $gh
            try { $e = Bottom-Edge $f $base $st -dx $gx -dy $gy; if ($null -ne $e) { $best = [math]::Max($best, ($restEdge - $e) / $dpi) } } finally { $f.Dispose() }
        }
        $base.Dispose()
        Write-Host "    live hop: highest rise seen $([math]::Round($best, 1)) DIP"
        if ($best -lt 8) { throw "in a live hop she rose only $best DIP" }
    }
    Step 'no hop while dragged' {
        Start-Sleep -Milliseconds 1000
        $st = State
        $from = '{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)
        $to = '{0},{1}' -f [int]($st.x + $st.width / 2 + 150), [int]($st.y + $st.height / 2)
        $drag = Start-Job -ScriptBlock { param($from, $to) & agent-desktop --headed drag --from-xy $from --to-xy $to --duration 1600 --drop-delay 200 --timeout-ms 8000 } -ArgumentList $from, $to
        $tried = $false
        while ($drag.State -eq 'Running') {
            Start-Sleep -Milliseconds 120
            if ((State).motion -eq 'Drag' -and -not $tried) {
                $tried = $true
                $r = Send '{"type":"cmd","name":"soul-hop","args":{"power":"1"}}'
                if ($r.error -notmatch 'hopped=false') { Remove-Job $drag -Force; throw "she hopped while dragged: $($r.error)" }
            }
        }
        Remove-Job $drag -Force
        if (-not $tried) { throw 'never saw the drag' }
        Start-Sleep -Milliseconds 1500
    }
    Step 'the soul survives a rescale' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"soul-hop","args":{"power":"1"}}' | Out-Null
        Send '{"type":"cmd","name":"debug-display-changed"}' | Out-Null
        Start-Sleep -Milliseconds 1600
        if ((Kv 'debug-soul').hopping -ne 'false') { throw 'still hopping after a rescale' }
        Assert-BodyInWork (State) 'after a hop and a display change'
    }
    Step 'click pokes her' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
        Start-Sleep -Milliseconds 300
        $st = State
        $xy = '{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)
        $before = (State).expression
        $hops = [int](Kv 'debug-soul').hops
        & agent-desktop --headed mouse-click --xy $xy | Out-Null
        Start-Sleep -Milliseconds 300
        $st = State
        # 0.11: a tap does one thing — the hop with her joy face; no "poked" expression on top.
        if ($st.expression -ne $before) { throw "expression after click is '$($st.expression)' (was '$before')" }
        if ([int](Kv 'debug-soul').hops -ne $hops + 1 -and (Info).face -ne 'Joy/Joy') { throw 'a click neither hopped nor showed her joy face' }
    }
    Step 'a tap hops with her joy face' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Start-Sleep -Milliseconds 900   # past the hop cooldown
        $st = State
        $xy = '{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)
        $hops = [int](Kv 'debug-soul').hops
        & agent-desktop --headed mouse-click --xy $xy | Out-Null
        $sw = [Diagnostics.Stopwatch]::StartNew()
        do { $face = (Info).face } while ($face -ne 'Joy/Joy' -and $sw.ElapsedMilliseconds -lt 800)
        if ((State).expression -eq 'poked') { throw 'a plain tap still plays the poked expression' }
        if ($face -ne 'Joy/Joy') { throw "face right after the tap: $face" }
        if ([int](Kv 'debug-soul').hops -ne $hops + 1) { throw 'the tap did not hop' }
        Start-Sleep -Milliseconds 1600
        $face = (Info).face
        if ($face -notin @('Happy/Grin', 'Open/Smile')) { throw "face after the joy: $face" }
        [Mouse]::Move(5, 5)
    }
    Step 'the joy face gives way to the newest expression' {
        Start-Sleep -Milliseconds 2000
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        $r = Send '{"type":"cmd","name":"soul-hop","args":{"power":"1"}}'
        if ($r.error -notmatch 'hopped=true') { throw "no hop: $($r.error)" }
        Start-Sleep -Milliseconds 300
        Post 'timer.done' | Out-Null
        Start-Sleep -Milliseconds 1500
        $face = (Info).face
        if ($face -ne 'Open/O') { throw "face after the joy and a timer: $face" }
        $st = State
        & agent-desktop --headed mouse-click --xy ('{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)) | Out-Null
        Start-Sleep -Milliseconds 600
        [Mouse]::Move(5, 5)
    }
    Step 'she acts by herself now and then' {
        Start-Sleep -Milliseconds 1800
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        $r = Kv 'soul-idle'
        $before = [int]$r.acts
        Send '{"type":"cmd","name":"soul-idle","args":{"on":"true","gap":"1"}}' | Out-Null
        $deadline = (Get-Date).AddSeconds(6)
        do { Start-Sleep -Milliseconds 500; $acts = [int](Kv 'soul-idle').acts } while ($acts -le $before -and (Get-Date) -lt $deadline)
        Send '{"type":"cmd","name":"soul-idle","args":{"on":"false"}}' | Out-Null
        if ($acts -le $before) { throw 'no own act in 6 s with a 1 s gap' }
    }
    Step 'reduced motion stops her idle motion' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        $r = Send '{"type":"cmd","name":"debug-reduced-motion","args":{"on":"true"}}'
        if ($r.error -notmatch 'reduced=true' -or $r.error -notmatch 'floating=false') { throw "not reduced: $($r.error)" }
        try {
            Start-Sleep -Milliseconds 600
            $st = State
            $a = [System.Drawing.Bitmap]::new((Screenshot 'reduced-a.png'))
            Start-Sleep -Milliseconds 1000
            $b = [System.Drawing.Bitmap]::new((Screenshot 'reduced-b.png'))
            try {
                $u = $st.width / 160
                $points = @(
                    @([int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)),                 # body centre
                    @([int]($st.x + (152 + 30 - 70) * $u), [int]($st.y + (8 + 30 - 88) * $u + 3)),  # leaf tip
                    @([int]($st.x + (262 - 70) * $u), [int]($st.y + (150 - 88) * $u)))              # where the moon rests
                foreach ($p in $points) {
                    $pa = $a.GetPixel($p[0], $p[1]); $pb = $b.GetPixel($p[0], $p[1])
                    if ([math]::Abs($pa.R - $pb.R) -gt 3 -or [math]::Abs($pa.G - $pb.G) -gt 3 -or [math]::Abs($pa.B - $pb.B) -gt 3) {
                        throw "pixel ($($p[0]),$($p[1])) moved under reduced motion: $($pa.R),$($pa.G),$($pa.B) -> $($pb.R),$($pb.G),$($pb.B)"
                    }
                }
            }
            finally { $a.Dispose(); $b.Dispose() }
            Start-Sleep -Milliseconds 900
            & agent-desktop --headed mouse-click --xy ('{0},{1}' -f [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2)) | Out-Null
            $sw = [Diagnostics.Stopwatch]::StartNew()
            do { $face = (Info).face } while ($face -ne 'Joy/Joy' -and $sw.ElapsedMilliseconds -lt 800)
            if ($face -ne 'Joy/Joy') { throw "no joy face on a tap under reduced motion: $face" }
            if ((Kv 'debug-soul').hopping -ne 'false') { throw 'she hopped under reduced motion' }
        }
        finally {
            $r = Send '{"type":"cmd","name":"debug-reduced-motion","args":{"on":"false"}}'
            [Mouse]::Move(5, 5)
        }
        if ($r.error -notmatch 'floating=true') { throw "not floating again: $($r.error)" }
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
    Step 'a sulk left alone ends after 20 s and her angry face goes' {
        Send '{"type":"cmd","name":"pose","args":{"x":"700","y":"400"}}' | Out-Null
        Send '{"type":"cmd","name":"shake","args":{"swings":"6","force":"angry"}}' | Out-Null
        Wait-State { param($st) $st.expression -eq 'angry' } 1500 'the angry face' | Out-Null
        [Mouse]::Move(5, 5)
        Wait-State { param($st) $st.expression -ne 'angry' } 24000 'the angry face going after the sulk' | Out-Null
        if ((Kv 'debug-shake').sulking -ne 'false') { throw 'still sulking after 20 s' }
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
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -notmatch "fullscreen: hide \(monitor 0, hers 0; class '") { throw 'the log does not say why she hid (monitor, hers, class)' }
        if ($log -notmatch 'fullscreen: show \(monitor none, hers 0; ') { throw 'the log does not say why she came back' }
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
    Step 'her idle acts keep memory inside 60 MB and reuse their animations' {
        Send '{"type":"cmd","name":"pose","args":{"x":"400","y":"250"}}' | Out-Null
        [Mouse]::Move(5, 5)
        Send '{"type":"cmd","name":"soul-idle","args":{"on":"true","gap":"1"}}' | Out-Null
        Start-Sleep -Seconds 40
        $acts = [int](Kv 'soul-idle').acts
        Send '{"type":"cmd","name":"soul-idle","args":{"on":"false"}}' | Out-Null
        $s = Kv 'debug-soul'
        $ws = (State).workingSetBytes
        Write-Host "    idle acts $acts, hops $($s.hops), hop animation sets built $($s.hopSets), working set $([math]::Round($ws / 1MB, 1)) MB"
        if ($acts -lt 10) { throw "only $acts idle acts in 40 s" }
        if ([int]$s.hopSets -gt 2) { throw "hop animations rebuilt per hop: $($s.hopSets) sets for $($s.hops) hops" }
        if ($ws -gt 60MB) { throw "working set $ws bytes" }
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

# debug-menu: "open=true view=main focus=… card=x,y,w,h clasp=x,y line64=… buttons=id:cx,cy,w,h;…" (screen px)
function Menu {
    $r = Send '{"type":"cmd","name":"debug-menu"}'
    if (-not $r.ok) { throw "debug-menu not ok: $($r.error)" }
    $h = @{ buttons = @() }
    foreach ($pair in ($r.error -split ' ')) {
        $k, $v = $pair -split '=', 2
        if ($k -eq 'buttons') {
            if ($v) {
                $h.buttons = @($v -split ';' | ForEach-Object {
                    $id, $xy = $_ -split ':', 2; $p = $xy -split ','
                    [pscustomobject]@{ id = $id; x = [double]$p[0]; y = [double]$p[1]; w = [double]$p[2]; h = [double]$p[3]; r = [math]::Min([double]$p[2], [double]$p[3]) / 2 }
                })
            }
        }
        else { $h[$k] = $v }
    }
    $h
}
function Ids($m) { (@($m.buttons | Where-Object id -ne 'fact') | ForEach-Object id) -join ',' }
Add-Type -TypeDefinition @"
public static class Fg {
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern System.IntPtr GetForegroundWindow();
    public static System.IntPtr Get() { return GetForegroundWindow(); }
}
"@
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

# Two copies of her at once (a "ghost"). A cross-fade mixes the old picture and the new one, so no pixel of a frame
# during it is brighter (or darker) than both — unless a third picture is drawn, e.g. a second outline of her at
# another pose. Ghost.Extra counts such pixels (luminance beyond both by more than a margin).
Add-Type -TypeDefinition @"
public static class Ghost {
    static double L(byte[] p, int i) { return 0.114 * p[i] + 0.587 * p[i + 1] + 0.299 * p[i + 2]; }
    public static int Extra(byte[] frame, byte[] before, byte[] after, double margin) {
        int n = 0;
        for (int i = 0; i + 3 < frame.Length; i += 4) {
            double f = L(frame, i), b = L(before, i), a = L(after, i);
            if (f > System.Math.Max(b, a) + margin || f < System.Math.Min(b, a) - margin) n++;
        }
        return n;
    }
}
"@
function Bytes([System.Drawing.Bitmap]$bmp) {
    $d = $bmp.LockBits([System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try { $buf = [byte[]]::new($d.Stride * $bmp.Height); [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $buf, 0, $buf.Length); , $buf }
    finally { $bmp.UnlockBits($d) }
}

function Stage-Menu {
    Step 'start engine' { Start-Engine }
    Step 'right-click opens the card' {
        Pose-Body 900 450 | Out-Null
        Start-Sleep -Milliseconds 300
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $m = Wait-Menu 'true' 2000 'card open after a right-click'
        if ($m.view -ne 'main') { throw "view $($m.view)" }
        $ids = Ids $m
        if ($ids -ne 'chat,send,play,dnd,hide,timer,note,listen,clipboard') { throw "buttons: $ids (no mic, no settings yet)" }
        if (-not (State).menuOpen) { throw 'state.menuOpen is false' }
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -notmatch 'menu: open \(right\)') { throw 'the log does not say how the card opened' }
        [Mouse]::Move(5, 5)
        Start-Sleep -Milliseconds 600
        Screenshot 'menu-card.png' | Out-Null
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'card closed' | Out-Null
    }
    Step 'a double-click pokes twice and does not open the card' {
        Start-Sleep -Milliseconds 1500
        $c = Body-Centre
        $hops = [int](Kv 'debug-soul').hops
        [Mouse]::DoubleClick($c[0], $c[1])
        Start-Sleep -Milliseconds 600
        if ((Menu).open -ne 'false') { throw 'a double-click opened the card (default: right-click and the shortcut only)' }
        if ([int](Kv 'debug-soul').hops -lt $hops + 1) { throw 'no poke hop' }
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 2
    }
    Step 'the card opens fast' {
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $m = Wait-Menu 'true' 2000 'card open'
        Write-Host "    openMs=$($m.openMs)"
        if ([int]$m.openMs -ge 100) { throw "the card took $($m.openMs) ms to set up" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'card closed' | Out-Null
    }
    Step 'her moon clasps the card' {
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $m = Wait-Menu 'true' 2000 'card open'
        Start-Sleep -Milliseconds 500
        $cx, $cy, $cw, $ch = $m.card -split ',' | ForEach-Object { [double]$_ }
        $mx, $my = $m.clasp -split ',' | ForEach-Object { [double]$_ }
        $near = [math]::Min([math]::Sqrt([math]::Pow($mx - $cx, 2) + [math]::Pow($my - $cy, 2)), [math]::Sqrt([math]::Pow($mx - $cx - $cw, 2) + [math]::Pow($my - $cy, 2)))
        if ($near -gt 30) { throw "the clasp at $($m.clasp) is $([int]$near) px from the card's top corners ($($m.card))" }
        if ((Info).moon -ne '0') { throw "her own moon is $((Info).moon) with the card open" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'card closed' | Out-Null
        Start-Sleep -Milliseconds 300
        if ((Info).moon -ne '1') { throw "her own moon is $((Info).moon) after the card closed" }
    }
    Step 'keys move and act' {
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}{TAB}{TAB}')
        Start-Sleep -Milliseconds 200
        $m = Menu
        if ($m.focus -ne 'play') { throw "focus after Tab x3: $($m.focus)" }
        [System.Windows.Forms.SendKeys]::SendWait('1')
        Start-Sleep -Milliseconds 300
        if ((Menu).view -ne 'play') { throw "view after 1: $((Menu).view)" }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 300
        if ((Menu).view -ne 'main') { throw "view after Esc in a drill: $((Menu).view)" }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-Menu 'false' 1000 'card closed by the second Esc' | Out-Null
    }
    Step 'a click on another app closes the card and leaves that app in front' {
        $w = Start-TestWindow 'typing'
        try {
            $c = Body-Centre
            [Mouse]::Click($c[0], $c[1], $true)
            Wait-Menu 'true' 2000 'card open' | Out-Null
            Start-Sleep -Milliseconds 300
            & agent-desktop --headed mouse-click --xy '430,320' 2>&1 | Out-Null   # inside the test window (other monitor parts are the catcher's)
            Wait-Menu 'false' 2000 'card closed by the click on another app' | Out-Null
            Start-Sleep -Milliseconds 400
            $fg = [Fg]::Get()
            if ($fg -ne $w.MainWindowHandle) { throw "foreground is $fg, the test window is $($w.MainWindowHandle)" }
        }
        finally { $w.Kill() }
    }
    Step 'focus goes back to the window that had it' {
        $w = Start-TestWindow 'typing'
        try {
            $c = Body-Centre
            [Mouse]::Click($c[0], $c[1], $true)
            Wait-Menu 'true' 2000 'card open' | Out-Null
            Start-Sleep -Milliseconds 300
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            Wait-Menu 'false' 1000 'card closed by Esc' | Out-Null
            Start-Sleep -Milliseconds 300
            $fg = [Fg]::Get()
            if ($fg -ne $w.MainWindowHandle) { throw "foreground is $fg, the test window is $($w.MainWindowHandle)" }
        }
        finally { $w.Kill() }
    }
    Step 'a poke, then a right-click mid-hop: the card opens where a lone right-click puts it, with her settled and one face (strip)' {
        # The owner (0.10.0): "when I double-click her she laughs, but the look glitches" — the menu opened mid-hop. A
        # double-click is two pokes now (spec §0.6); the menu opening mid-hop is a poke and then a right-click.
        Pose-Body 500 330 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 2
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $ref = Wait-Menu 'true' 2000 'menu open after a right-click'
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'menu closed' | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 3
        $rest = Info
        $size = 360; $gx = $c[0] - $size / 2; $gy = $c[1] - $size / 2

        $dir = Join-Path $Out 'dblclick'; New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $tiles = @(); $soul = $null; $second = $null
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        [Mouse]::Click($c[0], $c[1], $false)
        $first = $clock.ElapsedMilliseconds
        for ($i = 0; $i -lt 12; $i++) {
            $due = $first + 125 * $i
            while ($clock.ElapsedMilliseconds -lt $due) { Start-Sleep -Milliseconds 5 }
            if ($i -eq 2 -and -not $second) {
                [Mouse]::Click($c[0], $c[1], $true)   # the right-click, mid-hop
                $second = $clock.ElapsedMilliseconds
                $soul = Kv 'debug-soul'
                $soulAt = $clock.ElapsedMilliseconds
            }
            $bmp = Grab $gx $gy $size $size
            $t = $clock.ElapsedMilliseconds - $first
            $path = Join-Path $dir ('t{0:D4}ms.png' -f $t)
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
            $tiles += $path
        }
        Contact-Sheet $tiles 6 240 (Join-Path $Out 'poke-then-menu-strip.png')
        $m = Wait-Menu 'true' 1500 'card open after the right-click'
        Screenshot 'poke-then-menu.png' | Out-Null
        $after = Info
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Write-Host "    clicks at $first and $second ms; soul $($soul.hopping)/happy $($soul.happy) at $soulAt ms; face at rest $($rest.face) ($($rest.faceSwaps) swaps), after $($after.face) ($($after.faceSwaps) swaps)"
        $errors = @()
        foreach ($b in $ref.buttons) {
            $d = $m.buttons | Where-Object id -eq $b.id
            if (-not $d -or [math]::Abs($d.x - $b.x) -gt 3 -or [math]::Abs($d.y - $b.y) -gt 3) { $errors += "button $($b.id) at ($($d.x),$($d.y)), at rest ($($b.x),$($b.y))" }
        }
        if ($soul.hopping -ne 'false') { $errors += 'still hopping once the menu opened' }
        if ($soul.happy -ne 'false') { $errors += 'the joy face stayed once the menu opened' }
        if ($after.face -ne $rest.face) { $errors += "her face is $($after.face) with the menu open, $($rest.face) at rest" }
        # A poke face, then the joy face, then (menu) her own face: three swaps at most; more is a face flickering.
        if ([int]$after.faceSwaps - [int]$rest.faceSwaps -gt 3) { $errors += "$([int]$after.faceSwaps - [int]$rest.faceSwaps) face swaps in 1.5 s" }
        if ($errors.Count) { throw ($errors -join '; ') }
    }
    Step 'no ghost: a mood cross-fade at the hop''s squash shows one outline of her' {
        # The owner's phone video showed a squashed body over a taller, translucent one. Frozen at the anticipation
        # (+0.045 s) and landing (+0.56 s) squash, her mood cross-fades (idle Calm <-> back Happy, neither squishes)
        # while frames are grabbed; every frame must be a mix of the settled pictures before and after (a blink or a
        # twinkle may add a few dozen pixels; a second outline of her body adds about a thousand).
        # Since 0.11 a face change is a blink: both eyes shut a moment (~300 px lighter than both pictures, by design): limit 600.
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Pose-Body 500 330 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Milliseconds 800
        $st = State
        $size = 240; $gx = [int]($st.x + $st.width / 2 - $size / 2); $gy = [int]($st.y + $st.height / 2 - $size / 2)
        $dir = Join-Path $Out 'ghost'; New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $found = @(); $tiles = @(); $report = @()
        try {
            foreach ($t in @('0.045', '0.56')) {
                Soul-Freeze ('"floatT":"0","hopT":"' + $t + '"') | Out-Null
                Send '{"type":"cmd","name":"expression","args":{"key":"idle"}}' | Out-Null
                Start-Sleep -Milliseconds 600
                foreach ($key in @('back', 'idle')) {
                    $g = Grab $gx $gy $size $size; $before = Bytes $g; $g.Dispose()
                    Send ('{"type":"cmd","name":"expression","args":{"key":"' + $key + '"}}') | Out-Null
                    $frames = @(); $clock = [System.Diagnostics.Stopwatch]::StartNew()
                    while ($clock.ElapsedMilliseconds -lt 320) { $frames += , @($clock.ElapsedMilliseconds, (Grab $gx $gy $size $size)) }
                    Start-Sleep -Milliseconds 500
                    $g = Grab $gx $gy $size $size; $after = Bytes $g; $g.Dispose()
                    $k = 0
                    foreach ($f in $frames) {
                        $extra = [Ghost]::Extra((Bytes $f[1]), $before, $after, 60)
                        $report += "$t>$key@$($f[0]):$extra"
                        if ($extra -gt 600) { $found += "hop $t to $key at $($f[0]) ms: $extra px beyond both pictures" }
                        if ($k++ % 3 -eq 0) { $path = Join-Path $dir ("{0}-{1}-{2:D3}ms.png" -f $t, $key, $f[0]); $f[1].Save($path); $tiles += $path }
                        $f[1].Dispose()
                    }
                }
            }
        }
        finally { Soul-Freeze '' | Out-Null }
        Write-Host "    pixels beyond both pictures (pose>mood@ms:px): $($report -join ' ')"
        Contact-Sheet $tiles 6 200 (Join-Path $Out 'ghost-strip.png')
        if ($found.Count) { throw "a ghost: $($found -join '; ')" }
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
    Step 'a click outside closes it and nothing else happens' {
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Start-Sleep -Milliseconds 300
        $before = (State).expression
        [Mouse]::Click($c[0] - 400, $c[1] + 250, $false)
        Wait-Menu 'false' 1000 'menu closed by a click outside' | Out-Null
        if ((State).expression -ne $before) { throw "expression changed to $((State).expression)" }
    }
    Step 'items work by real clicks' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $m = Wait-Menu 'true' 1000 'card open'
        Start-Sleep -Milliseconds 600
        $b = $m.buttons | Where-Object id -eq 'play'
        [Mouse]::Click([int]$b.x, [int]$b.y, $false)
        Start-Sleep -Milliseconds 600
        $m = Menu
        if ($m.view -ne 'play') { throw "view is '$($m.view)' after clicking play" }
        if ((Ids $m) -ne 'game-chase,game-hide,game-ball,game-stars,play-random,back') { throw "play drill: $(Ids $m)" }
        [Mouse]::Move(5, 5)
        Start-Sleep -Milliseconds 300
        Screenshot 'menu-play.png' | Out-Null
        $b = $m.buttons | Where-Object id -eq 'back'
        [Mouse]::Click([int]$b.x, [int]$b.y, $false)
        Start-Sleep -Milliseconds 500
        if ((Menu).view -ne 'main') { throw "view is '$((Menu).view)' after back" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'card closed' | Out-Null
    }
    Step 'from every corner the card is on screen and never over her' {
        $n = 0
        foreach ($corner in @(@($Work.Left, $Work.Top), @(($Work.Right - 80), $Work.Top), @($Work.Left, ($Work.Bottom - 80)), @(($Work.Right - 80), ($Work.Bottom - 80)))) {
            $n++
            Pose-Body $corner[0] $corner[1] | Out-Null
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            $m = Wait-Menu 'true' 1000 "card open in corner $n"
            $st = State
            $cx, $cy, $cw, $ch = $m.card -split ',' | ForEach-Object { [double]$_ }
            if ($cx -lt $Work.Left -or $cy -lt $Work.Top -or $cx + $cw -gt $Work.Right + 1 -or $cy + $ch -gt $Work.Bottom + 1) { throw "corner ${n}: card $($m.card) is off the work area $Work" }
            if ($cx -lt $st.x + $st.width -and $cx + $cw -gt $st.x -and $cy -lt $st.y + $st.height -and $cy + $ch -gt $st.y) { throw "corner ${n}: card $($m.card) is over her body ($($st.x),$($st.y))" }
            foreach ($b in $m.buttons) {
                if ($b.x -lt $Work.Left -or $b.y -lt $Work.Top -or $b.x -gt $Work.Right -or $b.y -gt $Work.Bottom) { throw "corner ${n}: $($b.id) at ($($b.x),$($b.y)) is off the work area" }
            }
            Start-Sleep -Milliseconds 600
            Screenshot "menu-corner-$n.png" | Out-Null
            Send '{"type":"cmd","name":"menu-close"}' | Out-Null
            Wait-Menu 'false' 1000 'card closed' | Out-Null
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
        $ids = Ids (Menu)
        if ($ids -ne 'game-chase,game-hide,game-ball,game-stars,play-random,back') { throw "play drill: $ids" }
        Press 'play-random'
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
        $ids = Ids (Menu)
        if ($ids -ne 'timer-5,timer-15,timer-25,timer-45,timer-custom,back') { throw "timer options: $ids" }
        Start-Sleep -Milliseconds 400
        Screenshot 'menu-timer.png' | Out-Null
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
        $ids = Ids (Menu)
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
    Step 'do not disturb for 30 minutes, and the tile turns it off' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'card open' | Out-Null
        Press 'dnd'
        $ids = Ids (Menu)
        if ($ids -ne 'dnd-30,dnd-60,dnd-120,dnd-on,back') { throw "dnd options: $ids" }
        Start-Sleep -Milliseconds 400
        Screenshot 'menu-hush.png' | Out-Null
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
        $m = Wait-Menu 'true' 1000 'card open'
        if ($m.hush -ne 'true') { throw "hush=$($m.hush) with do-not-disturb on" }
        if ((Text64 $m.line64) -ne 'عم اسكت هلق') { throw "header line: $(Text64 $m.line64)" }
        if ($m.fact -ne 'hush') { throw "fact: $($m.fact)" }
        Start-Sleep -Milliseconds 400
        Screenshot 'menu-hushed.png' | Out-Null
        Press 'dnd'
        Wait-Menu 'false' 1000 'card closed after the hush tile' | Out-Null
        if ((Kv 'debug-dnd').on -ne 'false') { throw 'the hush tile did not turn do-not-disturb off' }
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
    Step 'a click on a segment starts the timer' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'card open' | Out-Null
        Press 'timer'
        Start-Sleep -Milliseconds 500
        $b = (Menu).buttons | Where-Object id -eq 'timer-15'
        [Mouse]::Click([int]$b.x, [int]$b.y, $false)
        Start-Sleep -Milliseconds 500
        $t = Kv 'debug-timer'
        if ($t.running -ne 'true' -or [int]$t.remaining -lt 890) { throw "a click on «١٥ د» did not start the timer (running=$($t.running))" }
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
            if ($d -gt 520) { throw "button $($b.id) is $([int]$d) px from her" }   # the card beside her: ~450 px at most
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
            Press 'play'
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
    # ---- Her chat row (Task 6; the mic is deferred to the voice phase) ----
    function Chat-Type([string]$text) {
        $m = Menu
        $b = $m.buttons | Where-Object id -eq 'chat'
        [Mouse]::Click([int]$b.x, [int]$b.y, $false)
        Start-Sleep -Milliseconds 300
        $edit = [IntPtr][long](Menu).chat
        if ($edit -eq [IntPtr]::Zero) { throw 'no chat field' }
        foreach ($ch in $text.ToCharArray()) { [Shell]::PostMessage($edit, 0x0102, [IntPtr][int]$ch, [IntPtr]1) | Out-Null }
        Start-Sleep -Milliseconds 200
        [Shell]::PostMessage($edit, 0x0100, [IntPtr]0x0D, [IntPtr]1) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    Step 'typing «مؤقت ٥» starts a timer, she answers and hops' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Pose-Body 900 450 | Out-Null
        Start-Sleep -Milliseconds 1200
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Start-Sleep -Milliseconds 400
        $hops = [int](Kv 'debug-soul').hops
        Chat-Type (-join ([char[]](0x0645, 0x0624, 0x0642, 0x062A, 0x0020, 0x0665)))   # مؤقت ٥
        $t = Kv 'debug-timer'
        if ($t.running -ne 'true' -or [int]$t.remaining -lt 290 -or [int]$t.remaining -gt 300) { throw "timer: running=$($t.running) remaining=$($t.remaining)" }
        $m = Menu
        if ((Text64 $m.line64) -ne 'تمام! شغّلت مؤقت ٥ دقيقة.') { throw "reply: $(Text64 $m.line64)" }
        if ($m.draft -ne '0') { throw "the field still holds $($m.draft) characters" }
        if ([int](Kv 'debug-soul').hops -ne $hops + 1) { throw 'she did not hop' }
        Start-Sleep -Milliseconds 300
        Screenshot 'menu-chat-reply.png' | Out-Null
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw -Encoding UTF8
        if ($log -notmatch 'chat: Timer') { throw 'the log does not say a timer command came' }
        if ($log -match [string][char]0x0645 + [char]0x0624 + [char]0x0642 + [char]0x062A) { throw 'the log holds what was typed' }
        Press 'timer'
        Press 'timer-cancel'
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'card open again' | Out-Null
    }
    Step 'an unknown line gets her learning reply, then her question again' {
        Chat-Type (-join ([char[]](0x0634, 0x0648, 0x0020, 0x0627, 0x0633, 0x0645, 0x0643)))   # شو اسمك
        $m = Menu
        if ((Text64 $m.line64) -ne 'سمعتك! لسا عم اتعلّم الحكي، وقريباً بجاوبك على كل شي.') { throw "reply: $(Text64 $m.line64)" }
        Start-Sleep -Milliseconds 6500
        if ((Text64 (Menu).line64) -ne 'شو بدك نعمل؟') { throw "after 6.5 s: $(Text64 (Menu).line64)" }
    }
    Step '«نلعب» opens the games' {
        Chat-Type (-join ([char[]](0x0646, 0x0644, 0x0639, 0x0628)))   # نلعب
        if ((Menu).view -ne 'play') { throw "view: $((Menu).view)" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'card closed' | Out-Null
    }
    Step 'no mic on this machine (deferred to the voice phase)' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $m = Wait-Menu 'true' 1000 'card open'
        if ($m.mic -ne 'false' -or (Ids $m) -match 'mic') { throw "mic=$($m.mic) ids=$(Ids $m)" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'Esc in the chat field closes the card' {
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Start-Sleep -Milliseconds 300
        $b = (Menu).buttons | Where-Object id -eq 'chat'
        [Mouse]::Click([int]$b.x, [int]$b.y, $false)
        Start-Sleep -Milliseconds 300
        if ((Menu).caret -ne 'true') { throw 'no caret in the chat field after a click on it' }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-Menu 'false' 1000 'card closed by Esc from the chat field' | Out-Null
    }
    # ---- Review fixes (0.11 fix pass): what the owner would see ----
    Step 'closing keeps the overlay until the card has faded' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'card open' | Out-Null
        Start-Sleep -Milliseconds 700
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        $o = Overlay
        if ($o.needs -notmatch 'menu') { throw "the overlay let go of the card at once (needs=$($o.needs)): the closing card is torn" }
        Start-Sleep -Milliseconds 700
        if ((Overlay).needs -ne 'none') { throw "needs left after the close: $((Overlay).needs)" }
    }
    Step 'the chat field appears with the card, exactly over its field' {
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $deadline = (Get-Date).AddSeconds(3)
        do { $m = Menu; Start-Sleep -Milliseconds 50 } while ($m.chatShown -ne 'true' -and (Get-Date) -lt $deadline)
        Write-Host "    chatAt=$($m.chatAt) openEnd=$($m.openEnd) chatRect=$($m.chatRect)"
        if ([int]$m.openEnd -lt 0 -or [int]$m.chatAt -lt [int]$m.openEnd) { throw "the chat field showed at $($m.chatAt) ms, the card finished opening at $($m.openEnd) ms" }
        $b = $m.buttons | Where-Object id -eq 'chat'
        $l, $t, $r, $bt = $m.chatRect -split ',' | ForEach-Object { [double]$_ }
        if ([math]::Abs(($l + $r) / 2 - $b.x) -gt 1 -or [math]::Abs(($t + $bt) / 2 - $b.y) -gt 1 -or [math]::Abs(($r - $l) - $b.w) -gt 2 -or [math]::Abs(($bt - $t) - $b.h) -gt 2) {
            throw "the chat popup $($m.chatRect) is not over the drawn field ($($b.x),$($b.y) $($b.w)x$($b.h))"
        }
    }
    Step 'a click on the field''s padding still gives the caret' {
        $l, $t, $r, $bt = (Menu).chatRect -split ',' | ForEach-Object { [int]$_ }
        [Mouse]::Click($l + 3, [int](($t + $bt) / 2), $false)
        Start-Sleep -Milliseconds 300
        if ((Menu).caret -ne 'true') { throw 'no caret after a click on the field''s padding' }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'card closed' | Out-Null
    }
    Step 'a countdown in the header does not redraw the chat field' {
        Send '{"type":"cmd","name":"timer","args":{"seconds":"300"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 1000 'card open' | Out-Null
        Start-Sleep -Milliseconds 800
        $first = [int](Menu).chatShows
        Start-Sleep -Milliseconds 3200
        $m = Menu
        if ($m.fact -ne 'timer') { throw "fact: $($m.fact)" }
        if ([int]$m.chatShows -ne $first) { throw "the chat field was placed again $([int]$m.chatShows - $first) times in 3 s (flicker)" }
        Press 'timer'
        Press 'timer-cancel'
    }
    Step 'a Windows setting change keeps the card and the draft' {
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Start-Sleep -Milliseconds 500
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Start-Sleep -Milliseconds 500
        $edit = [IntPtr][long](Menu).chat
        foreach ($ch in 'abc'.ToCharArray()) { [Shell]::PostMessage($edit, 0x0102, [IntPtr][int]$ch, [IntPtr]1) | Out-Null }
        Start-Sleep -Milliseconds 200
        $h = [Shell]::FindWindow('Sahar.Engine', 'Sahar')
        [Shell]::PostMessage($h, 0x001A, [IntPtr]0, [IntPtr]0) | Out-Null   # WM_SETTINGCHANGE
        Start-Sleep -Milliseconds 600
        $m = Menu
        if ($m.open -ne 'true') { throw 'a setting change closed the card' }
        if ($m.draft -ne '3') { throw "the draft holds $($m.draft) characters" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 1000 'card closed' | Out-Null
    }
    Step 'the shortcut does nothing during a game' {
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Chase","seconds":"30"}}' | Out-Null
        Start-Sleep -Milliseconds 800
        [System.Windows.Forms.SendKeys]::SendWait('^%s')
        Start-Sleep -Milliseconds 700
        $open = (Menu).open; $running = (Game).running
        Send '{"type":"cmd","name":"game-stop"}' | Out-Null
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        if ($open -ne 'false') { throw 'the shortcut opened the card over a game' }
        if ($running -ne 'true') { throw 'the shortcut ended the game' }
        Start-Sleep -Seconds 3
    }
    Step 'a failing swap never freezes the overlay' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Pose-Body 900 450 | Out-Null
        Start-Sleep -Milliseconds 500
        Send '{"type":"cmd","name":"debug-swap-throw"}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Start-Sleep -Milliseconds 1500
        $m = Menu; $o = Overlay
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        if ($o.swapping -ne 'false') { throw 'the overlay is stuck mid-swap' }
        if ($m.shown -ne 'true') { throw 'the card never appeared after a failing swap' }
        Start-Sleep -Milliseconds 600
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Start-Sleep -Milliseconds 1000
        if ((Menu).shown -ne 'true') { throw 'the card did not appear the next time' }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'face changes reuse their animations' {
        $before = [int](Info).faceAnims
        foreach ($i in 1..10) {
            Send '{"type":"cmd","name":"expression","args":{"key":"back"}}' | Out-Null; Start-Sleep -Milliseconds 300
            Send '{"type":"cmd","name":"expression","args":{"key":"idle"}}' | Out-Null; Start-Sleep -Milliseconds 300
        }
        $made = [int](Info).faceAnims - $before
        if ($made -gt 12) { throw "20 face changes made $made new animations" }
    }
    # ---- The card follows its settings (Task 7): each step writes settings.json and restarts her. ----
    $script:settingsPath = Join-Path $env:APPDATA 'Sahar\settings.json'
    $script:settingsKept = if (Test-Path $script:settingsPath) { Get-Content $script:settingsPath -Raw } else { $null }
    function Restart-With([string]$json) {
        Stop-Engine
        New-Item -ItemType Directory -Force -Path (Split-Path $script:settingsPath) | Out-Null
        Set-Content -Path $script:settingsPath -Value $json -Encoding UTF8
        Start-Engine
        Pose-Body 900 450 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Milliseconds 500
    }
    Step 'the shortcut opens the card with the focus in the chat row' {
        Restart-With '{ "helloDone": true }'
        [System.Windows.Forms.SendKeys]::SendWait('^%s')
        $m = Wait-Menu 'true' 2000 'card open by Ctrl+Alt+S'
        Start-Sleep -Milliseconds 400
        $m = Menu
        if ($m.focus -ne 'chat' -or $m.caret -ne 'true') { throw "focus: $($m.focus) caret: $($m.caret)" }
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -notmatch 'menu: open \(shortcut\)') { throw 'the log does not say the shortcut opened it' }
        if ((Ids $m) -match 'settings') { throw 'a settings button without the settings app' }
        [System.Windows.Forms.SendKeys]::SendWait('^%s')
        Wait-Menu 'false' 1000 'card closed by the shortcut again' | Out-Null
    }
    Step 'only the chosen items show' {
        Restart-With '{ "helloDone": true, "menuItems": ["play", "timer"] }'
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $m = Wait-Menu 'true' 1000 'card open'
        if ((Ids $m) -ne 'play,timer') { throw "buttons: $(Ids $m)" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'right-click only' {
        Restart-With '{ "helloDone": true, "menuOpen": "right" }'
        $c = Body-Centre
        [Mouse]::DoubleClick($c[0], $c[1])
        Start-Sleep -Milliseconds 700
        if ((Menu).open -ne 'false') { throw 'a double-click opened it' }
        Start-Sleep -Milliseconds 1200
        [Mouse]::Click($c[0], $c[1], $true)
        Wait-Menu 'true' 2000 'card open by a right-click' | Out-Null
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'double-click only' {
        Restart-With '{ "helloDone": true, "menuOpen": "double" }'
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        Start-Sleep -Milliseconds 700
        if ((Menu).open -ne 'false') { throw 'a right-click opened it' }
        [Mouse]::DoubleClick($c[0], $c[1])
        Wait-Menu 'true' 2000 'card open by a double-click' | Out-Null
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        if ($log -notmatch 'menu: open \(double\)') { throw 'the log does not say a double-click opened it' }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'in the middle of the screen' {
        Restart-With '{ "helloDone": true, "menuPlace": "center" }'
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $m = Wait-Menu 'true' 1000 'card open'
        $cx, $cy, $cw, $ch = $m.card -split ',' | ForEach-Object { [double]$_ }
        $dx = $cx + $cw / 2 - ($Work.Left + $Work.Width / 2); $dy = $cy + $ch / 2 - ($Work.Top + $Work.Height / 2)
        if ([math]::Abs($dx) -gt 2 -or [math]::Abs($dy) -gt 2) { throw "card centre is ($dx,$dy) from the work area's" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'the header shows nothing when asked' {
        Restart-With '{ "helloDone": true, "menuHeader": "none" }'
        Send '{"type":"cmd","name":"timer","args":{"seconds":"300"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $m = Wait-Menu 'true' 1000 'card open'
        if ($m.fact -ne 'none') { throw "fact: $($m.fact)" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'a dark card' {
        Restart-With '{ "helloDone": true, "appearance": "dark" }'
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        $m = Wait-Menu 'true' 1000 'card open'
        if ($m.dark -ne 'true') { throw "dark=$($m.dark)" }
        Start-Sleep -Milliseconds 600
        $shot = Screenshot 'menu-card-dark.png'
        $cx, $cy, $cw, $ch = $m.card -split ',' | ForEach-Object { [double]$_ }
        $px = Pixel $shot ([int]($cx + 70)) ([int]($cy + 84))
        $want = [System.Drawing.ColorTranslator]::FromHtml('#3A3450')
        if ([math]::Abs($px.R - $want.R) -gt 6 -or [math]::Abs($px.G - $want.G) -gt 6 -or [math]::Abs($px.B - $want.B) -gt 6) { throw "header pixel $($px.R),$($px.G),$($px.B), want 58,52,80" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }
    Step 'a taken shortcut is refused, and she carries on' {
        $marker = Join-Path $env:TEMP 'sahar-hotkey-holder.txt'
        Remove-Item $marker -ErrorAction SilentlyContinue
        # A script file: a -Command line with quotes in it is mangled on its way through Start-Process.
        $script = Join-Path $env:TEMP 'sahar-hotkey-holder.ps1'
        Set-Content -Path $script -Encoding UTF8 -Value @"
Add-Type -TypeDefinition 'public static class HK { [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool RegisterHotKey(System.IntPtr h, int id, uint m, uint vk); }'
`$ok = [HK]::RegisterHotKey([System.IntPtr]::Zero, 7, 3, 0x4B)
Set-Content -Path '$marker' -Value `$ok
Start-Sleep -Seconds 60
"@
        $holder = Start-Process -FilePath 'pwsh' -PassThru -ArgumentList '-NoProfile', '-File', $script
        try {
            $deadline = (Get-Date).AddSeconds(20)
            $held = ''
            while (-not $held -and (Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 200
                if (Test-Path $marker) { $held = "$(Get-Content $marker -Raw -ErrorAction SilentlyContinue)".Trim() }
            }
            Write-Host "    the helper holds Ctrl+Alt+K: $held"
            if ($held -ne 'True') { throw "the helper could not take Ctrl+Alt+K ($held)" }
            Restart-With '{ "helloDone": true, "menuShortcut": "Ctrl+Alt+K" }'
            Start-Sleep -Milliseconds 500
            $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
            if ($log -notmatch 'hotkey: taken') { throw 'the log does not say the shortcut was taken' }
            if ((Menu).open -ne 'false') { throw 'debug-menu: unexpected state' }
        }
        finally { if ($holder -and -not $holder.HasExited) { $holder.Kill() } }
    }
    Step 'nothing visible: the header and the Esc hint, and Esc still closes' {
        Restart-With '{ "helloDone": true, "menuItems": [], "menuHeader": "none" }'
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $m = Wait-Menu 'true' 2000 'card open'
        if ($m.buttons.Count -ne 0) { throw "buttons: $(Ids $m)" }
        Start-Sleep -Milliseconds 400
        Screenshot 'menu-empty.png' | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-Menu 'false' 1000 'card closed by Esc' | Out-Null
    }
    Stop-Engine
    if ($null -ne $script:settingsKept) { Set-Content -Path $script:settingsPath -Value $script:settingsKept -Encoding UTF8 -NoNewline }
    else { Remove-Item $script:settingsPath -ErrorAction SilentlyContinue }
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
        $ids = Ids (Menu)
        if (($ids -split ',') -notcontains 'clipboard') { throw "no clipboard on the card: $ids" }
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
            # A session counts from midnight at the latest: near midnight (CI runs at any hour), step past it first.
            $toMidnight = ((Get-Date).Date.AddDays(1) - (Get-Date)).TotalMinutes
            if ($toMidnight -lt 130) { Send ('{"type":"cmd","name":"apptime-skip","args":{"minutes":"' + [int]($toMidnight + 1) + '"}}') | Out-Null }
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
    Step 'away stops her own actions, whatever face shows (owner log: ~60 actions overnight)' {
        $r = Send '{"type":"cmd","name":"life","args":{"gap":"1"}}'
        if (-not $r.ok) { throw "life not ok: $($r.error)" }
        Post 'music' | Out-Null   # a song outranks her sleepy face: away must still be away
        Post 'user.away' | Out-Null
        $p = Kv 'debug-presence'
        if ($p.away -ne 'true') { throw "presence: $($p.away)" }
        Start-Sleep -Milliseconds 1500
        $before = [int](Life).actions
        Start-Sleep -Seconds 6
        $after = [int](Life).actions
        Post 'user.back' | Out-Null
        Post 'music.stop' | Out-Null
        if ($after -ne $before) { throw "she acted $($after - $before) times while you were away" }
        if ((Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw) -notmatch 'presence: away') { throw 'the log does not say you went away' }
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
        # Old test builds' data: no settings saved by this version (an earlier stage in the same job may have saved some).
        Remove-Item (Join-Path $roaming 'settings.json') -ErrorAction SilentlyContinue
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
        foreach ($name in 'الفجر', 'الشروق', 'الضهر', 'العصر', 'المغرب', 'العشا') {
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
        # Between 00:00 and 04:59 (CI runs at any hour) her once-a-night "late-night" moment may show first; it passes.
        if ($st.expression -eq 'late-night') { $st = Wait-State { param($s) $s.expression -ne 'late-night' } 12000 'the late-night moment passing' }
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

function Game { Kv 'debug-game' }

function Stage-Games {
    Step 'start engine' {
        Stop-Engine
        Remove-Item (Join-Path $env:APPDATA 'Sahar\settings.json') -ErrorAction SilentlyContinue
        Start-Engine
        Pose-Body 600 350 | Out-Null
    }
    Step 'play opens the games; a game takes the mouse and Esc or stop ends it with the score' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Press 'play'
        Press 'game-chase'
        $g = Game
        if ($g.running -ne 'true' -or $g.kind -ne 'Chase') { throw "no chase game: $($g.running) $($g.kind)" }
        if ((State).motion -ne 'Game') { throw "motion during the game: $((State).motion)" }
        Send '{"type":"cmd","name":"game-stop"}' | Out-Null
        if ((Game).running -ne 'false') { throw 'still running after stop' }
        if ((Text64 (Kv 'debug-comment').text64) -ne 'ولا مرة مسكتني 😜') { throw "result: '$(Text64 (Kv 'debug-comment').text64)'" }
        Wait-State { param($st) $st.motion -eq 'Hover' } 2000 'back to hovering' | Out-Null
    }
    Step 'chase: she runs from the pointer and a click on her counts' {
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Chase","seconds":"30"}}' | Out-Null
        Start-Sleep -Milliseconds 400
        $g = Game
        $cx = [int]$g.x + [int]$g.w / 2; $cy = [int]$g.y + [int]$g.h / 2
        [Mouse]::Move($cx - 50, $cy)
        Start-Sleep -Milliseconds 700
        $g2 = Game
        if ([Math]::Abs([int]$g2.x - [int]$g.x) + [Math]::Abs([int]$g2.y - [int]$g.y) -lt 60) { throw "she did not run away ($($g.x),$($g.y) -> $($g2.x),$($g2.y))" }
        Screenshot 'game-chase.png' | Out-Null
        $hx = [int]$g2.x + [int]$g2.w / 2; $hy = [int]$g2.y + [int]$g2.h / 2
        Send "{`"type`":`"cmd`",`"name`":`"game-click`",`"args`":{`"x`":`"$hx`",`"y`":`"$hy`"}}" | Out-Null
        if ((Game).score -ne '1') { throw "score after a catch: $((Game).score)" }
        Send '{"type":"cmd","name":"game-stop"}' | Out-Null
        if ((Text64 (Kv 'debug-comment').text64) -ne 'مسكتني مرة وحدة! 🏆') { throw "result: '$(Text64 (Kv 'debug-comment').text64)'" }
    }
    Step 'the chase is catchable: she rests after each dart (owner log: 0 catches in 5 of 6 games)' {
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Chase","seconds":"25"}}' | Out-Null
        $deadline = (Get-Date).AddSeconds(22); $tries = 0
        try {
            do {
                $g = Game
                $cx = [int]$g.x + [int]$g.w / 2; $cy = [int]$g.y + [int]$g.h / 2
                [Mouse]::Move($cx, $cy)   # a player who goes straight for her
                Start-Sleep -Milliseconds 150
                $g2 = Game
                if ([Math]::Abs([int]$g2.x - [int]$g.x) + [Math]::Abs([int]$g2.y - [int]$g.y) -le 4) {
                    $tries++
                    Send "{`"type`":`"cmd`",`"name`":`"game-click`",`"args`":{`"x`":`"$cx`",`"y`":`"$cy`"}}" | Out-Null
                }
            } while ([int](Game).score -lt 1 -and (Get-Date) -lt $deadline)
            $score = [int](Game).score
            Write-Host "    clicks on a still Sahar: $tries; score $score"
            if ($score -lt 1) { throw 'never caught her in 22 s' }
        }
        finally { Send '{"type":"cmd","name":"game-stop"}' | Out-Null; [Mouse]::Move(5, 5) }
    }
    Step 'hide and seek: she hides at an edge, a click on what shows finds her' {
        Send '{"type":"cmd","name":"game-start","args":{"kind":"HideSeek"}}' | Out-Null
        $deadline = (Get-Date).AddSeconds(4)
        do { Start-Sleep -Milliseconds 200; $g = Game } while ($g.state -ne 'hidden-1' -and (Get-Date) -lt $deadline)
        if ($g.state -ne 'hidden-1') { throw "not hidden: $($g.state)" }
        $x = [int]$g.x; $y = [int]$g.y; $w = [int]$g.w; $h = [int]$g.h
        $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $l = [Math]::Max($x, $work.Left); $t = [Math]::Max($y, $work.Top); $r = [Math]::Min($x + $w, $work.Right); $b = [Math]::Min($y + $h, $work.Bottom)
        $shown = ($r - $l) * ($b - $t) / ($w * $h)
        if ($shown -gt 0.4 -or $shown -lt 0.15) { throw "she shows $([int]($shown * 100))% of herself" }
        Screenshot 'game-hide.png' | Out-Null
        $px = [int](($l + $r) / 2); $py = [int](($t + $b) / 2)
        Send "{`"type`":`"cmd`",`"name`":`"game-click`",`"args`":{`"x`":`"$px`",`"y`":`"$py`"}}" | Out-Null
        if ((Game).state -ne 'found-1') { throw "not found: $((Game).state)" }
        if ((Text64 (Kv 'debug-comment').text64) -ne 'لقيتني! 😆') { throw "found line: '$(Text64 (Kv 'debug-comment').text64)'" }
        # Round 2 with the real mouse: her own input window must not take the click from the game.
        $deadline = (Get-Date).AddSeconds(8)
        do { Start-Sleep -Milliseconds 200; $g = Game } while ($g.state -ne 'hidden-2' -and (Get-Date) -lt $deadline)
        if ($g.state -ne 'hidden-2') { throw "round 2 not hidden: $($g.state)" }
        $x = [int]$g.x; $y = [int]$g.y; $w = [int]$g.w; $h = [int]$g.h
        $l = [Math]::Max($x, 0); $t = [Math]::Max($y, 0); $r = [Math]::Min($x + $w, $work.Right); $b = [Math]::Min($y + $h, [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Bottom)
        $xy = '{0},{1}' -f [int](($l + $r) / 2), [int](($t + $b) / 2)
        & agent-desktop --headed mouse-click --xy $xy | Out-Null
        Start-Sleep -Milliseconds 300
        if ((Game).state -ne 'found-2') { throw "a real click on her did not find her: $((Game).state) (clicked $xy)" }
        Send '{"type":"cmd","name":"game-stop"}' | Out-Null
        if ((Text64 (Kv 'debug-comment').text64) -notmatch '^لقيتني بـ \d+ ثانية بالمعدل') { throw "result: '$(Text64 (Kv 'debug-comment').text64)'" }
    }
    Step 'ball: a flick throws it, she fetches it and brings it back' {
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Ball"}}' | Out-Null
        Start-Sleep -Milliseconds 1500
        $g = Game
        if ($g.ball -ne 'Ready') { throw "ball: $($g.ball)" }
        $bx = [int]$g.ballX; $by = [int]$g.ballY
        Send "{`"type`":`"cmd`",`"name`":`"game-flick`",`"args`":{`"x1`":`"$bx`",`"y1`":`"$by`",`"x2`":`"$($bx + 150)`",`"y2`":`"$($by - 60)`"}}" | Out-Null
        if ((Game).ball -ne 'Flying') { throw "after the flick: $((Game).ball)" }
        Start-Sleep -Milliseconds 600
        Screenshot 'game-ball.png' | Out-Null
        $deadline = (Get-Date).AddSeconds(15)
        do { Start-Sleep -Milliseconds 300; $g = Game } while ($g.score -ne '1' -and (Get-Date) -lt $deadline)
        if ($g.score -ne '1') { throw "no fetch: ball=$($g.ball) state=$($g.state)" }
        Send '{"type":"cmd","name":"game-stop"}' | Out-Null
        if ((Text64 (Kv 'debug-comment').text64) -ne 'جبتلك الطابة مرة وحدة ⚽') { throw "result: '$(Text64 (Kv 'debug-comment').text64)'" }
    }
    Step 'stars: they fall, she follows the pointer along the bottom, the game ends by itself' {
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Stars","seconds":"10"}}' | Out-Null
        $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $end = (Get-Date).AddSeconds(9)
        $shot = $false
        while ((Get-Date) -lt $end) {
            [Mouse]::Move([int]($work.Left + ($work.Width * (0.5 + 0.4 * [Math]::Sin(((Get-Date) - $end).TotalSeconds)))), $work.Bottom - 40)
            Start-Sleep -Milliseconds 150
            if (-not $shot -and [int](Game).stars -ge 3) { Screenshot 'game-stars.png' | Out-Null; $shot = $true }
        }
        $g = Game
        if ([int]$g.y -lt $work.Bottom - 200) { throw "she is not along the bottom (y $($g.y))" }
        $deadline = (Get-Date).AddSeconds(4)
        do { Start-Sleep -Milliseconds 200 } while ((Game).running -eq 'true' -and (Get-Date) -lt $deadline)
        if ((Game).running -ne 'false') { throw 'the stars game did not end by itself' }
        $said = Text64 (Kv 'debug-comment').text64
        if ($said -notmatch 'نجمة') { throw "result: '$said'" }
        Write-Host "    stars result: $said"
    }
    Step 'a game is interrupted cleanly when she has to hide' {
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Chase"}}' | Out-Null
        Send '{"type":"cmd","name":"hide","args":{"minutes":"1"}}' | Out-Null
        Start-Sleep -Milliseconds 1200
        if ((Game).running -ne 'false') { throw 'the game kept running while she hid' }
        Send '{"type":"cmd","name":"show"}' | Out-Null
        Start-Sleep -Seconds 2
        if (-not (State)) { throw 'no state after an interrupted game' }
    }
    Stop-Engine
}

# The shell (Explorer, taskbar tools such as Now Bar, notifications) must never take her for a fullscreen app:
# no Sahar window may contain a whole monitor, and Explorer keeps its taskbar topmost.
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ShellProbe {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    /// Explorer drops WS_EX_TOPMOST from its taskbar while it sees a fullscreen ("rude") window on that monitor.
    public static bool TrayTopmost() { var h = FindWindow("Shell_TrayWnd", null); return h != IntPtr.Zero && ((long)GetWindowLongPtr(h, -20) & 8) != 0; }
    public static int Quns() { int s; return SHQueryUserNotificationState(out s) == 0 ? s : -1; }
    /// "name=l,t,r,b;…" for the visible top-level windows of a process (our own engine: its titles are ours).
    public static string Rects(int pid) {
        var list = new List<string>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) {
                RECT r; GetWindowRect(h, out r);
                var t = new StringBuilder(64); GetWindowText(h, t, 64);
                list.Add(t.ToString().Replace(" ", "_") + "=" + r.L + "," + r.T + "," + r.R + "," + r.B);
            }
            return true;
        }, IntPtr.Zero);
        return string.Join(";", list);
    }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    static IntPtr Find(int pid, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            var t = new StringBuilder(64); GetWindowText(h, t, 64);
            if (p == pid && t.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    /// Moves one of our windows by (dx,dy) from outside, as Windows does after a display change.
    public static bool Nudge(int pid, string title, int dx, int dy) {
        var h = Find(pid, title); RECT r;
        if (h == IntPtr.Zero || !GetWindowRect(h, out r)) return false;
        return SetWindowPos(h, IntPtr.Zero, r.L + dx, r.T + dy, 0, 0, 0x0001 | 0x0004 | 0x0010);   // NOSIZE NOZORDER NOACTIVATE
    }
    public static string RectOf(int pid, string title) {
        var h = Find(pid, title); RECT r;
        return h != IntPtr.Zero && GetWindowRect(h, out r) ? r.L + "," + r.T + "," + r.R + "," + r.B : "";
    }
}
"@

function Assert-NoCover([string]$when) {
    $rects = [ShellProbe]::Rects($script:engine.Id)
    Write-Host "    ${when}: windows $rects"
    foreach ($w in ($rects -split ';' | Where-Object { $_ })) {
        $name, $xy = $w -split '=', 2
        $l, $t, $r, $b = $xy -split ',' | ForEach-Object { [int]$_ }
        foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
            $m = $s.Bounds
            if ($l -le $m.Left -and $t -le $m.Top -and $r -ge $m.Right -and $b -ge $m.Bottom) {
                throw "${when}: window '$name' $l,$t,$r,$b covers monitor $($m.Left),$($m.Top),$($m.Right),$($m.Bottom)"
            }
        }
    }
}

function Tray([string]$when) {
    $t = [ShellProbe]::TrayTopmost()
    Write-Host "    taskbar topmost $t, notification state $([ShellProbe]::Quns()) ($when)"
    $t
}

# Both checks, both printed: the taskbar's topmost (when asked) and no window over a whole monitor.
function Assert-Shell([string]$when, [switch]$Tray) {
    $errors = @()
    if ($Tray) {
        $t = Tray $when
        if ($t -ne $script:trayBefore) { $errors += "the taskbar's topmost changed ($($script:trayBefore) -> $t): the shell took her for a fullscreen app" }
    }
    try { Assert-NoCover $when } catch { $errors += "$_" }
    if ($errors.Count) { throw "${when}: $($errors -join '; ')" }
}

function Overlay { Kv 'debug-overlay' }

# The overlay holds her body (screen pixels), e.g. after she hid and came back.
function Assert-OverlayHolds([string]$when) {
    $o = Overlay; $st = State
    Write-Host "    ${when}: overlay $($o.x),$($o.y) $($o.w)x$($o.h) (needs $($o.needs)); body $($st.x),$($st.y) $($st.width)x$($st.height)"
    if ($st.x -lt [int]$o.x -or $st.y -lt [int]$o.y -or $st.x + $st.width -gt [int]$o.x + [int]$o.w -or $st.y + $st.height -gt [int]$o.y + [int]$o.h) {
        throw "${when}: her body is outside the overlay"
    }
}

# Something of hers is drawn around (x,y) (not clipped away): of five points (the centre and +-d), at least one changed
# between two screenshots. Five, because a white button over a white window, or the ball's black middle on a dark
# desktop, can match what was there before at a single point.
function Assert-Drawn([string]$before, [string]$after, [int]$x, [int]$y, [string]$what, [int]$d = 8) {
    $a = [System.Drawing.Bitmap]::new($before); $b = [System.Drawing.Bitmap]::new($after)
    try {
        $seen = @()
        foreach ($p in @(@(0, 0), @(-$d, 0), @($d, 0), @(0, -$d), @(0, $d))) {
            $pa = $a.GetPixel($x + $p[0], $y + $p[1]); $pb = $b.GetPixel($x + $p[0], $y + $p[1])
            $diff = [math]::Abs($pa.R - $pb.R) + [math]::Abs($pa.G - $pb.G) + [math]::Abs($pa.B - $pb.B)
            $seen += "$($pa.R),$($pa.G),$($pa.B)->$($pb.R),$($pb.G),$($pb.B)"
            if ($diff -ge 60) { Write-Host "    $what drawn near ($x,$y)"; return }
        }
        throw "$what is not drawn near ($x,$y) ($($seen -join ' ')): clipped by the overlay?"
    }
    finally { $a.Dispose(); $b.Dispose() }
}

function Stage-Shell {
    $script:trayBefore = $null
    Step 'a normal window in front; the taskbar before Sahar' {
        $script:win = Start-TestWindow 'typing'
        Start-Sleep -Seconds 1
        $script:trayBefore = Tray 'before Sahar'
    }
    Step 'at rest no Sahar window covers a monitor and the taskbar stays topmost' {
        Start-Engine
        Start-Sleep -Milliseconds 800
        & agent-desktop --headed mouse-click --xy '430,320' 2>&1 | Out-Null   # the test window in front again
        Start-Sleep -Milliseconds 1500
        Assert-Shell 'at rest' -Tray
    }
    Step 'at rest the overlay is her size and never moves' {
        $o = Overlay
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        if ([int]$o.w * [int]$o.h -ge $screen.Width * $screen.Height / 2) { throw "overlay $($o.w)x$($o.h) is not her size" }
        Start-Sleep -Seconds 3
        $o2 = Overlay
        Write-Host "    moves $($o.moves) -> $($o2.moves), resizes $($o.resizes) -> $($o2.resizes)"
        if ($o2.moves -ne $o.moves -or $o2.resizes -ne $o.resizes) { throw 'the overlay moved while she rested' }
        Assert-OverlayHolds 'at rest'
    }
    Step 'talking: no window covers a monitor' {
        $t64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes('مرحبا 🌙'))
        $before = Screenshot 'shell-before-talk.png'
        Send ('{"type":"cmd","name":"say","args":{"text64":"' + $t64 + '"}}') | Out-Null
        Start-Sleep -Milliseconds 600
        $after = Screenshot 'shell-talk.png'
        Assert-NoCover 'talking'
        # Her bubble: above her body, white; its left part has no text (right-to-left).
        $st = State
        Assert-Drawn $before $after ([int]($st.x + $st.width / 2 - 95)) ([int]($st.y - 30)) 'her speech bubble'
    }
    Step 'menu open: no window covers a monitor and the taskbar stays topmost' {
        Start-Sleep -Seconds 8   # her bubble has gone
        $before = Screenshot 'shell-before-menu.png'
        $c = Body-Centre
        [Mouse]::Click($c[0], $c[1], $true)
        $m = Wait-Menu 'true' 2000 'menu open after a right-click'
        [Mouse]::Move(5, 5)
        Start-Sleep -Milliseconds 1200
        $after = Screenshot 'shell-menu.png'
        try {
            Assert-Shell 'menu open' -Tray
            foreach ($b in $m.buttons) { Assert-Drawn $before $after ([int]$b.x) ([int]$b.y) "menu button $($b.id)" ([int]($b.r / 2)) }
        }
        finally { Send '{"type":"cmd","name":"menu-close"}' | Out-Null }
    }
    Step 'a game: no window covers a monitor and the taskbar stays topmost' {
        & agent-desktop --headed mouse-click --xy '430,320' 2>&1 | Out-Null
        Start-Sleep -Milliseconds 500
        $before = Screenshot 'shell-before-game.png'
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Ball","seconds":"20"}}' | Out-Null
        Start-Sleep -Milliseconds 800
        try {
            # The ball falls to the bottom of the screen first: wait until it rests, then look.
            $g = Game; $deadline = (Get-Date).AddSeconds(4)
            do { Start-Sleep -Milliseconds 300; $p = $g; $g = Game } while (($g.ballY -ne $p.ballY -or $g.ballX -ne $p.ballX) -and (Get-Date) -lt $deadline)
            $after = Screenshot 'shell-game.png'
            Assert-Shell 'game' -Tray
            # The ball rests near the pointer, far from her: drawn only if the overlay grew to the game's screen.
            # The glyph sits low in its sprite (text box), so look up to 30 px around its logical centre.
            Assert-Drawn $before $after ([int]$g.ballX + 15) ([int]$g.ballY + 15) "the ball ($($g.ball))" 30
        }
        finally { Send '{"type":"cmd","name":"game-stop"}' | Out-Null }
    }
    Step 'a game click on the outermost pixels (left, top, right) is still the game''s' {
        # The catcher stops short only on the taskbar's side: a click on any other screen edge must not reach the app
        # or desktop underneath (that would activate it and end the game) — e.g. hide-and-seek spots at the edges.
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Chase","seconds":"30"}}' | Out-Null
        Start-Sleep -Milliseconds 800
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $lost = @()
        try {
            foreach ($p in @(@(($screen.Right - 1), 300, 'right'), @($screen.Left, 300, 'left'), @(500, $screen.Top, 'top'))) {
                [Mouse]::Click($p[0], $p[1], $false)
                Start-Sleep -Milliseconds 700
                $g = Game
                Write-Host "    click at the $($p[2]) edge ($($p[0]),$($p[1])): game running $($g.running)"
                if ($g.running -ne 'true') {
                    $lost += "$($p[2]) edge ($($p[0]),$($p[1]))"
                    Send '{"type":"cmd","name":"game-start","args":{"kind":"Chase","seconds":"30"}}' | Out-Null
                    Start-Sleep -Milliseconds 800
                }
            }
        }
        finally { Send '{"type":"cmd","name":"game-stop"}' | Out-Null }
        if ($lost.Count) { throw "a click went past the game at the $($lost -join ', ')" }
    }
    Step 'her showcase and the butterfly: no window covers a monitor' {
        Start-Sleep -Milliseconds 800
        Send '{"type":"cmd","name":"showcase"}' | Out-Null
        Start-Sleep -Milliseconds 1500
        try { Assert-NoCover 'showcase' } finally { Send '{"type":"cmd","name":"showcase-stop"}' | Out-Null }
        Start-Sleep -Milliseconds 1500
        Send '{"type":"cmd","name":"motion","args":{"kind":"ChaseButterfly"}}' | Out-Null
        Start-Sleep -Milliseconds 1500
        Assert-NoCover 'butterfly'
    }
    Step 'after she hid and came back, and after a display change, the overlay holds her' {
        Start-Sleep -Seconds 6   # the butterfly has flown off
        Send '{"type":"cmd","name":"hide","args":{"minutes":"1"}}' | Out-Null
        Wait-State { param($st) $st.hidden } 2000 'hidden' | Out-Null
        Start-Sleep -Seconds 2
        Send '{"type":"cmd","name":"show"}' | Out-Null
        Wait-State { param($st) -not $st.hidden } 3000 'shown again' | Out-Null
        Start-Sleep -Seconds 2
        Assert-OverlayHolds 'after hide/show'
        Assert-NoCover 'after hide/show'
        # Windows may move the overlay itself around a display change: the stage must plan from where it really is.
        if (-not [ShellProbe]::Nudge($script:engine.Id, 'Sahar', 37, 23)) { throw 'could not move the overlay' }
        Send '{"type":"cmd","name":"debug-display-changed"}' | Out-Null
        Start-Sleep -Seconds 1
        Assert-OverlayHolds 'after a display change'
        $o = Overlay; $real = [ShellProbe]::RectOf($script:engine.Id, 'Sahar')
        $said = "$($o.x),$($o.y),$([int]$o.x + [int]$o.w),$([int]$o.y + [int]$o.h)"
        Write-Host "    overlay after an outside move and a display change: real $real, planned $said"
        if ($real -ne $said) { throw "the overlay is at $real but the stage thinks $said (she would be drawn offset)" }
        if ((Overlay).needs -ne 'none') { throw "needs left behind: $((Overlay).needs)" }
    }
    Stop-Engine
    if ($script:win -and -not $script:win.HasExited) { $script:win.Kill() }
}

# The owner's glitch reports on 0.10.1 (0.11): two faces at once, things in the menu flashing in the wrong place, her
# drawing jumping while she moves. Burst grabs whole frames at screen rate in C# (PowerShell loops reach ~17 fps) and can
# act at a moment: a left or right click, or a drag that moves the cursor a fixed step per frame.
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public sealed class Frame { public long Ms; public int CursorX; public byte[] Px; public int W, H; }
public static class Burst {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, byte[] bmi, uint usage, out IntPtr bits, IntPtr sec, uint off);
    /// act: 0 none, 1 left click, 2 right click, 3 drag (press at ax,ay at actMs, then stepPx right per frame, release at the end).
    /// Frames are 32-bit BGRX, top-down (GDI BitBlt of the screen: what DWM composed).
    public static List<Frame> Run(int x, int y, int w, int h, int totalMs, int actMs, int ax, int ay, int act, int stepPx) {
        var bmi = new byte[40];
        BitConverter.GetBytes(40).CopyTo(bmi, 0); BitConverter.GetBytes(w).CopyTo(bmi, 4); BitConverter.GetBytes(-h).CopyTo(bmi, 8);
        BitConverter.GetBytes((short)1).CopyTo(bmi, 12); BitConverter.GetBytes((short)32).CopyTo(bmi, 14);
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bits;
        IntPtr dib = CreateDIBSection(screen, bmi, 0, out bits, IntPtr.Zero, 0), old = SelectObject(mem, dib);
        var list = new List<Frame>(); var clock = System.Diagnostics.Stopwatch.StartNew(); bool acted = false; int cx = ax;
        try {
            while (clock.ElapsedMilliseconds < totalMs) {
                if (!acted && clock.ElapsedMilliseconds >= actMs) {
                    acted = true;
                    if (act != 0) { SetCursorPos(ax, ay); System.Threading.Thread.Sleep(30); }
                    if (act == 1 || act == 3) mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                    if (act == 2) mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero);
                    if (act != 0) System.Threading.Thread.Sleep(20);
                    if (act == 1) mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                    if (act == 2) mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
                }
                if (acted && act == 3) { cx += stepPx; SetCursorPos(cx, ay); }
                BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
                var px = new byte[w * h * 4]; Marshal.Copy(bits, px, 0, px.Length);
                list.Add(new Frame { Ms = clock.ElapsedMilliseconds - actMs, CursorX = cx, Px = px, W = w, H = h });
            }
        } finally {
            if (act == 3) mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            SelectObject(mem, old); DeleteObject(dib); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
        return list;
    }
    /// The same on a worker thread, so the script can send a pipe command while frames are grabbed.
    public static System.Threading.Tasks.Task<List<Frame>> Start(int x, int y, int w, int h, int totalMs) {
        return System.Threading.Tasks.Task.Run(() => Run(x, y, w, h, totalMs, 0, 0, 0, 0, 0));
    }
}
public static class FaceProbe {
    static int L(byte[] p, int i) { return (int)(0.114 * p[i] + 0.587 * p[i + 1] + 0.299 * p[i + 2]); }
    /// Faint eye ink (navy mixed with her white body) with no solid ink within r px — a face fading in or out over the
    /// other — on rows where her white body is on both sides of the eye columns (the halo, leaf and backdrop never count).
    public static int Faint(byte[] p, int w, int h, int x0, int x1, int bodyL, int bodyR, int solid, int faintHi, int r) {
        int n = 0;
        for (int y = r; y < h - r; y++) {
            bool left = false, right = false;
            for (int x = bodyL; x < x0 && !left; x++) if (L(p, (y * w + x) * 4) >= 215) left = true;
            for (int x = x1; x < bodyR && !right; x++) if (L(p, (y * w + x) * 4) >= 215) right = true;
            if (!left || !right) continue;
            for (int x = x0; x < x1; x++) {
                int i = (y * w + x) * 4, l = L(p, i);
                if (l < solid || l >= faintHi) continue;
                int b = p[i], g = p[i + 1], rr = p[i + 2];
                if (g > rr + 25 && g > b + 10) continue;   // the leaf
                if (rr > b + 25) continue;                 // blush, warm washes
                bool near = false;
                for (int dy = -r; dy <= r && !near; dy++)
                    for (int dx = -r; dx <= r && !near; dx++) {
                        int xx = x + dx; if (xx < 0 || xx >= w) continue;
                        if (L(p, ((y + dy) * w + xx) * 4) < solid) near = true;
                    }
                if (!near) n++;
            }
        }
        return n;
    }
    /// Sharp rises into her white body down one column: one outline gives 1, a second (translucent) body 2.
    public static int Edges(byte[] p, int w, int h, int x, int yMax, int jump) {
        int n = 0;
        for (int y = 0; y + 3 < yMax && y + 15 < h; y++) {
            if (L(p, ((y + 3) * w + x) * 4) - L(p, (y * w + x) * 4) < jump) continue;
            bool run = true;
            for (int k = 3; k < 15 && run; k++) if (L(p, ((y + k) * w + x) * 4) < 215) run = false;
            if (run) { n++; y += 15; }
        }
        return n;
    }
    /// Pixels of a frame darker or lighter (by margin) than both the settled picture before and the one after, each
    /// taken over a (2 tol + 1)^2 neighbourhood: something drawn where neither picture has it (a button or label at a
    /// stale place, a second copy, her body displaced), while scales and fades between the two never count.
    public static int[][] Range(byte[] before, byte[] after, int w, int h, int tol) {
        var lo = new int[w * h]; var hi = new int[w * h];
        for (int i = 0; i < w * h; i++) { int a = L(before, i * 4), b = L(after, i * 4); lo[i] = Math.Min(a, b); hi[i] = Math.Max(a, b); }
        return new[] { Min(lo, w, h, tol), Max(hi, w, h, tol) };
    }
    public static int Beyond(byte[] f, int[][] range, int w, int h, int margin) {
        int[] lo = range[0], hi = range[1];
        int n = 0;
        for (int i = 0; i < w * h; i++) { int l = L(f, i * 4); if (l > hi[i] + margin || l < lo[i] - margin) n++; }
        return n;
    }
    static int[] Min(int[] v, int w, int h, int r) { return Pass(Pass(v, w, h, r, true, true), w, h, r, false, true); }
    static int[] Max(int[] v, int w, int h, int r) { return Pass(Pass(v, w, h, r, true, false), w, h, r, false, false); }
    static int[] Pass(int[] v, int w, int h, int r, bool horiz, bool min) {
        var o = new int[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
            int best = min ? 999 : -1;
            for (int k = -r; k <= r; k++) {
                int xx = horiz ? x + k : x, yy = horiz ? y : y + k;
                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                int q = v[yy * w + xx]; best = min ? Math.Min(best, q) : Math.Max(best, q);
            }
            o[y * w + x] = best;
        }
        return o;
    }
    /// Her body's place: the centre of her white body pixels (over the slate-blue test window), "x,y,count".
    public static string Centroid(byte[] p, int w, int h) {
        long sx = 0, sy = 0, n = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (L(p, (y * w + x) * 4) >= 225) { sx += x; sy += y; n++; }
        return n == 0 ? "0,0,0" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2}", sx / (double)n, sy / (double)n, n);
    }
}
"@

function Save-Frames($frames, [string]$dir, [int]$every, [int]$max) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $tiles = @(); $k = 0
    foreach ($f in $frames) {
        if ($k % $every -eq 0 -and $tiles.Count -lt $max) {
            $path = Join-Path $dir ('{0:D3}-t{1}ms.png' -f $k, $f.Ms)
            $bmp = [System.Drawing.Bitmap]::new($f.W, $f.H, [System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
            $d = $bmp.LockBits([System.Drawing.Rectangle]::new(0, 0, $f.W, $f.H), [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppRgb)
            [System.Runtime.InteropServices.Marshal]::Copy($f.Px, 0, $d.Scan0, $f.Px.Length); $bmp.UnlockBits($d)
            $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $tiles += $path
        }
        $k++
    }
    $tiles
}
function Dispose-Frames($frames) { }

# Smooth motion: each body position must lie where its neighbours put it (time-interpolated), within 3 px plus a quarter
# of the move across the three frames: a frame drawn at a stale window/content place is off by a whole step and back.
function Assert-Smooth($frames, [string]$what) {
    $pts = @(foreach ($f in $frames) { $c = ([FaceProbe]::Centroid($f.Px, $f.W, $f.H)) -split ','; [pscustomobject]@{ t = $f.Ms; x = [double]$c[0]; y = [double]$c[1]; n = [int]$c[2] } })
    $bad = @(); $path = @()
    for ($i = 1; $i -lt $pts.Count - 1; $i++) {
        $a = $pts[$i - 1]; $b = $pts[$i]; $c = $pts[$i + 1]
        if ($a.n -lt 200 -or $b.n -lt 200 -or $c.n -lt 200 -or $c.t -eq $a.t) { continue }
        $k = ($b.t - $a.t) / ($c.t - $a.t)
        $px = $a.x + ($c.x - $a.x) * $k; $py = $a.y + ($c.y - $a.y) * $k
        $dev = [math]::Sqrt([math]::Pow($b.x - $px, 2) + [math]::Pow($b.y - $py, 2))
        $span = [math]::Sqrt([math]::Pow($c.x - $a.x, 2) + [math]::Pow($c.y - $a.y, 2))
        $path += ('{0}:{1:0},{2:0}' -f $b.t, $b.x, $b.y)
        if ($dev -gt 3 + 0.25 * $span) { $bad += ('{0}: frame at +{1} ms is {2:0.0} px off its path (move {3:0.0} px)' -f $what, $b.t, $dev, $span) }
    }
    Write-Host "    ${what}: $($pts.Count) frames; t:x,y = $($path -join ' ')"
    $bad
}

function Stage-Face {
    Step 'start engine on a plain window' {
        Start-Engine
        $script:faceWin = Start-TestWindow 'maximized'
    }
    Step 'a tap: one face at a time, one outline (60 fps burst)' {
        # 240 DIP: 1.5 px per rig unit, so an eye is ~27 x 33 px. Canvas = body rect grown by the rig's 70/88 units.
        $r = Send '{"type":"cmd","name":"debug-size","args":{"dip":"240"}}'
        if (-not $r.ok) { throw "debug-size not ok: $($r.error)" }
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Pose-Body 392 262 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 2
        $st = State
        $s = $st.width / 160.0
        $cl = [int]($st.x - 70 * $s); $ct = [int]($st.y - 88 * $s)
        $gx = $cl; $gy = [math]::Max(0, $ct - 130); $gw = [int](300 * $s); $gh = [int](300 * $s) + ($ct - $gy)
        $u = { param($units) [int]($units * $s) }
        $x0 = & $u 116; $x1 = & $u 184; $bl = & $u 74; $br = & $u 226
        $colA = & $u 95; $colB = & $u 205; $yMax = ($ct - $gy) + (& $u 170)
        $frames = [Burst]::Run($gx, $gy, $gw, $gh, 2000, 300, [int]($st.x + $st.width / 2), [int]($st.y + $st.height * 0.62), 1, 0)
        [Mouse]::Move(5, 5)
        $n = $frames.Count; $fps = [int]($n / 2.0)
        $report = @(); $bad = @(); $base = 0
        foreach ($f in $frames) {
            $faint = [FaceProbe]::Faint($f.Px, $gw, $gh, $x0, $x1, $bl, $br, 110, 195, 2)
            $ea = [FaceProbe]::Edges($f.Px, $gw, $gh, $colA, $yMax, 60); $eb = [FaceProbe]::Edges($f.Px, $gw, $gh, $colB, $yMax, 60)
            $report += "$($f.Ms):$faint/$ea$eb"
            if ($f.Ms -lt 0) { $base = [math]::Max($base, $faint); continue }
            if ($faint -gt $base + 150) { $bad += "two faces at +$($f.Ms) ms (faint eye ink $faint px, at rest $base)" }
            if ($ea -ge 2 -and $eb -ge 2) { $bad += "two outlines at +$($f.Ms) ms" }
        }
        $tiles = Save-Frames ($frames | Where-Object { $_.Ms -ge -40 }) (Join-Path $Out 'face-tap') ([math]::Max(1, [int]($n / 36))) 36
        Contact-Sheet $tiles 6 240 (Join-Path $Out 'tap-strip.png')
        Dispose-Frames $frames
        Write-Host "    $n frames ($fps fps); baseline faint $base; t:faint/edgesAB = $($report -join ' ')"
        if ($fps -lt 18) { $bad += "only $fps fps" }
        Send '{"type":"cmd","name":"debug-size","args":{"dip":"80"}}' | Out-Null
        if ($bad.Count) { throw ($bad -join '; ') }
    }
    Step 'the menu opens with everything in its place (60 fps burst)' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Pose-Body 470 330 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 2
        $st = State
        $cx = [int]($st.x + $st.width / 2); $cy = [int]($st.y + $st.height / 2)
        $gx = [math]::Max(0, $cx - 330); $gy = [math]::Max(0, $cy - 300); $gw = [math]::Min(660, $Work.Right - $gx); $gh = [math]::Min(600, $Work.Bottom - $gy)
        $frames = [Burst]::Run($gx, $gy, $gw, $gh, 1500, 250, $cx, $cy, 2, 0)
        $m = Wait-Menu 'true' 1000 'menu open after the right-click'
        $before = ($frames | Where-Object { $_.Ms -lt 0 } | Select-Object -Last 1).Px
        $after = $frames[-1].Px
        $range = [FaceProbe]::Range($before, $after, $gw, $gh, 10)
        $report = @(); $bad = @()
        foreach ($f in $frames) {
            if ($f.Ms -lt 0) { continue }
            $x = [FaceProbe]::Beyond($f.Px, $range, $gw, $gh, 60)
            $report += "$($f.Ms):$x"
            if ($x -gt 40) { $bad += "+$($f.Ms) ms: $x px drawn where neither the closed nor the open menu has them" }
        }
        $tiles = Save-Frames ($frames | Where-Object { $_.Ms -ge -20 }) (Join-Path $Out 'menu-open') 1 36
        Contact-Sheet $tiles 6 240 (Join-Path $Out 'menu-open-strip.png')
        Write-Host "    $($frames.Count) frames; t:beyond = $($report -join ' ')"
        Dispose-Frames $frames
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Start-Sleep -Milliseconds 600
        if ($bad.Count) { throw ($bad -join '; ') }
    }
    Step 'a game starts with her in her place (60 fps burst)' {
        # Her body only (below her leaf, where the game's speech bubble never is): every frame is her before or after.
        Pose-Body 470 330 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 1
        $st = State
        $gx = [int]$st.x - 40; $gy = [int]$st.y + 10; $gw = [int]$st.width + 80; $gh = [int]$st.height + 40
        $burst = [Burst]::Start($gx, $gy, $gw, $gh, 1200)
        Start-Sleep -Milliseconds 250
        Send '{"type":"cmd","name":"game-start","args":{"kind":"Chase","seconds":"30"}}' | Out-Null
        $frames = $burst.Result
        # 12 px of tolerance: the game's squish and her new face stay inside it; a stale window offset (hundreds of px) does not.
        $range = [FaceProbe]::Range($frames[0].Px, $frames[-1].Px, $gw, $gh, 12)
        $report = @(); $bad = @()
        foreach ($f in $frames) {
            $x = [FaceProbe]::Beyond($f.Px, $range, $gw, $gh, 60)
            $report += "$($f.Ms):$x"
            if ($x -gt 150) { $bad += "+$($f.Ms) ms: her body drawn off its place ($x px)" }   # her squish and new face: < 100
        }
        Write-Host "    $($frames.Count) frames; t:beyond = $($report -join ' ')"
        $tiles = Save-Frames $frames (Join-Path $Out 'game-start') 2 36
        Contact-Sheet $tiles 6 200 (Join-Path $Out 'game-start-strip.png')
        Send '{"type":"cmd","name":"game-stop"}' | Out-Null
        Start-Sleep -Seconds 2
        if ($bad.Count) { throw ($bad -join '; ') }
    }
    Step 'a drag moves her smoothly (60 fps burst)' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Pose-Body 200 330 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 1
        $st = State
        $gx = [int]$st.x - 120; $gy = [int]$st.y - 160; $gw = 760; $gh = [int]$st.height + 320
        $frames = [Burst]::Run($gx, $gy, $gw, $gh, 1600, 200, [int]($st.x + $st.width / 2), [int]($st.y + $st.height / 2), 3, 5)
        $bad = Assert-Smooth ($frames | Where-Object { $_.Ms -ge 300 }) 'drag'
        $tiles = Save-Frames $frames (Join-Path $Out 'drag') 3 36
        Contact-Sheet $tiles 6 200 (Join-Path $Out 'drag-strip.png')
        Dispose-Frames $frames
        Start-Sleep -Seconds 2
        if ($bad.Count) { throw ($bad -join '; ') }
    }
    Step 'a flight moves her smoothly (60 fps burst)' {
        Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
        Pose-Body 470 330 | Out-Null
        [Mouse]::Move(5, 5)
        Start-Sleep -Seconds 1
        $deadline = (Get-Date).AddSeconds(8)   # the game's last line goes first (a white bubble moves the centroid)
        while ((Kv 'debug-comment').showing -eq 'true' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
        # Below the test window's white title bar (the centroid counts white pixels).
        $gx = [math]::Max(0, $Work.Left); $gy = $Work.Top + 40; $gw = $Work.Width; $gh = $Work.Height - 40
        $burst = [Burst]::Start($gx, $gy, $gw, $gh, 2500)
        Start-Sleep -Milliseconds 200
        Send '{"type":"cmd","name":"motion","args":{"kind":"Wander","flight":"Zigzag"}}' | Out-Null
        $frames = $burst.Result
        $bad = Assert-Smooth $frames 'flight'
        $tiles = Save-Frames $frames (Join-Path $Out 'flight') 4 36
        Contact-Sheet $tiles 6 200 (Join-Path $Out 'flight-strip.png')
        Dispose-Frames $frames
        if ($bad.Count) { throw ($bad -join '; ') }
    }
    Stop-Engine
    if ($script:faceWin -and -not $script:faceWin.HasExited) { $script:faceWin.Kill() }
    [Mouse]::Move([int]($Work.Width / 2), [int]($Work.Height / 2))   # the next stage starts with the pointer where a fresh runner has it
}

# ---- installer + update (owner roadmap: installer + auto-update) ----
# The real Sahar-Setup-<version>.exe on this runner: install, upgrade over a running Sahar, start-with-Windows on/off,
# the in-app update against a local fake of the GitHub API (python's static server on 127.0.0.1; never GitHub), and
# uninstall. The runner's profile is throwaway, so it installs where a user's would: %LocalAppData%\Programs\Sahar.
$AppId = '{B24750DD-9A27-4894-8A97-D099CBA9B19C}'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Sahar'
$InstalledExe = Join-Path $InstallDir 'Sahar.Engine.exe'
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${AppId}_is1"
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$StartMenuLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\سحر.lnk'
$Roaming = Join-Path $env:APPDATA 'Sahar'
$LocalData = Join-Path $env:LOCALAPPDATA 'Sahar'
$EngineLog = Join-Path $LocalData 'engine.log'

function Sahars { @(Get-Process Sahar.Engine -ErrorAction SilentlyContinue) }

function Run-Setup([string[]]$Extra = @(), [string]$Exe = $script:setupExe, [string]$Name = 'setup') {
    $log = Join-Path $Out "$Name.log"
    $p = Start-Process -FilePath $Exe -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/LOG=`"$log`"") + $Extra) -PassThru
    # The process only (Start-Process -Wait could wait for the Sahar it starts).
    if (-not $p.WaitForExit(180000)) { throw "$Name did not finish in 3 minutes" }
    if ($p.ExitCode -ne 0) { throw "$Name exit code $($p.ExitCode) (see $Name.log)" }
}

function Wait-Sahar([string]$what, [int]$notPid = 0, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $p = Sahars | Where-Object { $_.Id -ne $notPid -and $_.Path -eq $InstalledExe } | Select-Object -First 1
        if ($p -and (Test-Path '\\.\pipe\sahar')) {
            if ((Send '{"type":"cmd","name":"state"}').ok) { return $p }
        }
        Start-Sleep -Milliseconds 300
    }
    throw "${what}: no Sahar running from $InstalledExe within $seconds s"
}

function Wait-Gone([int]$id, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $id -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 200
    }
    throw "${what}: process $id still running after $seconds s"
}

function Log-Since([int]$lines) { (Get-Content $EngineLog -Encoding utf8 | Select-Object -Skip $lines) -join "`n" }
function Log-Lines { if (Test-Path $EngineLog) { @(Get-Content $EngineLog -Encoding utf8).Count } else { 0 } }

function Assert-DataKept([string]$when) {
    foreach ($m in (Join-Path $Roaming 'install-marker.txt'), (Join-Path $LocalData 'install-marker.txt')) {
        if (-not (Test-Path $m)) { throw "${when}: $m is gone" }
    }
    $s = Get-Content (Join-Path $Roaming 'settings.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if (-not $s.helloDone -or $s.updateCheckEnabled -ne $false -or $s.activity -ne 'Calm') { throw "${when}: settings changed: $($s | ConvertTo-Json -Compress)" }
    if (-not (Test-Path $EngineLog)) { throw "${when}: engine.log is gone" }
}

function Update-State { Kv 'debug-update' }

function Wait-Update([scriptblock]$cond, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $u = Update-State
        if (& $cond $u) { return $u }
        Start-Sleep -Milliseconds 300
    }
    throw "$what (last: $(($u.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '))"
}

function Stage-Install {
    $version = (Select-Xml -Path "$PSScriptRoot\..\..\Directory.Build.props" -XPath '//Version').Node.InnerText
    $script:setupExe = Join-Path (Resolve-Path $Setup).Path "Sahar-Setup-$version.exe"
    Stop-Engine
    $backup = Join-Path $Out 'settings-before-install.json'
    $settingsPath = Join-Path $Roaming 'settings.json'

    Step 'the setup and its checksum were built' {
        if (-not (Test-Path $script:setupExe)) { throw "no $script:setupExe" }
        $line = Get-Content "$script:setupExe.sha256" -Raw
        $hash = (Get-FileHash $script:setupExe -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($line -ne "$hash  Sahar-Setup-$version.exe`n") { throw "checksum file says '$line', the setup is $hash" }
        if (Sahars) { throw "a Sahar is already running before the install" }
    }

    Step 'seed settings (no automatic update check: CI never reaches GitHub) and markers' {
        New-Item -ItemType Directory -Force -Path $Roaming, $LocalData | Out-Null
        if (Test-Path $settingsPath) { Copy-Item $settingsPath $backup -Force }
        Set-Content $settingsPath '{"version":1,"helloDone":true,"updateCheckEnabled":false,"activity":"Calm"}' -Encoding utf8NoBOM
        Set-Content (Join-Path $Roaming 'data-version') '2' -NoNewline
        Set-Content (Join-Path $Roaming 'install-marker.txt') 'roaming'
        Set-Content (Join-Path $LocalData 'install-marker.txt') 'local'
    }

    Step 'silent install: files, Start-menu shortcut, uninstall entry, start with Windows, and she starts' {
        Run-Setup -Name 'setup-install'
        $script:installed = Wait-Sahar 'after the install'
        foreach ($f in 'Sahar.Engine.exe', 'README-test-build.txt', 'unins000.exe') {
            if (-not (Test-Path (Join-Path $InstallDir $f))) { throw "$f not in $InstallDir" }
        }
        if (-not (Test-Path -LiteralPath $StartMenuLink)) { throw "no Start-menu shortcut $StartMenuLink" }
        # Shell.Application reads the link as Unicode (WScript.Shell's CreateShortcut gives '' for an Arabic name).
        $folder = (New-Object -ComObject Shell.Application).NameSpace((Split-Path $StartMenuLink))
        $target = $folder.ParseName((Split-Path $StartMenuLink -Leaf)).GetLink.Path
        if ($target -ne $InstalledExe) { throw "the shortcut points at '$target'" }
        $u = Get-ItemProperty $UninstallKey
        Write-Host "    uninstall entry: $($u.DisplayName) $($u.DisplayVersion) at $($u.InstallLocation)"
        if ($u.DisplayVersion -ne $version -or $u.DisplayName -notmatch 'Sahar' -or $u.InstallLocation.TrimEnd('\') -ne $InstallDir) { throw 'uninstall entry is wrong' }
        if (-not $u.UninstallString) { throw 'no UninstallString' }
        $run = (Get-ItemProperty $RunKey -ErrorAction SilentlyContinue).Sahar
        if ($run -ne "`"$InstalledExe`"") { throw "Run value is '$run'" }
        Assert-DataKept 'after the install'
    }

    Step 'upgrade over a running Sahar: she is asked to quit (no kill), comes back, settings and log kept' {
        $old = (Wait-Sahar 'before the upgrade').Id
        $before = Log-Lines
        Run-Setup -Name 'setup-upgrade'
        Wait-Gone $old 5 'the old Sahar'
        $script:installed = Wait-Sahar 'after the upgrade' -notPid $old
        $since = Log-Since $before
        if ($since -notmatch '(?m) exit 0\s*$') { throw "the old run did not end with exit 0:`n$since" }
        if ($since -match 'previous run ended without exit') { throw "the old run was cut short:`n$since" }
        Assert-DataKept 'after the upgrade'
    }

    Step 'start with Windows off removes the Run value; on brings it back' {
        Run-Setup @('/MERGETASKS="!startup"') -Name 'setup-startup-off'
        if ($null -ne (Get-ItemProperty $RunKey -ErrorAction SilentlyContinue).Sahar) { throw 'Run value still there with the task off' }
        Run-Setup @('/MERGETASKS="startup"') -Name 'setup-startup-on'
        if ((Get-ItemProperty $RunKey -ErrorAction SilentlyContinue).Sahar -ne "`"$InstalledExe`"") { throw 'Run value not back with the task on' }
        $script:installed = Wait-Sahar 'after the task changes'
    }

    Step '--quit ends a running Sahar gracefully (exit 0)' {
        $id = $script:installed.Id
        $before = Log-Lines
        $q = Start-Process -FilePath $InstalledExe -ArgumentList '--quit' -PassThru
        if (-not $q.WaitForExit(30000)) { throw '--quit did not return' }
        if ($q.ExitCode -ne 0) { throw "--quit exit code $($q.ExitCode)" }
        Wait-Gone $id 3 'after --quit'
        if ((Log-Since $before) -notmatch '(?m) exit 0\s*$') { throw 'no exit 0 in the log after --quit' }
        $q2 = Start-Process -FilePath $InstalledExe -ArgumentList '--quit' -PassThru
        if (-not $q2.WaitForExit(10000) -or $q2.ExitCode -ne 0) { throw '--quit with no Sahar running is not exit 0' }
    }

    # The fake GitHub API: the release JSON at its API path, a "newer" setup (this one renamed) and its .sha256.
    $port = Get-Random -Minimum 41000 -Maximum 49000
    $api = "http://127.0.0.1:$port"
    $fake = Join-Path $Out 'fake-api'
    $newName = 'Sahar-Setup-99.0.0.exe'
    $good = (Get-FileHash $script:setupExe -Algorithm SHA256).Hash.ToLowerInvariant()
    Step 'fake API up (python http.server on 127.0.0.1)' {
        $tags = Join-Path $fake 'repos\A7ma-B25\sahar\releases\tags'
        New-Item -ItemType Directory -Force -Path $tags | Out-Null
        Copy-Item $script:setupExe (Join-Path $fake $newName) -Force
        Set-Content (Join-Path $fake "$newName.sha256") ("0" * 64 + "  $newName`n") -NoNewline -Encoding ascii   # wrong at first
        $size = (Get-Item $script:setupExe).Length
        $json = '{"tag_name":"test-build","draft":false,"prerelease":true,"assets":[' +
            '{"name":"Sahar-99.0.0-test-win-x64.zip","browser_download_url":"' + $api + '/nothing.zip","size":1},' +
            '{"name":"' + $newName + '","browser_download_url":"' + $api + '/' + $newName + '","size":' + $size + '},' +
            '{"name":"' + $newName + '.sha256","browser_download_url":"' + $api + '/' + $newName + '.sha256","size":90}]}'
        Set-Content (Join-Path $tags 'test-build') $json -NoNewline -Encoding utf8NoBOM
        $script:server = Start-Process -FilePath python -ArgumentList '-m', 'http.server', $port, '--bind', '127.0.0.1', '--directory', "`"$fake`"" -PassThru `
            -RedirectStandardError (Join-Path $Out 'fake-api.log')
        $deadline = (Get-Date).AddSeconds(15)
        while ($true) {
            try { Invoke-WebRequest "$api/repos/A7ma-B25/sahar/releases/tags/test-build" -UseBasicParsing -TimeoutSec 2 | Out-Null; break }
            catch { if ((Get-Date) -gt $deadline) { throw "fake API not up: $_" }; Start-Sleep -Milliseconds 300 }
        }
    }

    Step 'test mode without --update-api never checks' {
        $script:engine = Start-Process -FilePath $InstalledExe -ArgumentList '--test-mode', '--capturable' -PassThru
        Wait-Pipe 10
        $r = Send '{"type":"cmd","name":"update-check","args":{"manual":"true"}}'
        if ($r.ok) { throw 'update-check answered ok without a fake API' }
        if ((Update-State).reachable -ne 'false') { throw 'reachable without a fake API' }
        Stop-Engine
    }

    Step 'a newer version: she asks in her bubble, later snoozes a day, the tray check asks anyway' {
        $script:engine = Start-Process -FilePath $InstalledExe -ArgumentList '--test-mode', '--capturable', '--update-api', $api -PassThru
        Wait-Pipe 10
        Send '{"type":"cmd","name":"update-check","args":{"manual":"false"}}' | Out-Null
        $u = Wait-Update { param($u) $u.status -eq 'asked' } 25 'no question asked'
        if ($u.offer -ne '99.0.0' -or $u.step -ne 'Ask') { throw "offer $($u.offer) step $($u.step)" }
        $after = [DateTimeOffset]::Parse($u.askAfter, [Globalization.CultureInfo]::InvariantCulture)
        $hours = ($after - [DateTimeOffset]::Now).TotalHours
        if ($hours -lt 23.9 -or $hours -gt 24.1) { throw "askAfter is $hours h away, not a day" }
        $said = Text64 (Kv 'debug-comment').text64
        Write-Host "    she said: $said"
        if ($said -notmatch '99\.0\.0' -or $said -notmatch 'نسخة جديدة') { throw "her bubble says '$said'" }
        if ((Kv 'debug-list').open -ne 'true') { throw 'no yes / later choice shown' }
        if ((Get-Content $settingsPath -Raw) -notmatch '"updateAskAfter"') { throw 'askAfter not saved' }
        Send '{"type":"cmd","name":"update-answer","args":{"answer":"later"}}' | Out-Null
        if ((Kv 'debug-list').open -ne 'false') { throw 'the choice stayed open after later' }
        Send '{"type":"cmd","name":"update-check","args":{"manual":"false"}}' | Out-Null
        Wait-Update { param($u) $u.status -eq 'idle' -and $u.step -eq 'Snoozed' } 25 'a background check within the day was not snoozed' | Out-Null
        if ((Kv 'debug-list').open -ne 'false') { throw 'asked again within the day' }
        Send '{"type":"cmd","name":"tray-pick","args":{"item":"update"}}' | Out-Null
        Wait-Update { param($u) $u.status -eq 'asked' -and $u.step -eq 'Ask' } 25 'the tray item did not ask' | Out-Null
    }

    Step 'yes with a wrong .sha256: nothing runs, she stays' {
        $before = Log-Lines
        Send '{"type":"cmd","name":"update-answer","args":{"answer":"yes"}}' | Out-Null
        Wait-Update { param($u) $u.status -eq 'failed' } 60 'the bad download did not fail' | Out-Null
        if ((Log-Since $before) -notmatch 'update: checksum mismatch') { throw 'no checksum mismatch in the log' }
        if ($script:engine.HasExited) { throw 'she quit on a bad checksum' }
        if (Test-Path (Join-Path ([System.IO.Path]::GetTempPath()) "Sahar-update\$newName")) { throw 'the unverified setup was kept' }
    }

    Step 'yes with the right .sha256: verified setup runs silently, she quits gracefully and comes back' {
        Set-Content (Join-Path $fake "$newName.sha256") "$good  $newName`n" -NoNewline -Encoding ascii
        $old = $script:engine.Id
        $before = Log-Lines
        Send '{"type":"cmd","name":"update-answer","args":{"answer":"yes"}}' | Out-Null
        Wait-Gone $old 60 'the updating Sahar'
        $script:installed = Wait-Sahar 'after the update' -notPid $old -seconds 120
        $since = Log-Since $before
        foreach ($want in 'update: 99.0.0 downloaded and verified', 'update: installing 99.0.0') {
            if ($since -notmatch [regex]::Escape($want)) { throw "no '$want' in the log:`n$since" }
        }
        if ($since -notmatch '(?m) exit 0\s*$') { throw "the updating run did not end with exit 0:`n$since" }
        $setupLog = Join-Path ([System.IO.Path]::GetTempPath()) 'Sahar-update\setup.log'
        if (Test-Path $setupLog) { Copy-Item $setupLog (Join-Path $Out 'setup-update.log') -Force } else { throw 'the update ran no setup (no setup.log)' }
        Assert-DataKept 'after the update'
    }
    if ($script:server -and -not $script:server.HasExited) { $script:server.Kill() }

    Step 'silent uninstall: Sahar asked to quit; files, shortcut, entry, Run value gone; settings and log kept' {
        $id = $script:installed.Id
        $p = Start-Process -FilePath (Join-Path $InstallDir 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
        if (-not $p.WaitForExit(120000)) { throw 'uninstall did not finish' }
        Wait-Gone $id 30 'Sahar during the uninstall'
        $deadline = (Get-Date).AddSeconds(30)
        while ((Test-Path $InstallDir) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }   # the uninstaller removes itself last
        if (Test-Path $InstallDir) { throw "$InstallDir still there: $((Get-ChildItem $InstallDir).Name -join ', ')" }
        if (Test-Path -LiteralPath $StartMenuLink) { throw 'the Start-menu shortcut is still there' }
        if (Test-Path $UninstallKey) { throw 'the uninstall entry is still there' }
        if ($null -ne (Get-ItemProperty $RunKey -ErrorAction SilentlyContinue).Sahar) { throw 'the Run value is still there' }
        if (Sahars) { throw 'a Sahar is still running' }
        Assert-DataKept 'after the uninstall'
        if (@(Get-Content $EngineLog -Encoding utf8)[-1] -notmatch ' exit 0$') { throw 'her last run did not end with exit 0' }
    }

    # Leave the runner as the stage found it.
    if ($script:server -and -not $script:server.HasExited) { $script:server.Kill() }
    Sahars | ForEach-Object { $_.Kill() }
    Remove-Item (Join-Path $Roaming 'install-marker.txt'), (Join-Path $LocalData 'install-marker.txt') -ErrorAction SilentlyContinue
    if (Test-Path $backup) { Copy-Item $backup $settingsPath -Force } else { Remove-Item $settingsPath -ErrorAction SilentlyContinue }
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
            'games' { Stage-Games }
            'shell' { Stage-Shell }
            'face' { Stage-Face }
            'install' { Stage-Install }
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
