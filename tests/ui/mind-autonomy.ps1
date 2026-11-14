# 0.14 stage 'mind', stream C (dot-sourced by run.ps1; uses its helpers: Step, Start-Engine, Send, Kv, …).
# Her brain is played by the stage (test mode: mind-test-fake, then each Mind line through mind-test-msg), so every
# step is about the engine's side: the fast path, the header's words, the calls mapped to her real abilities, the Ask
# drill (header and bubble), «لا» changing nothing, Claude Code never answered, the log page, and no typed text in logs.

function Auto { Kv 'debug-autonomy' }
function Mind-Line([string]$json) {
    $b = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    $r = Send ('{"type":"cmd","name":"mind-test-msg","args":{"line64":"' + $b + '"}}')
    if (-not $r.ok) { throw "mind-test-msg refused: $($r.error)" }
}
function Mind-Sent { "$((Kv 'mind-test-sent').sent)" }
function Mind-Log([string]$day = 'today') {
    $r = Kva 'mind-log' ('{"day":"' + $day + '"}')
    (Text64 $r.json64) | ConvertFrom-Json
}
function Last-Turn([string]$kind = 'chat') {
    $m = [regex]::Matches((Mind-Sent), "turn:([ci]\d+):$kind") | Select-Object -Last 1
    if (-not $m) { throw "no $kind turn sent: $(Mind-Sent)" }
    $m.Groups[1].Value
}
function Chat-Line([string]$text) {
    $b = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text))
    $r = Send ('{"type":"cmd","name":"menu-chat","args":{"text64":"' + $b + '"}}')
    if (-not $r.ok) { throw "menu-chat refused: $($r.error)" }
}
function Card-Shot([string]$name) {
    $m = Menu
    $c = $m.card -split ','
    $full = Screenshot "$name-screen.png"
    $src = [System.Drawing.Bitmap]::new($full)
    try {
        $x = [math]::Max(0, [int]$c[0] - 24); $y = [math]::Max(0, [int]$c[1] - 24)
        $w = [math]::Min([int]$c[2] + 48, $src.Width - $x); $h = [math]::Min([int]$c[3] + 48, $src.Height - $y)
        $dst = $src.Clone([System.Drawing.Rectangle]::new($x, $y, $w, $h), $src.PixelFormat)
        $dst.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $dst.Dispose()
    }
    finally { $src.Dispose() }
    Remove-Item $full -ErrorAction SilentlyContinue
}

function Stage-MindAutonomy {
    Stop-Engine
    $kept = Settings-Json
    $notesFile = Join-Path $env:APPDATA 'Sahar\notes.json'
    $notesKept = if (Test-Path $notesFile) { Get-Content $notesFile -Raw } else { $null }
    Write-Settings '{ "helloDone": true }'
    Start-Engine
    Pose-Body ([int]($Work.Width * 0.3)) ([int]($Work.Height * 0.35)) | Out-Null

    Step 'mind (autonomy): her brain''s engine side stays light (turns, calls, the log, nothing drawn: < 3 MB over rest)' {
        Start-Sleep -Seconds 20
        $rest = (State).workingSetBytes
        Kva 'mind-test-fake' '{"on":"true"}' | Out-Null
        for ($k = 0; $k -lt 5; $k++) {
            Kva 'mind-test-initiative' '{"force":"true"}' | Out-Null
            $id = Last-Turn 'initiative'
            Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":1,"tool":"prayer.next","data":"{}","why":"x"}')
            Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":2,"tool":"timer.cancel","data":"{}","why":"x"}')
            Mind-Line ('{"v":1,"t":"say","id":"' + $id + '","text":"","final":true}')
            Mind-Log 'all' | Out-Null
            Write-Host ("    after turn {0}: {1:N1} MB" -f ($k + 1), ((State).workingSetBytes / 1MB))
        }
        Start-Sleep -Seconds 3
        $after = (State).workingSetBytes
        Write-Host ("    engine working set: {0:N1} MB at rest, {1:N1} MB after five turns (+{2:N1} MB)" -f ($rest / 1MB), ($after / 1MB), (($after - $rest) / 1MB))
        if ($after - $rest -gt 3MB) { throw "the autonomy side costs the engine $((($after - $rest) / 1MB)) MB" }
        Kva 'mind-test-fake' '{"on":"false"}' | Out-Null
    }

    Step 'mind (autonomy): the tools are her real abilities, and no Claude-permission tool exists' {
        $a = Auto
        $tools = $a.tools -split ','
        foreach ($t in 'timer.start', 'timer.cancel', 'reminder.set', 'note.save', 'hush.on', 'sleep.hide', 'game.start', 'move.play', 'prayer.next', 'music.control', 'settings.set', 'memory.remember', 'goal.add') {
            if ($tools -notcontains $t) { throw "not announced: $t ($($a.tools))" }
        }
        if ($a.tools -match 'allow|permission|approve|deny|file|send|install') { throw "a tool that must not exist: $($a.tools)" }
    }

    Step 'mind (autonomy): the fast path runs at once without her brain, logged with undo' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Chat-Line 'مؤقت ٥'
        if ((Kv 'debug-timer').running -ne 'true') { throw 'no timer' }
        Start-Sleep -Milliseconds 300
        $log = Mind-Log
        $e = @($log.entries | Where-Object tool -eq 'timer.start')[0]
        if (-not $e) { throw "no timer line in the log: $($log | ConvertTo-Json -Compress -Depth 4)" }
        if ($e.source -ne 'user' -or -not $e.undo) { throw "line: $($e | ConvertTo-Json -Compress)" }
        if ($e.summary -ne 'شغّلت مؤقت ٥ دقيقة') { throw "summary: $($e.summary)" }
        $r = Kva 'mind-undo' ('{"id":"' + $e.id + '"}')
        if ($r.ok -ne 'true') { throw 'undo refused' }
        if ((Kv 'debug-timer').running -ne 'false') { throw 'undo left the timer running' }
        $again = Kva 'mind-undo' ('{"id":"' + $e.id + '"}')
        if ($again.ok -ne 'false') { throw 'a second undo ran' }
        if ((Kv 'debug-autonomy').chatTurn -ne '-') { throw 'the fast path started a turn' }
    }

    Step 'mind (autonomy): a free line goes to her brain; the header thinks, then streams' {
        $f = Kva 'mind-test-fake' '{"on":"true","askSeconds":"8"}'
        if ($f.fake -ne 'true') { throw 'no fake Mind' }
        Chat-Line 'ركّزني شي ربع ساعة SENTINEL-TYPED'
        $script:c1 = Last-Turn
        $m = Menu
        if ((Text64 $m.line64) -ne 'عم فكّر…') { throw "header: $(Text64 $m.line64)" }
        Start-Sleep -Milliseconds 400
        Card-Shot 'app-header-thinking'
        Mind-Line ('{"v":1,"t":"partial","id":"' + $script:c1 + '","text":"يلا نركّز!"}')
        if ((Text64 (Menu).line64) -ne 'يلا نركّز!') { throw "streamed: $(Text64 (Menu).line64)" }
    }

    Step 'mind (autonomy): calls run her real abilities (timer, hush, note, game) and are logged' {
        Mind-Line ('{"v":1,"t":"call","id":"' + $script:c1 + '","call":1,"tool":"timer.start","data":"{\"minutes\":15}","why":"x"}')
        Start-Sleep -Milliseconds 300
        $t = Kv 'debug-timer'
        if ($t.running -ne 'true' -or [int]$t.remaining -lt 880) { throw "timer: $($t.running) $($t.remaining)" }
        if ((Mind-Sent) -notmatch "tool-result:$($script:c1)::1:ok") { throw "sent: $(Mind-Sent)" }
        Mind-Line ('{"v":1,"t":"call","id":"' + $script:c1 + '","call":2,"tool":"hush.on","data":"{\"minutes\":30}","why":"x"}')
        Start-Sleep -Milliseconds 300
        if ((Auto).hush -ne 'true') { throw 'no hush' }
        Mind-Line ('{"v":1,"t":"call","id":"' + $script:c1 + '","call":3,"tool":"note.save","data":"{\"text\":\"SENTINEL-NOTE\"}","why":"x"}')
        Start-Sleep -Milliseconds 300
        if (-not ((Get-Content $notesFile -Raw) -match 'SENTINEL-NOTE')) { throw 'the note was not saved' }
        Mind-Line ('{"v":1,"t":"say","id":"' + $script:c1 + '","text":"شغّلت مؤقت ربع ساعة وسكتّ","final":true}')
        Start-Sleep -Milliseconds 300
        $log = Mind-Log
        foreach ($t in 'timer.start', 'hush.on', 'note.save') {
            if (-not ($log.entries | Where-Object { $_.tool -eq $t -and $_.outcome -eq 'done' })) { throw "no $t line" }
        }
        $q = @($log.entries | Where-Object tool -eq 'note.save')[0]
        if ($q.why -notmatch 'ركّزني') { throw "his words are not with his line: $($q.why)" }
        # a game: the card closes and the game runs
        Chat-Line 'بدي العب'
        $script:c2 = Last-Turn
        Mind-Line ('{"v":1,"t":"call","id":"' + $script:c2 + '","call":1,"tool":"game.start","data":"{\"game\":\"stars\"}","why":"x"}')
        Start-Sleep -Milliseconds 500
        if ((Auto).game -ne 'true') { throw 'no game' }
        Send '{"type":"cmd","name":"game-stop"}' | Out-Null
        Start-Sleep -Milliseconds 800
    }

    Step 'mind (autonomy): a B call asks in the header; «لا» changes nothing' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Chat-Line 'شغلي الطقس'
        $id = ([regex]::Matches((Mind-Sent), 'turn:(c\d+):chat') | Select-Object -Last 1).Groups[1].Value
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":1,"tool":"settings.set","data":"{\"key\":\"weatherEnabled\",\"value\":true}","why":"x"}')
        $a = Auto
        if ($a.ask -ne 'header' -or (Text64 $a.ask64) -ne 'شغّل الطقس؟') { throw "ask: $($a.ask) $(Text64 $a.ask64)" }
        $m = Menu
        if ((Text64 $m.line64) -ne 'شغّل الطقس؟') { throw "header: $(Text64 $m.line64)" }
        if (-not ($m.buttons | Where-Object id -eq 'ask-yes') -or -not ($m.buttons | Where-Object id -eq 'ask-no')) { throw "buttons: $(Ids $m)" }
        if ($m.focus -ne 'ask-no') { throw "focus: $($m.focus)" }
        Press 'ask-yes'   # at once (a typed "1" or a stray Enter): the guard keeps the question
        if ((Auto).ask -ne 'header') { throw 'an answer within the guard was taken' }
        Start-Sleep -Milliseconds 400
        Card-Shot 'app-header-ask'
        Start-Sleep -Milliseconds 500
        Press 'ask-no'
        Start-Sleep -Milliseconds 300
        if ((Auto).ask -ne 'none') { throw 'the question stayed' }
        if ((Mind-Sent) -notmatch "tool-result:${id}::1:no:asked-no") { throw "sent: $(Mind-Sent)" }
        if ((Setting 'weatherEnabled') -eq $true) { throw 'the weather was switched on' }
        $e = @((Mind-Log).entries | Where-Object outcome -eq 'asked-no')[0]
        if (-not $e -or $e.summary -ne 'سألتك: شغّل الطقس؟') { throw "log: $($e | ConvertTo-Json -Compress)" }
        # «إي» runs a B change he asked for (language is B: always asked)
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":2,"tool":"settings.set","data":"{\"key\":\"language\",\"value\":\"arabic\"}","why":"x"}')
        if ((Auto).ask -ne 'header') { throw 'no second question' }
        Start-Sleep -Milliseconds 900
        Press 'ask-yes'
        Start-Sleep -Milliseconds 300
        if ((Mind-Sent) -notmatch "tool-result:${id}::2:ok") { throw "sent: $(Mind-Sent)" }
        Wait-Settings '"language"\s*:\s*"arabic"' 3000 'language arabic'
        # her timer and her hush end (his line asked): the next steps start calm
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":3,"tool":"timer.cancel","data":"{}","why":"x"}')
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":4,"tool":"hush.off","data":"{}","why":"x"}')
        Start-Sleep -Milliseconds 300
        if ((Auto).timer -ne 'false' -or (Auto).hush -ne 'false') { throw "timer/hush still on: $((Auto).timer) $((Auto).hush)" }
        Mind-Line ('{"v":1,"t":"say","id":"' + $id + '","text":"تمام","final":true}')
    }

    Step 'mind (autonomy): his own timer is never replaced or stopped without his yes; undo gives it back' {
        Chat-Line 'مؤقت ٥'
        if ((Kv 'debug-timer').running -ne 'true') { throw 'no timer of his' }
        Chat-Line 'خلّيه ربع ساعة'
        $id = Last-Turn
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":1,"tool":"timer.start","data":"{\"minutes\":15}","why":"x"}')
        $a = Auto
        if ($a.ask -ne 'header' -or (Text64 $a.ask64) -notmatch 'مؤقتك') { throw "ask: $($a.ask) $(Text64 $a.ask64)" }
        Start-Sleep -Milliseconds 900
        Press 'ask-no'
        Start-Sleep -Milliseconds 300
        if ([int](Kv 'debug-timer').remaining -gt 300) { throw 'his timer was replaced' }
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":2,"tool":"timer.start","data":"{\"minutes\":15}","why":"x"}')
        Start-Sleep -Milliseconds 900
        Press 'ask-yes'
        Start-Sleep -Milliseconds 300
        if ([int](Kv 'debug-timer').remaining -lt 880) { throw 'her timer did not start after his yes' }
        $e = @((Mind-Log).entries | Where-Object { $_.tool -eq 'timer.start' -and $_.outcome -eq 'asked-yes' })[0]
        if (-not $e.undo) { throw 'no undo for her timer' }
        if ((Kva 'mind-undo' ('{"id":"' + $e.id + '"}')).ok -ne 'true') { throw 'undo refused' }
        $left = [int](Kv 'debug-timer').remaining
        if ($left -gt 300 -or $left -lt 200) { throw "his timer was not given back: $left s" }
        Send '{"type":"cmd","name":"menu-press","args":{"id":"timer"}}' | Out-Null
        Send '{"type":"cmd","name":"menu-press","args":{"id":"timer-cancel"}}' | Out-Null
        Mind-Line ('{"v":1,"t":"say","id":"' + $id + '","text":"","final":true}')
        if ((Kv 'debug-timer').running -ne 'false') { throw 'his timer is still running' }
    }

    Step 'mind (autonomy): with the card closed the bubble asks; no answer is «لا»' {
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 2000 'card closed' | Out-Null
        $g = Kva 'mind-test-initiative' '{"force":"true"}'
        $id = ([regex]::Matches((Mind-Sent), 'turn:(i\d+):initiative') | Select-Object -Last 1).Groups[1].Value
        if (-not $id) { throw "no initiative turn ($($g.gate)): $(Mind-Sent)" }
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":1,"tool":"settings.set","data":"{\"key\":\"awarenessEnabled\",\"value\":false}","why":"x"}')
        if ((Auto).ask -ne 'bubble') { throw "ask: $((Auto).ask)" }
        $l = Kv 'debug-list'
        if ($l.open -ne 'true') { throw 'no bubble' }
        Start-Sleep -Milliseconds 9000   # the stage's 8 s instead of 2 min
        if ((Auto).ask -ne 'none') { throw 'still asking' }
        if ((Mind-Sent) -notmatch "tool-result:${id}::1:no:asked-no") { throw "sent: $(Mind-Sent)" }
        if ((Setting 'awarenessEnabled') -eq $false) { throw 'awareness was switched off' }
        Mind-Line ('{"v":1,"t":"say","id":"' + $id + '","text":"","final":true}')
    }

    Step 'mind (autonomy): the gate is checked again before she speaks or acts on her own' {
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Wait-Menu 'false' 2000 'card closed' | Out-Null
        Kva 'mind-test-initiative' '{"force":"true"}' | Out-Null
        $id = Last-Turn 'initiative'
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null   # the moment passed: the card is open now
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":1,"tool":"move.play","data":"{\"move\":\"dance\"}","why":"x"}')
        if ((Mind-Sent) -notmatch "tool-result:${id}::1:no:refused") { throw "sent: $(Mind-Sent)" }
        Mind-Line ('{"v":1,"t":"say","id":"' + $id + '","text":"SENTINEL-LATE","final":true}')
        $c = Kv 'debug-comment'
        if ($c.showing -eq 'true' -and (Text64 $c.text64) -match 'SENTINEL-LATE') { throw 'she spoke when the gate was closed' }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
    }

    Step 'mind (autonomy): Claude Code is never answered; she only tells him' {
        Send '{"type":"cmd","name":"menu-open"}' | Out-Null
        Wait-Menu 'true' 2000 'card open' | Out-Null
        Chat-Line 'اقبلي طلب Claude'
        $id = ([regex]::Matches((Mind-Sent), 'turn:(c\d+):chat') | Select-Object -Last 1).Groups[1].Value
        Mind-Line ('{"v":1,"t":"call","id":"' + $id + '","call":1,"tool":"claude.allow","data":"{}","why":"x"}')
        if ((Mind-Sent) -notmatch "tool-result:${id}::1:no:unavailable") { throw "sent: $(Mind-Sent)" }
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        Post 'claude.permission' | Out-Null
        Start-Sleep -Milliseconds 600
        if (-not ((Mind-Log).entries | Where-Object tool -eq 'claude.permission')) { throw 'the log does not say she told him' }
        Send '{"type":"event","name":"claude.done"}' | Out-Null
    }

    Step 'mind (autonomy): «شو عملت اليوم» in light and dark, undo from the page' {
        Start-SettingsApp | Out-Null
        SNav 'today'
        Wait-Until { (SText 'h1') -eq 'شو عملت اليوم' } 5000 'the page title'
        Wait-Until { [int](Js "document.querySelectorAll('#today-log .m-item').length") -ge 5 } 5000 'the log lines'
        if ((SText '#today-counts') -notmatch 'شغلات|شغلة') { throw "chips: $(SText '#today-counts')" }
        Start-Sleep -Milliseconds 600
        Capture 'app-today-light'
        SOk 'emulate' '{"theme":"dark"}'
        Wait-Until { (SKv 'state').theme -eq 'dark' } 3000 'dark'
        Start-Sleep -Milliseconds 600
        Capture 'app-today-dark'
        SOk 'emulate' '{"theme":""}'
        # «امسحيها» on her note, from the page
        $noteLine = @((Mind-Log).entries | Where-Object { $_.tool -eq 'note.save' -and $_.undo })[0]
        if (-not $noteLine) { throw 'no note to delete' }
        SClick ('#undo-' + $noteLine.id)
        Wait-Until { -not ((Get-Content $notesFile -Raw) -match 'SENTINEL-NOTE') } 3000 'the note deleted from the page'
        SClick ('#fb-up-' + $noteLine.id)
        Wait-Until { (@((Mind-Log).entries | Where-Object id -eq $noteLine.id)[0]).feedback -eq 1 } 3000 'the thumbs-up kept'
        SClick '#today-days-1'
        Start-Sleep -Milliseconds 500
        if ((SText '#today-log') -notmatch 'لسا ما عملت شي هون') { throw 'yesterday is not empty' }
        Stop-SettingsApp
    }

    Step 'mind (autonomy): engine.log holds kinds only, never what he typed' {
        $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw
        foreach ($s in 'SENTINEL-TYPED', 'SENTINEL-NOTE', 'ركّزني', 'شغّل الطقس') { if ($log -match [regex]::Escape($s)) { throw "engine.log holds '$s'" } }
        if ($log -notmatch 'mind: call timer.start act') { throw 'the call kinds are not logged' }
    }

    Stop-Engine
    if ($kept) { Write-Settings $kept } else { Remove-Item $script:SettingsFile -ErrorAction SilentlyContinue }
    if ($null -ne $notesKept) { Set-Content -Path $notesFile -Value $notesKept -Encoding UTF8 -NoNewline } else { Remove-Item $notesFile -ErrorAction SilentlyContinue }
}
