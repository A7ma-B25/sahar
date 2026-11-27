# 0.17 stage 'mind', the online brain (dot-sourced by run.ps1; uses its helpers). A fake OpenAI-compatible provider
# on 127.0.0.1 (tests/ui/fake_llm.py; never the internet) stands in for Groq and OpenRouter through
# SAHAR_MIND_ONLINE_BASE (loopback only); her local brain is the scripted fake (SAHAR_MIND_SCRIPT). Proves: consent
# first, the key sealed by the Mind (never in settings.json or a log), the key test's plain lines, a chat-window line
# streamed online with the «أونلاين» badge, an action line local, 429 → the next provider → told once → local,
# «محلي فقط» sends nothing, background turns never go online, a sensitive past line never leaves.

function Fake([string]$path, [string]$method = 'GET') {
    Invoke-RestMethod -Method $method -Uri "http://127.0.0.1:$($script:fakePort)$path" -NoProxy -TimeoutSec 5
}
function Fake-Count([string]$route) { $c = (Fake '/_stats').counts; if ($c.PSObject.Properties[$route]) { [int]$c.$route } else { 0 } }
function Fake-Total { $n = 0; foreach ($p in (Fake '/_stats').counts.PSObject.Properties) { $n += [int]$p.Value }; $n }
function MindLog { Get-Content (Join-Path $script:onHome 'mind.log') -Raw -Encoding utf8 }
function Chat-Her {
    $r = Send ('{"type":"cmd","name":"surface-eval","args":{"surface":"chat","script64":"' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("JSON.stringify([...document.querySelectorAll('.msg.her')].map(m => m.textContent))")) + '"}}')
    $h = @{}; foreach ($pair in ($r.error -split ' ')) { $k, $v = $pair -split '=', 2; $h[$k] = $v }
    @(([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($h.result64)) | ConvertFrom-Json) | ConvertFrom-Json)
}
function Window-Line([string]$text) {
    $b = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text))
    Send ('{"type":"cmd","name":"chat-type","args":{"text64":"' + $b + '"}}') | Out-Null
}
function Routes { @([regex]::Matches((MindLog), 'turn: route (\w+) \(([\w-]+)\)') | ForEach-Object { "$($_.Groups[1].Value):$($_.Groups[2].Value)" }) }

function Stage-MindOnline {
    Stop-Engine
    $kept = Settings-Json
    $script:onHome = Join-Path $env:TEMP ("sahar-online-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path (Join-Path $onHome 'models') | Out-Null
    $script:fakePort = Get-Random -Minimum 20000 -Maximum 40000
    $fake = Start-Process -FilePath python -ArgumentList '-I', "`"$PSScriptRoot\fake_llm.py`"", '--port', $fakePort -PassThru -WindowStyle Hidden
    $script_ = Join-Path $onHome 'script.txt'
    (1..40 | ForEach-Object { '{"actions":[],"next":"done","say":"محلي ✓","mood":null,"remember":[]}' }) | Set-Content -Path $script_ -Encoding UTF8
    $env:SAHAR_MIND_HOME = $onHome
    $env:SAHAR_MIND_SCRIPT = $script_
    $env:SAHAR_MIND_ONLINE_BASE = "http://127.0.0.1:$fakePort"
    try {
        Step 'online: the fake provider is up (127.0.0.1 only)' {
            Wait-Until { try { Fake '/_stats' | Out-Null; $true } catch { $false } } 10000 'fake_llm.py did not start'
            Fake '/_marker?w=SENTINELDEBT' 'POST' | Out-Null
            Fake '/_marker?w=good-' 'POST' | Out-Null   # a key in a body would be a bug (it goes in the header only)
        }
        Step 'online: «العقل أونلاين» asks consent first; the key is sealed by the Mind and tested («يشتغل ✓»)' {
            Write-Settings '{ "helloDone": true, "mindEnabled": true }'
            Start-Engine
            Start-SettingsApp | Out-Null
            SNav 'online'
            Wait-Until { (Js "!!document.getElementById('on-consent')") -eq 'true' } 5000 'no consent card'
            if ((Js "!!document.getElementById('on-key-groq')") -eq 'true') { throw 'the key field shows before consent' }
            Capture 'app-online-consent'
            SClick '#on-consent-ok'
            Wait-Settings '"mindOnlineConsent"\s*:\s*true' 3000 'consent not saved'
            Wait-Until { (Js "!!document.getElementById('on-key-groq')") -eq 'true' } 5000 'no key field after consent'
            SType '#on-key-groq' 'good-groq'
            SClick '#on-save-groq'
            Wait-Until { (SText '#on-result-groq') -eq 'يشتغل ✓' } 20000 "the groq key test: $(SText '#on-result-groq')"
            if ((Js "document.getElementById('on-key-groq').value") -ne '') { throw 'the key stayed in the page field' }
            Wait-Until { @(Setting 'mindOnlineKeys') -contains 'groq' } 5000 'mindOnlineKeys has no groq flag'
            if ((Settings-Json) -match 'good-groq') { throw 'the key is in settings.json' }
            SType '#on-key-openrouter' 'bad-openrouter'
            SClick '#on-save-openrouter'
            Wait-Until { (SText '#on-result-openrouter') -eq 'المفتاح غلط' } 20000 "the bad key: $(SText '#on-result-openrouter')"
            SType '#on-key-openrouter' 'good-openrouter'
            SClick '#on-save-openrouter'
            Wait-Until { (SText '#on-result-openrouter') -eq 'يشتغل ✓' } 20000 "the openrouter key test: $(SText '#on-result-openrouter')"
            Wait-Until { (SText '#on-quota-openrouter') -match 'اليوم: ٣ من ٥٠' } 8000 "openrouter's counter: $(SText '#on-quota-openrouter')"
            foreach ($theme in 'light', 'dark') {
                SOk 'emulate' ('{"theme":"' + $theme + '"}')
                Start-Sleep -Milliseconds 600
                Write-Host "    page: consent=$(Js 'String(window.sahar.settings().mindOnlineConsent)') keys=$(Js 'JSON.stringify(window.sahar.settings().mindOnlineKeys)') rows=$(Js "document.querySelectorAll('.on-row').length"); file: consent=$(Setting 'mindOnlineConsent')"
                Wait-Until { (Js "document.querySelectorAll('.on-row').length") -eq '3' } 5000 'the providers are not on the page after a re-render'
                Capture "app-online-$theme"
            }
            SOk 'emulate' '{"theme":""}'
            Stop-SettingsApp
        }
        Step 'online: a sensitive line in the card stays local and never leaves later' {
            $before = Fake-Total
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Chat-Line 'عندي ديون كتير SENTINELDEBT وتعبان'
            Wait-Until { (Routes).Count -ge 0 -and (MindLog) -match 'turn: chat \d+ms' } 20000 'the card turn'
            Send '{"type":"cmd","name":"menu-close"}' | Out-Null
            if ((Fake-Total) -ne $before) { throw 'a card line reached the online provider' }
        }
        Step 'online: a chat-window line streams online, with the «أونلاين» badge' {
            Send '{"type":"cmd","name":"chat-open","args":{"fresh":"true"}}' | Out-Null
            Wait-Surface 'chat' | Out-Null
            $n = Fake-Count '/groq/chat/completions'
            $sw = [Diagnostics.Stopwatch]::StartNew()
            Window-Line 'شو رأيك بالقهوة الصبح؟'
            Wait-Until { (Kv 'debug-chat').lastBrain -eq 'online' } 30000 "no online answer (routes: $((Routes) -join ', '))"
            Write-Host "    answered online in $($sw.ElapsedMilliseconds) ms; $(([regex]::Matches((MindLog), 'turn: online [^\r\n]*') | Select-Object -Last 1).Value)"
            if ((Fake-Count '/groq/chat/completions') -ne $n + 1) { throw 'groq was not asked exactly once' }
            $her = @(Chat-Her)
            if (($her[-1]) -notmatch 'جواب أونلاين') { throw "her line: $($her[-1])" }
            if (@(Routes)[-1] -ne 'online:chat') { throw "route: $(@(Routes)[-1])" }
            $hits = (Fake '/_stats').hits
            if ($hits.PSObject.Properties['SENTINELDEBT']) { throw 'the sensitive past line was sent online' }
            if ($hits.PSObject.Properties['good-']) { throw 'a key was in a request body' }
        }
        Step 'online: an action line stays local («ذكريني…»)' {
            $before = Fake-Total
            Window-Line 'ذكريني بعد شوي إشرب مي'
            Wait-Until { @(Routes)[-1] -eq 'local:action' } 15000 "route: $(@(Routes)[-1])"
            Wait-Until { $k = Kv 'debug-chat'; $k.lastBrain -eq 'local' -and $k.typing -eq 'false' } 15000 'no local answer'
            if ((Fake-Total) -ne $before) { throw 'an action line reached the online provider' }
        }
        Step 'online: a background turn never goes online' {
            $before = Fake-Total
            Send '{"type":"cmd","name":"mind-test-initiative","args":{"force":"true"}}' | Out-Null
            Wait-Until { (MindLog) -match 'turn: initiative \d+ms' } 20000 'the initiative turn'
            if ((Fake-Total) -ne $before) { throw 'a background turn reached the online provider' }
        }
        Step 'online: a 429 is a cooldown (retried once, the next provider answers); a provider''s own count ends its day → told once → local' {
            Fake '/_mode?p=groq&mode=429' 'POST' | Out-Null
            Fake '/_mode?p=orkey&mode=full' 'POST' | Out-Null   # OpenRouter's counter reads 50 of 50 after its next answer
            $g = Fake-Count '/groq/chat/completions'
            Window-Line 'احكيلي عن حلب'
            Wait-Until { (MindLog) -match 'turn: online openrouter' } 30000 "no openrouter answer: $((Routes) -join ', ')"
            if ((Fake-Count '/groq/chat/completions') -ne $g + 2) { throw "groq asked $((Fake-Count '/groq/chat/completions') - $g) times (want 2: one try + one retry)" }
            Wait-Until { (MindLog) -notmatch 'fell through' } 1000 'a plain 429 must not end the turn online'
            Start-Sleep -Seconds 2   # the groq cooldown (Retry-After 1 s) passes; OpenRouter's counter was read after its answer
            Fake '/_mode?p=groq&mode=429day' 'POST' | Out-Null
            Wait-Until { $k = Kv 'debug-chat'; $k.typing -eq 'false' } 15000 'the turn did not end'
            Window-Line 'وشو بتحبي تعملي بالعطلة؟'
            Wait-Until { (MindLog) -match 'fell through \(quota\), told' } 30000 "not told that online is out: $(([regex]::Matches((MindLog), 'turn: online[^\r\n]*') | Select-Object -Last 1).Value)"
            Wait-Until { $k = Kv 'debug-chat'; $k.lastBrain -eq 'local' -and $k.typing -eq 'false' } 15000 'no local answer after online ran out'
            $her = @(Chat-Her)
            if (-not ($her | Where-Object { $_ -match 'خلّصت طلبات الأونلاين' })) { throw 'her «out for today» line is not in the chat' }
            $before = Fake-Total
            Window-Line 'سؤال تاني'
            Wait-Until { @(Routes)[-1] -eq 'local:no-provider' } 15000 "route after all out: $(@(Routes)[-1])"
            if (([regex]::Matches((MindLog), ', told')).Count -ne 1) { throw 'told more than once' }
            if ((Fake-Total) -ne $before) { throw 'an out provider was asked again' }
        }
        Step 'online: «محلي فقط» sends nothing (no turn, no key test, no counter)' {
            Fake '/_mode?p=groq&mode=ok' 'POST' | Out-Null
            Fake '/_mode?p=openrouter&mode=ok' 'POST' | Out-Null
            Fake '/_mode?p=orkey&mode=ok' 'POST' | Out-Null
            $s = (Settings-Json | ConvertFrom-Json)
            $s | Add-Member -NotePropertyName mindLocalOnly -NotePropertyValue $true -Force
            Write-Settings ($s | ConvertTo-Json -Depth 5)
            Reload-Settings | Out-Null
            $before = Fake-Total
            Window-Line 'شو رأيك بالقمر؟'
            Wait-Until { @(Routes)[-1] -eq 'local:local-only' } 15000 "route: $(@(Routes)[-1])"
            Send '{"type":"cmd","name":"mind-online","args":{"op":"test","provider":"groq"}}' | Out-Null
            Send '{"type":"cmd","name":"mind-online","args":{"op":"status"}}' | Out-Null
            Start-Sleep -Seconds 3
            if ((Fake-Total) -ne $before) { throw "«محلي فقط» but $((Fake-Total) - $before) request(s) went out" }
            if ((Kv 'debug-chat').chip -ne 'hidden') { throw 'the chip shows with «محلي فقط»' }
            Send '{"type":"cmd","name":"chat-close"}' | Out-Null
        }
        Step 'online: no log holds a key, his words or her words' {
            foreach ($f in (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log'), (Join-Path $env:LOCALAPPDATA 'Sahar\settings.log'), (Join-Path $onHome 'mind.log')) {
                if (-not (Test-Path $f)) { continue }
                $bad = @(Get-Content $f -Encoding utf8 | Where-Object { $_ -match 'good-groq|good-openrouter|bad-openrouter|SENTINELDEBT|القهوة|جواب أونلاين|العطلة|Bearer' })
                if ($bad.Count) { throw "$(Split-Path $f -Leaf) holds a key or words:`n      $($bad -join "`n      ")" }
            }
        }
    }
    finally {
        Stop-SettingsApp
        Stop-Engine
        Get-Process -Name 'Sahar.Mind' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        if ($fake -and -not $fake.HasExited) { $fake.Kill() }
        Copy-Item (Join-Path $onHome 'mind.log') (Join-Path $Out 'mind-online.log') -ErrorAction SilentlyContinue
        Remove-Item Env:SAHAR_MIND_HOME, Env:SAHAR_MIND_SCRIPT, Env:SAHAR_MIND_ONLINE_BASE -ErrorAction SilentlyContinue
        if ($kept) { Set-Content -Path $script:SettingsFile -Value $kept -Encoding UTF8 } else { Remove-Item $script:SettingsFile -ErrorAction SilentlyContinue }
        Remove-Item -Recurse -Force $onHome -ErrorAction SilentlyContinue
    }
}
