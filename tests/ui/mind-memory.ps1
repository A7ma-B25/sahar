# 0.14 stage 'mind', stream B (dot-sourced by run.ps1; uses its helpers: Step, Start-Engine, Send, Kv, …).
# Personality and memory: music learning off keeps nothing, on keeps it encrypted (DPAPI in the AOT engine); «شو فهمت
# عنك» in light and dark (app-you-*.png); confirm / reject round trip; a WhatsApp import previewed, consent asked,
# only his lines kept; a draft bubble with «بصوتك»; «انسي كل شي» leaves nothing in mind\; logs hold no text.
function Mind-Dir { if ($env:SAHAR_MIND_HOME) { Join-Path $env:SAHAR_MIND_HOME 'mind' } else { Join-Path $env:APPDATA 'Sahar\mind' } }
function Mind-Files { $d = Mind-Dir; if (Test-Path $d) { @(Get-ChildItem -Path $d -Force -Recurse -File) } else { @() } }
function B64([string]$s) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) }
function Exists([string]$sel) { (Js ("document.querySelector(" + ($sel | ConvertTo-Json) + ") !== null")) -eq $true }

function Stage-MindMemory {
    $song = 'Shams Al-Sahar Test Song'
    $secret = 'هلق جاي بس خلّص الشغل'
    Step 'mind (memory): a fresh start' {
        Stop-SettingsApp
        Stop-Engine
        $d = Mind-Dir
        if (Test-Path $d) { Remove-Item -Recurse -Force $d }
        Write-Settings '{ "helloDone": true, "userName": "أحمد", "userGender": "male", "mindLearnMusic": false, "mindLearnChats": true }'
        Start-Engine
    }
    Step 'mind (memory): music learning off keeps nothing' {
        $r = Kva 'debug-mind-music' ('{"title64":"' + (B64 $song) + '","artist64":"' + (B64 'Fairuz') + '"}')
        if ($r.kept -ne 'false') { throw "kept=$($r.kept) with music learning off" }
        $files = Mind-Files
        if ($files.Count -ne 0) { throw "mind\ has $($files.Count) file(s) with music learning off" }
    }
    Step 'mind (memory): music learning on keeps it, encrypted with a DPAPI key' {
        Write-Settings '{ "helloDone": true, "userName": "أحمد", "userGender": "male", "mindLearnMusic": true, "mindLearnChats": true }'
        Reload-Settings | Out-Null
        $r = Kva 'debug-mind-music' ('{"title64":"' + (B64 $song) + '","artist64":"' + (B64 'Fairuz') + '"}')
        if ($r.kept -ne 'true' -or $r.count -ne '1') { throw "kept=$($r.kept) count=$($r.count)" }
        $d = Mind-Dir
        if (-not (Test-Path (Join-Path $d 'key.dpapi'))) { throw 'no key.dpapi' }
        if (-not (Test-Path (Join-Path $d 'music.enc'))) { throw 'no music.enc' }
        foreach ($f in Mind-Files) {
            $bytes = [IO.File]::ReadAllBytes($f.FullName)
            foreach ($enc in [Text.Encoding]::UTF8, [Text.Encoding]::Unicode) {
                if ($enc.GetString($bytes).Contains('Shams')) { throw "$($f.Name) holds the title in plain text" }
            }
        }
        # The key is DPAPI's, not a plain test key (the AOT engine took the Windows path).
        $key = [IO.File]::ReadAllBytes((Join-Path $d 'key.dpapi'))
        if ([Text.Encoding]::ASCII.GetString($key).StartsWith('SAHAR-TEST-WRAP')) { throw 'the key is wrapped by the test wrap, not DPAPI' }
        if ($key.Length -lt 100) { throw "key.dpapi is $($key.Length) bytes: not a DPAPI blob" }
    }
    Step 'mind (memory): her proposals go in through the real store' {
        foreach ($p in @(
            @('الصبح بتحب الجواب قصير.', 'style', '0', 'false'), @('الصبح بتحب الجواب قصير.', 'style', '1', 'false'),
            @('الصبح بتحب الجواب قصير.', 'style', '2', 'false'), @('بعد جلسة شغل طويلة بتحب نكتة صغيرة.', 'whatHelps', '0', 'false'),
            @('بعد جلسة شغل طويلة بتحب نكتة صغيرة.', 'whatHelps', '3', 'false'),
            @('لما تكون مضغوط: خطوات صغيرة وعملية، مش حكي كتير.', 'whatHelps', '11', 'true'),
            @('بتشتغل أحسن بالليل.', 'routines', '4', 'true'), @('بدك تخلّص المشروع يوم الجمعة.', 'goals', '2', 'true'))) {
            $r = Kva 'debug-mind-propose' ('{"text64":"' + (B64 $p[0]) + '","category":"' + $p[1] + '","daysAgo":"' + $p[2] + '","confirm":"' + $p[3] + '"}')
            if ($r.ok -ne 'true') { throw "propose refused: $($p[0])" }
        }
    }
    Step 'mind (memory): a WhatsApp chat — preview, consent asked, only his lines kept' {
        Start-SettingsApp | Out-Null
        [Mouse]::Move([int]$Work.Right - 2, [int]$Work.Bottom - 2)
        SNav 'you'
        Wait-Until { (Text64 (SKv 'state').h1_64) -eq 'شو فهمت عنك' } 3000 'the page title'
        $chat = (@(
            '13/11/2026, 21:41 - أحمد: يلا تمام',
            '13/11/2026, 21:42 - سامر: وينك يا رجل',
            "13/11/2026, 21:43 - أحمد: $secret",
            'وبعتلك الرقم 0944123456',
            '13/11/2026, 21:44 - أحمد: تمام منيح 😄',
            '13/11/2026, 21:45 - أحمد: يلا بشوفك',
            '13/11/2026, 21:46 - أحمد: كتير حلو تمام',
            '13/11/2026, 21:47 - أحمد: شو عم تعمل هلق؟ يلا') -join "`n")
        Js ("window.sahar.importText(" + ($chat | ConvertTo-Json) + ", 'WhatsApp Chat.txt'); true") | Out-Null
        Wait-Until { Exists '#import-preview' } 5000 'no import preview'
        Wait-Until { (Js "document.getElementById('import-me')?.value") -eq 'أحمد' } 3000 'his name is not picked from settings'
        $sample = Js "[...document.querySelectorAll('#import-sample li')].map(l => l.textContent).join('|')"
        if ($sample -match 'وينك') { throw "the other person's line is in the preview: $sample" }
        if ($sample -notmatch '\[رقم\]') { throw "the phone number was not scrubbed: $sample" }
        if ((Mind-Files | Where-Object Name -eq 'style.enc')) { throw 'the preview kept something' }
        SClick '#import-save'
        Wait-Until { Exists '#import-consent-yes' } 3000 'no consent question (imports are opt-in)'
        SClick '#import-consent-yes'
        Wait-Settings '"mindLearnImports"\s*:\s*true' 3000 'imports consent saved'
        Wait-Until { (SText '#import-status') -match 'حفظت' } 5000 'no «حفظت»'
        Wait-Until { (SText '#style-summary') -match 'شامي' } 3000 "style card: $(SText '#style-summary')"
    }
    Step 'mind (memory): «شو فهمت عنك» light and dark' {
        foreach ($theme in 'light', 'dark') {
            SOk 'emulate' ('{"theme":"' + $theme + '"}')
            Wait-Until { (SKv 'state').theme -eq $theme } 3000 "the page is not $theme"
            SNav 'today'
            SNav 'you'
            Start-Sleep -Milliseconds 700
            $st = SKv 'state'
            if ($st.blocked -ne '0') { throw "blocked=$($st.blocked)" }
            if (-not (Exists '#prop-0') -or -not (Exists '#prop-1')) { throw 'two proposals are not shown' }
            if (-not (Exists '#conf-2')) { throw 'three confirmed items are not shown' }
            if ((SText '#prop-0') -notmatch 'نص نص') { throw "confidence chip: $(SText '#prop-0')" }
            Capture "app-you-$theme"
        }
        SOk 'emulate' '{"theme":""}'
    }
    Step 'mind (memory): confirm and reject round trip' {
        $first = Js "document.querySelector('#prop-0 .mm-soft-text').textContent"
        SClick '#prop-0-yes'
        Wait-Until { (Js "[...document.querySelectorAll('#you-confirmed .label')].map(l => l.textContent).join('|')") -match [regex]::Escape($first) } 3000 'the confirmed item did not move'
        SClick '#prop-0-no'
        Wait-Until { Exists '#you-proposed-none' } 3000 'the rejected item stayed'
        # «لا» is kept as a hash only: the same words are not proposed again.
        $r = Kva 'debug-mind-propose' ('{"text64":"' + (B64 'بعد جلسة شغل طويلة بتحب نكتة صغيرة.') + '","category":"whatHelps"}')
        if ($r.ok -ne 'false') { throw 'a rejected item was proposed again' }
        SNav 'today'; SNav 'you'
        if (-not (Exists '#you-proposed-none')) { throw 'it came back after a reload' }
    }
    Step 'mind (memory): a draft in his voice — «بصوتك», copy, never sent' {
        $r = Kva 'debug-mind-draft' ('{"text64":"' + (B64 'يلا خلص، منحكي بكرا 👍') + '"}')
        if ($r.open -ne 'true') { throw 'no draft bubble' }
        if ((Text64 $r.button64) -notmatch 'انسخها') { throw "button: $(Text64 $r.button64)" }
        $shot = Screenshot 'mind-draft.png'
        Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    }
    Step 'mind (memory): «انسي كل شي» leaves nothing in mind\' {
        if ((Mind-Files).Count -lt 3) { throw "mind\ has $((Mind-Files).Count) file(s) before forgetting" }
        SClick '#forget-ask'
        SClick '#forget-yes'
        Wait-Until { (SText '#forget-status') -match 'نسيت كل شي' } 5000 "forget status: $(SText '#forget-status')"
        $left = Mind-Files
        if ($left.Count -ne 0) { throw "mind\ still has: $(($left | ForEach-Object Name) -join ', ')" }
        if (-not (Test-Path $script:SettingsFile)) { throw 'settings went with it' }
        if (-not (Exists '#you-proposed-none') -or (Exists '#conf-0')) { throw 'the page still shows items' }
        Wait-Until { (Text64 (Kv 'debug-comment').text64) -match 'نسيت كل شي' } 3000 'she did not say «نسيت كل شي»'
    }
    Step 'mind (memory): logs hold kinds and counts, never his words or titles' {
        $logs = @((Join-Path $env:LOCALAPPDATA 'Sahar\engine.log'), (Join-Path $env:LOCALAPPDATA 'Sahar\settings.log')) | Where-Object { Test-Path $_ }
        foreach ($l in $logs) {
            $text = Get-Content $l -Raw -Encoding UTF8
            foreach ($word in @('Shams', 'Fairuz', 'خلّص الشغل', 'يلا تمام', 'وينك', 'منحكي بكرا', 'نكتة صغيرة', 'أحمد', 'سامر')) {
                if ($text.Contains($word)) { throw "$(Split-Path $l -Leaf) holds '$word'" }
            }
        }
        if (-not ((Get-Content (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log') -Raw) -match 'mind: forgot everything')) { throw 'no forget line in the log' }
    }
}
