# 0.14 stage 'mind', stream A (dot-sourced by run.ps1; uses its helpers: Step, Start-Engine, Send, Kv, Kva, Wait-Until, …).
# Her brain's runtime on a CPU-only runner: the pinned llama.cpp CPU build next to Sahar.Mind.exe and the tiny pinned
# model (tools/mind/fetch.ps1 -Model). Never the 5 GB model.
function Mind-Kv { Kv 'debug-mind' }
function Mind-Procs([string]$name) { @(Get-Process -Name $name -ErrorAction SilentlyContinue).Count }

function Stage-MindRuntime {
    $root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $pins = Get-Content (Join-Path $root 'tools\mind\pins.json') -Raw | ConvertFrom-Json
    $model = Join-Path $root "artifacts\mind\ci-model\$($pins.ciModel.file)"
    $home0 = Join-Path $env:TEMP ("sahar-mind-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path (Join-Path $home0 'models') | Out-Null
    $env:SAHAR_MIND_HOME = $home0
    $env:SAHAR_MIND_FAST_IDLE = '1'
    # The CPU runner bench: 6 dialect + 6 command rows (≈ 2.7 s a row measured on run 43510183582); the app runs 60.
    $env:SAHAR_MIND_BENCH_ROWS = '6'
    $kept = Settings-Json
    try {
        Step 'mind: the pieces are there (Mind exe, CPU runtime, the tiny model)' {
            $dir = Split-Path $Exe
            if (-not (Test-Path (Join-Path $dir 'Sahar.Mind.exe'))) { throw 'no Sahar.Mind.exe next to the engine' }
            if (-not (Test-Path (Join-Path $dir 'runtime\cpu\llama-server.exe'))) { throw 'no runtime\cpu\llama-server.exe' }
            if (-not (Test-Path $model)) { throw "no CI model at $model" }
            $probe = & (Join-Path $dir 'Sahar.Mind.exe') --probe | Out-String
            Write-Host "    probe: $($probe.Trim())"
            if ($probe -notmatch 'tier=') { throw 'the probe printed nothing' }
        }
        Step 'mind: the engine at rest, then with the model loaded (spec §3.12: the Mind client adds < 2 MB)' {
            Write-Settings '{ "helloDone": true, "mindEnabled": true, "mindUnloadMinutes": 1 }'
            $manifest = @{ models = @(@{ id = 'ci-tiny'; name = 'CI tiny'; origin = 'Local'; role = 'ci'; bytes = $pins.ciModel.bytes; sha256 = $pins.ciModel.sha256;
                file = $pins.ciModel.file; localPath = "$model"; quant = 'Q8_0'; license = 'Gemma'; installed = $true; active = $false; ability = 'Unknown' }); active = 'ci-tiny' }
            Set-Content -Path (Join-Path $home0 'models\models.json') -Value ($manifest | ConvertTo-Json -Depth 5) -Encoding UTF8
            Start-Engine
            Start-Sleep -Seconds 20
            $rest = (State).workingSetBytes
            Send '{"type":"cmd","name":"mind-warm"}' | Out-Null
            Wait-Until { (Mind-Procs 'llama-server') -ge 1 -and (Mind-Kv).phase -in 'ready', 'idle' } 120000 'llama-server ready'
            Start-Sleep -Seconds 5
            $loaded = (State).workingSetBytes
            Write-Host ("    engine working set: {0:N1} MB at rest, {1:N1} MB with the model loaded (+{2:N1} MB)" -f ($rest / 1MB), ($loaded / 1MB), (($loaded - $rest) / 1MB))
            if ($loaded -ge 55MB) { throw "engine working set $($loaded / 1MB) MB with the model loaded" }
            if ($loaded - $rest -gt 3MB) { throw "the Mind client costs the engine $((($loaded - $rest) / 1MB)) MB" }
        }
        Step 'mind: the fast path never reaches her brain («مؤقت ٥»)' {
            $starts = (Mind-Kv).starts
            $turns = ([regex]::Matches((Get-Content (Join-Path $home0 'mind.log') -Raw), 'turn: ')).Count
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            $t = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('مؤقت ٥'))
            Send ('{"type":"cmd","name":"menu-chat","args":{"text64":"' + $t + '"}}') | Out-Null
            Start-Sleep -Milliseconds 800
            if ((Mind-Kv).starts -ne $starts) { throw 'the Mind was started for a fast-path command' }
            if (([regex]::Matches((Get-Content (Join-Path $home0 'mind.log') -Raw), 'turn: ')).Count -ne $turns) { throw 'a fast-path command reached the Mind' }
            if ((Kv 'debug-timer').running -ne 'true') { throw 'the timer did not start' }
            Send '{"type":"cmd","name":"menu-close"}' | Out-Null
        }
        Step 'mind: the tiny model loads on the CPU runtime (warm) and answers a quick bench' {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            Send '{"type":"cmd","name":"mind-warm"}' | Out-Null
            Wait-Until { (Mind-Kv).phase -in 'ready', 'idle' -and (Mind-Procs 'llama-server') -ge 1 } 120000 'llama-server ready'
            Write-Host "    warm in $($sw.ElapsedMilliseconds) ms"
            if ((Mind-Kv).active -ne 'ci-tiny') { throw "active model: $((Mind-Kv).active)" }
            $log0 = (Get-Content (Join-Path $home0 'mind.log') -Raw)
            Send '{"type":"cmd","name":"mind-bench","args":{"quick":"true"}}' | Out-Null
            Wait-Until { (Get-Content (Join-Path $home0 'mind.log') -Raw) -match 'bench: load=(\d+)ms first=(\d+)ms tps=([\d.]+)' } 180000 'the quick bench'
            $b = [regex]::Match((Get-Content (Join-Path $home0 'mind.log') -Raw), 'bench: load=(\d+)ms first=(\d+)ms tps=([\d.]+)')
            Write-Host "    bench: load $($b.Groups[1].Value) ms, first token $($b.Groups[2].Value) ms, $($b.Groups[3].Value) tok/s"
            if ([double]$b.Groups[3].Value -le 0) { throw 'no tokens per second: the client read nothing' }
            if ($log0 -match 'runtime: ready') { Write-Host '    (warm logged ready)' }
        }
        Step 'mind: «النماذج» shows her model; a full «جرّبيني» gives its numbers (and «حكي بس» when it cannot run commands)' {
            Start-SettingsApp | Out-Null
            SNav 'models'
            Wait-Until { (Js "document.getElementById('mine-list')?.textContent ?? ''") -match 'CI tiny' } 5000 'the CI model is not under «نماذجك»'
            SClick '#bench-ci-tiny'
            Wait-Until { (Get-Content (Join-Path $home0 'mind.log') -Raw) -match 'tools=\d+/6' } 180000 'the bench (6 + 6 rows on the CPU runner)'
            Wait-Until { (Js "document.querySelectorAll('#mine-list .m-stat').length") -ge 4 } 8000 'the stat tiles'
            $b = [regex]::Matches((Get-Content (Join-Path $home0 'mind.log') -Raw), 'dialect=(\d+)/(\d+) tools=(\d+)/(\d+)') | Select-Object -Last 1
            Write-Host "    full bench: dialect $($b.Groups[1].Value)/$($b.Groups[2].Value), tools $($b.Groups[3].Value)/$($b.Groups[4].Value)"
            if ([int]$b.Groups[3].Value * 5 -lt [int]$b.Groups[4].Value * 4) {
                Wait-Until { (Js "document.getElementById('mine-list').textContent") -match 'حكي بس' } 5000 'no «حكي بس» badge for a chat-only model'
            }
            foreach ($theme in 'light', 'dark') {
                SOk 'emulate' ('{"theme":"' + $theme + '"}')
                Start-Sleep -Milliseconds 600
                Capture "app-models-ci-$theme"
                SNav 'brain'
                Start-Sleep -Milliseconds 600
                Capture "app-brain-ci-$theme"
                SNav 'models'
            }
            SOk 'emulate' '{"theme":""}'
            Stop-SettingsApp
        }
        Step 'mind: idle for a minute → llama-server is gone (0 MB at rest)' {
            Wait-Until { (Mind-Procs 'llama-server') -eq 0 } 120000 'llama-server still running after the idle minute'
            if ((Get-Content (Join-Path $home0 'mind.log') -Raw) -notmatch 'runtime: unloaded') { throw 'mind.log does not say unloaded' }
        }
        Step 'mind: logs hold no typed text' {
            $t = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('SENTINELWORDS بدي احكي معك'))
            Send '{"type":"cmd","name":"menu-open"}' | Out-Null
            Send ('{"type":"cmd","name":"menu-chat","args":{"text64":"' + $t + '"}}') | Out-Null
            Start-Sleep -Seconds 3
            Send '{"type":"cmd","name":"menu-close"}' | Out-Null
            foreach ($f in (Join-Path $env:LOCALAPPDATA 'Sahar\engine.log'), (Join-Path $home0 'mind.log')) {
                if (-not (Test-Path $f)) { continue }
                $bad = @(Get-Content $f | Where-Object { $_ -match 'SENTINELWORDS|gemma-3-270m|\\Users\\' } | ForEach-Object { $_ -replace [regex]::Escape($env:USERNAME), '<user>' -replace 'RUNNER~1', '<user>' })
                if ($bad.Count) { throw "$(Split-Path $f -Leaf) holds text, a file name or a path:`n      $($bad -join "`n      ")" }
            }
        }
        Step 'mind: when the engine quits, the Mind and llama-server go too' {
            Send '{"type":"cmd","name":"mind-warm"}' | Out-Null
            Wait-Until { (Mind-Procs 'llama-server') -ge 1 } 120000 'llama-server for the quit check'
            Stop-Engine
            Wait-Until { (Mind-Procs 'Sahar.Mind') -eq 0 -and (Mind-Procs 'llama-server') -eq 0 } 15000 'the Mind or llama-server outlived the engine'
        }
    }
    finally {
        Stop-Engine
        Get-Process -Name 'llama-server', 'Sahar.Mind' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Copy-Item (Join-Path $home0 'mind.log') (Join-Path $Out 'mind.log') -ErrorAction SilentlyContinue
        Remove-Item Env:SAHAR_MIND_HOME, Env:SAHAR_MIND_FAST_IDLE, Env:SAHAR_MIND_BENCH_ROWS -ErrorAction SilentlyContinue
        if ($kept) { Set-Content -Path $script:SettingsFile -Value $kept -Encoding UTF8 } else { Remove-Item $script:SettingsFile -ErrorAction SilentlyContinue }
        Remove-Item -Recurse -Force $home0 -ErrorAction SilentlyContinue
    }
}
