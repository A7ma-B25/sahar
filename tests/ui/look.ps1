# 0.16 «الشكل والحكي»: every new surface opened for real on Windows — the pickers, the input cards, the ask bubble, the
# tray menu, «الدرج» and the chat — checked through the page itself and photographed in light and dark (app-<name>-<theme>.png,
# for the owner beside the approved V15 mockups). Each open time from the warm host is printed (target < 150 ms).

function Crop-Rect([string]$path, [int]$x, [int]$y, [int]$w, [int]$h, [string]$to) {
    $src = [System.Drawing.Bitmap]::new($path)
    try {
        $x = [Math]::Max(0, $x); $y = [Math]::Max(0, $y)
        $w = [Math]::Min($w, $src.Width - $x); $h = [Math]::Min($h, $src.Height - $y)
        $dst = $src.Clone([System.Drawing.Rectangle]::new($x, $y, $w, $h), $src.PixelFormat)
        $dst.Save($to, [System.Drawing.Imaging.ImageFormat]::Png)
        $dst.Dispose()
    }
    finally { $src.Dispose() }
}

$script:lookMs = [System.Collections.Generic.List[string]]::new()

# Photographs the open surface with her body (a margin around both), after its enter animation.
function Look-Shot([string]$name, [string]$surface = 'panel') {
    $s = Wait-Surface $surface
    Start-Sleep -Milliseconds 450
    $c = $s.$surface
    $script:lookMs.Add("$name $($c.ms) ms")
    $st = State
    $x1 = [Math]::Min([int]$c.x, [int]$st.x); $y1 = [Math]::Min([int]$c.y, [int]$st.y)
    $x2 = [Math]::Max([int]$c.x + [int]$c.w, [int]$st.x + [int]$st.width); $y2 = [Math]::Max([int]$c.y + [int]$c.h, [int]$st.y + [int]$st.height)
    if ($surface -eq 'chat' -or $name -like 'tray*') { $x1 = [int]$c.x; $y1 = [int]$c.y; $x2 = [int]$c.x + [int]$c.w; $y2 = [int]$c.y + [int]$c.h }
    $full = Screenshot "look-full-$name.png"
    Crop-Rect $full ($x1 - 48) ($y1 - 48) ($x2 - $x1 + 96) ($y2 - $y1 + 96) (Join-Path $Out "app-$name.png")
    $s
}

function Look-Kv([string]$cmd, [string]$argsJson) {
    $r = Send ('{"type":"cmd","name":"' + $cmd + '","args":' + $argsJson + '}')
    if (-not $r.ok) { throw "$cmd not ok: $($r.error)" }
    $h = @{}; foreach ($pair in ($r.error -split ' ')) { $k, $v = $pair -split '=', 2; $h[$k] = $v }
    $h
}
function T64([string]$s) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) }

function Stage-Look {
    $wx = Join-Path $env:TEMP 'sahar-look-weather'
    New-Item -ItemType Directory -Force -Path $wx | Out-Null
    '{"results":[{"name":"Damascus","latitude":33.51,"longitude":36.29}]}' | Set-Content (Join-Path $wx 'geocode.json')
    '{"hourly":{"time":["2026-11-06T20:00"],"weather_code":[1],"precipitation_probability":[0]}}' | Set-Content (Join-Path $wx 'forecast.json')
    foreach ($theme in @('light', 'dark')) {
        Step "look ($theme): the warm surface host starts hidden and both windows are ready" {
            Stop-Engine
            Write-Settings ('{ "helloDone": true, "appearance": "' + $theme + '" }')
            $script:lookWin = Start-TestWindow "scene-$theme"
            Start-Engine @('--weather-stub', $wx)
            Send '{"type":"cmd","name":"motion","args":{"kind":"Hover"}}' | Out-Null
            Pose-Body ([int]($Work.Width * 0.62)) ([int]($Work.Height * 0.55)) | Out-Null
            [Mouse]::Move(5, 5)
            Wait-Until { $s = Surface; $s -and $s.panel.ready -and $s.chat.ready } 120000 'the surface host did not get ready'
            $s = Surface
            if ($s.panel.visible -or $s.chat.visible) { throw 'a surface showed before it was asked for' }
        }
        Step "look ($theme): Quran — the resume row, typing «يس» leaves one row, the height holds, no result, the reciter drill" {
            Write-Settings ('{ "helloDone": true, "appearance": "' + $theme + '", "quranLastSurah": 18 }')
            Reload-Settings | Out-Null
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Wait-Menu 'true' 2000 'menu open' | Out-Null
            Press 'listen'; Press 'quran'
            $s = Look-Shot "surah-$theme"
            if ($s.panel.dark -ne ($theme -eq 'dark')) { throw "the panel theme is wrong (dark=$($s.panel.dark))" }
            $h0 = (Panel-State).height
            Type-InBubble 'يس'
            Start-Sleep -Milliseconds 300
            $st = Panel-State
            if ([int]$st.rows -ne 1) { throw "typing «يس»: $($st.rows) rows" }
            if ([math]::Abs([double]$st.height - [double]$h0) -gt 0.5) { throw "the panel jumped: $h0 → $($st.height)" }
            Look-Shot "surah-search-$theme" | Out-Null
            Type-InBubble ([string]::new([char[]](0x0663, 0x0666)))   # ٣٦
            if ([int](Panel-State).rows -ne 1) { throw '«٣٦» did not find يس' }
            Type-InBubble 'الفرقانن'
            if ((Panel-State).text -notmatch 'ما لقيت سورة') { throw 'no «ما لقيت» state' }
            Look-Shot "surah-none-$theme" | Out-Null
            Type-InBubble ''
            Send '{"type":"cmd","name":"panel-click","args":{"id":"reciter"}}' | Out-Null
            Start-Sleep -Milliseconds 400
            if ((Kv 'debug-panel').kind -ne 'reciter') { throw 'no reciter drill' }
            Look-Shot "reciter-$theme" | Out-Null
            Send '{"type":"cmd","name":"panel-key","args":{"key":"ArrowRight"}}' | Out-Null
            Start-Sleep -Milliseconds 400
            if ((Kv 'debug-panel').kind -ne 'surah') { throw 'back did not return to the surahs' }
            Close-Panel
            if ((Surface).panel.open) { throw 'Esc did not close it' }
        }
        Step "look ($theme): the clipboard — its list, «امسح الكل» asks «أكيد؟», «لا» keeps them; empty" {
            foreach ($t in 'سلام، رح اتأخر شي ربع ساعة', 'npm run build -- --release', 'شكراً كتير، وصل الملف') { Set-Clipboard -Value $t; Start-Sleep -Milliseconds 700 }
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Press 'clipboard'
            Look-Shot "clipboard-$theme" | Out-Null
            $n = [int](Kv 'debug-panel').lines
            Send '{"type":"cmd","name":"panel-click","args":{"id":"clear"}}' | Out-Null
            Start-Sleep -Milliseconds 300
            if ((Panel-State).text -notmatch 'أكيد؟') { throw 'no «أكيد؟» before clearing' }
            Look-Shot "clipboard-confirm-$theme" | Out-Null
            Send '{"type":"cmd","name":"panel-click","args":{"id":"confirm-no"}}' | Out-Null
            Start-Sleep -Milliseconds 300
            if ([int](Kv 'debug-panel').lines -ne $n -or [int](Clip-Count) -lt 3) { throw '«لا» cleared them' }
            Close-Panel
            Send '{"type":"cmd","name":"clipboard-clear"}' | Out-Null
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Press 'clipboard'
            if ((Panel-State).text -notmatch 'لسا ما نسخت') { throw 'no empty state' }
            Look-Shot "clipboard-empty-$theme" | Out-Null
            Close-Panel
        }
        Step "look ($theme): the city card suggests while typing, then the prayer times and the method drill" {
            Write-Settings ('{ "helloDone": true, "appearance": "' + $theme + '" }')
            Reload-Settings | Out-Null
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Send '{"type":"cmd","name":"menu-press","args":{"id":"prayer"}}' | Out-Null
            Look-Shot "city-$theme" | Out-Null
            Type-InBubble 'Damascus'
            Wait-Until { (Panel-State).text -match 'Damascus' -and (Kv 'debug-panel').lines -ge 0 } 3000 'no suggestion'
            Start-Sleep -Milliseconds 600
            Look-Shot "city-suggest-$theme" | Out-Null
            Type-InBubble 'Damascus' -Enter
            Wait-Until { (Kv 'debug-panel').kind -eq 'prayer' } 5000 'the prayer times did not open'
            Look-Shot "prayer-$theme" | Out-Null
            Send '{"type":"cmd","name":"panel-click","args":{"id":"method"}}' | Out-Null
            Wait-Until { (Kv 'debug-panel').kind -eq 'method' } 3000 'no method drill'
            Look-Shot "method-$theme" | Out-Null
            Close-Panel
        }
        Step "look ($theme): the timer card — «٤٠» starts 40 minutes; words show the error, keep the text, start nothing" {
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Press 'timer'; Press 'timer-custom'
            Look-Shot "timer-$theme" | Out-Null
            $words = [string]::new([char[]](0x0634, 0x064A, 0x0020, 0x0633, 0x0627, 0x0639, 0x0629, 0x0020, 0x0648, 0x0646, 0x0635))   # شي ساعة ونص
            Type-InBubble $words -Enter
            Start-Sleep -Milliseconds 400
            $b = Kv 'debug-bubble'
            if ($b.open -ne 'true' -or (Text64 $b.error64) -notmatch 'ما فهمتها' -or (Text64 $b.value64) -ne $words) { throw "no error, or the text went: $(Text64 $b.error64) / $(Text64 $b.value64)" }
            if ((Kv 'debug-timer').running -ne 'false') { throw 'a timer started from words' }
            Look-Shot "timer-error-$theme" | Out-Null
            Type-InBubble ([string]::new([char[]](0x0664, 0x0660))) -Enter   # ٤٠
            Start-Sleep -Milliseconds 400
            $t = Kv 'debug-timer'
            if ($t.running -ne 'true' -or [int]$t.remaining -lt 2390) { throw "«٤٠»: running=$($t.running) remaining=$($t.remaining)" }
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Press 'timer'; Press 'timer-cancel'
            $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw -Encoding UTF8
            if ($log -match $words) { throw 'what he typed is in engine.log' }
        }
        Step "look ($theme): the note card, her first hello and the «شب ولا بنت؟» ask bubble" {
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Press 'note'
            Look-Shot "note-$theme" | Out-Null
            Close-Panel
            Send '{"type":"cmd","name":"hello-new"}' | Out-Null
            Look-Shot "name-$theme" | Out-Null
            Type-InBubble 'Sami' -Enter
            Wait-Until { (Kv 'debug-panel').kind -eq 'gender' } 3000 'no «شب ولا بنت؟»'
            Look-Shot "gender-$theme" | Out-Null
            Send '{"type":"cmd","name":"panel-key","args":{"key":"1"}}' | Out-Null
            Wait-Until { (Settings-Json) -match '"userGender"\s*:\s*"male"' } 3000 'key 1 did not answer «شب»'
        }
        Step "look ($theme): the tray menu — one language, the weather a clear toggle, quit last" {
            Look-Kv 'tray-open' ('{"x":"' + ($Work.Width - 60) + '","y":"' + ($Work.Height + 20) + '"}') | Out-Null
            $s = Look-Shot "tray-$theme"
            $txt = (Panel-State).text
            if ($txt -match '[A-Za-z]{4,}') { throw "English in the Arabic tray menu: $txt" }
            if ($txt -match '\(اسمي\)') { throw 'the placeholder «(اسمي)» is back' }
            if ([int]$s.panel.y + [int]$s.panel.h -gt $Work.Height + 1) { throw 'the tray menu leaves the work area' }
            Close-Panel
        }
        Step "look ($theme): «الدرج» — what is live, the shelf, and what happened" {
            Act 'activity-sim' '{"kind":"music"}' | Out-Null
            Send ('{"type":"cmd","name":"drawer-open","args":{"text64":"' + (T64 'خلص المؤقت') + '"}}') | Out-Null
            Look-Shot "drawer-$theme" | Out-Null
            $txt = (Panel-State).text
            if ($txt -notmatch 'شغّال هلق' -or $txt -notmatch 'شو صار اليوم' -or $txt -notmatch 'خلص المؤقت') { throw "the drawer shows: $txt" }
            Close-Panel
            Act 'activity-clear' | Out-Null
        }
        Step "look ($theme): the chat — empty, then he left and she right, starts at the bottom, the list" {
            Send '{"type":"cmd","name":"chat-open","args":{"fresh":"true"}}' | Out-Null
            Look-Shot "chat-empty-$theme" 'chat' | Out-Null
            Send ('{"type":"cmd","name":"chat-type","args":{"text64":"' + (T64 'صباح الخير يا سحورة') + '"}}') | Out-Null
            Wait-Until { [int](Kv 'debug-chat').count -ge 2 } 5000 'no reply in the chat'
            foreach ($i in 1..6) { Send ('{"type":"cmd","name":"chat-said","args":{"text64":"' + (T64 "سطر $i من حكينا 🌙") + '"}}') | Out-Null }
            Start-Sleep -Milliseconds 400
            $st = Panel-State 'chat'
            if ($null -eq $st.herX -or $null -eq $st.meX -or [double]$st.herX -le [double]$st.meX) { throw "sides: her x=$($st.herX), his x=$($st.meX)" }
            if (-not $st.atBottom) { throw 'the thread is not at its latest line' }
            Look-Shot "chat-$theme" 'chat' | Out-Null
            # Nothing of hers sits over the open chat: her speech bubble, «الدرج», an ask (it goes into the thread).
            $chatRect = (Surface).chat
            function Meets($a, $x, $y, $w, $h) { -not ($x -ge $a.x + $a.w -or $x + $w -le $a.x -or $y -ge $a.y + $a.h -or $y + $h -le $a.y) }
            Send ('{"type":"cmd","name":"say","args":{"text64":"' + (T64 'سطر تجربة فوق الحكي، طويل شوي لحتى يكون عريض') + '"}}') | Out-Null
            Wait-Until { (Kv 'debug-speech').showing -eq 'true' } 3000 'her bubble did not show'
            $r = (Kv 'debug-speech').rect -split ','
            if (Meets $chatRect ([int]$r[0]) ([int]$r[1]) ([int]$r[2]) ([int]$r[3])) { throw "her bubble ($($r -join ',')) is over the chat ($($chatRect.x),$($chatRect.y),$($chatRect.w),$($chatRect.h))" }
            Send '{"type":"cmd","name":"drawer-open"}' | Out-Null
            $p = (Wait-Surface 'panel').panel
            if (Meets $chatRect $p.x $p.y $p.w $p.h) { throw "«الدرج» ($($p.x),$($p.y),$($p.w),$($p.h)) is over the chat" }
            Close-Panel
            Send '{"type":"cmd","name":"panel-click","args":{"surface":"chat","id":"all"}}' | Out-Null
            Start-Sleep -Milliseconds 400
            if ((Panel-State 'chat').view -ne 'list') { throw 'no conversation list' }
            Look-Shot "chat-list-$theme" 'chat' | Out-Null
            Send '{"type":"cmd","name":"chat-close"}' | Out-Null
            Start-Sleep -Milliseconds 500
            if ((Surface).chat.open) { throw 'the chat did not close' }
            $log = Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw -Encoding UTF8
            if ($log -match 'صباح الخير يا سحورة' -or $log -match 'من حكينا') { throw 'chat text is in engine.log' }
        }
        Step "look ($theme): open times from the warm host" {
            Write-Host "    $($script:lookMs -join ' | ')"
            $slow = @($script:lookMs | Where-Object { [double](($_ -split ' ')[1]) -gt 1500 })
            if ($slow.Count) { throw "slow opens: $($slow -join ', ')" }
            $script:lookMs.Clear()
        }
        if ($script:lookWin -and -not $script:lookWin.HasExited) { $script:lookWin.Kill() }
    }
    Stop-Engine
    # What this stage set up (a city, a name, copied text) never reaches the next stage in the same job.
    Remove-Item $script:SettingsFile, (Join-Path $env:APPDATA 'Sahar\notes.json') -ErrorAction SilentlyContinue
    Send-Clear-Clipboard
}
function Send-Clear-Clipboard {
    Remove-Item (Join-Path $env:APPDATA 'Sahar\clipboard.json') -Force -ErrorAction SilentlyContinue
    Set-Clipboard -Value ' '
}
