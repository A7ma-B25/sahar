# 0.14.1 opt-in stage 'mindeval' (dot-sourced by run.ps1; never in "ui-test all": mind-eval.yml runs it by hand).
# Her REAL brain end to end on the runner's CPU: the engine sends each golden line from the card's chat field exactly as
# he types it, the Mind runs the real pinned model (tools/mind/fetch.ps1 -EvalModel), and the stage records what became
# VISIBLE — the card's header line, her bubble, or the list bubble (a draft) — plus the tools her brain called (engine.log
# kinds) and whether the fast path took the line. Prompts and her replies are test data: they go to the artifact only
# (mind-eval.jsonl); her own logs are still checked for text. tools/ci/mind_eval_score.py scores the file.
function Eval-Lines([string]$path) { if (Test-Path $path) { @(Get-Content $path -Encoding utf8) } else { @() } }
function Eval-Turns([string]$mindLog) { if (Test-Path $mindLog) { ([regex]::Matches((Get-Content $mindLog -Raw -Encoding utf8), '(?m) turn: (chat|draft|voice) \d+ms')).Count } else { 0 } }

function Stage-MindEval {
    $root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $pins = Get-Content (Join-Path $root 'tools\mind\pins.json') -Raw | ConvertFrom-Json
    $id = if ($env:SAHAR_EVAL_MODEL) { $env:SAHAR_EVAL_MODEL } else { 'gemma-4-e4b-q4km' }
    $pin = @($pins.evalModels) | Where-Object id -eq $id
    $model = Join-Path $root "artifacts\mind\eval-model\$($pin.file)"
    $budget = if ($env:SAHAR_EVAL_BUDGET_S) { [int]$env:SAHAR_EVAL_BUDGET_S } else { 240 }
    $only = if ($env:SAHAR_EVAL_ONLY) { @($env:SAHAR_EVAL_ONLY -split ',') } else { @() }
    $rows = @(Get-Content (Join-Path $root 'tests\mind\golden\eval.jsonl') -Encoding utf8 | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object { $only.Count -eq 0 -or $only -contains $_.id })
    $home0 = Join-Path $env:TEMP ("sahar-eval-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path (Join-Path $home0 'models') | Out-Null
    $env:SAHAR_MIND_HOME = $home0
    $env:SAHAR_MIND_CHAT_BUDGET_S = "$budget"
    $mindLog = Join-Path $home0 'mind.log'
    $engineLog = Join-Path $env:LOCALAPPDATA 'Sahar\engine.log'
    $outFile = Join-Path $Out 'mind-eval.jsonl'
    Remove-Item $outFile -ErrorAction SilentlyContinue
    $kept = Settings-Json
    try {
        Step "mindeval: the real model is there ($id, pinned SHA-256)" {
            if (-not $pin) { throw "no evalModels pin '$id'" }
            if (-not (Test-Path $model)) { throw "no eval model at $model" }
            if ((Get-Item $model).Length -ne $pin.bytes) { throw "$($pin.file) is $((Get-Item $model).Length) bytes, pinned $($pin.bytes)" }
            $probe = & (Join-Path (Split-Path $Exe) 'Sahar.Mind.exe') --probe | Out-String
            Write-Host "    probe: $($probe.Trim())"
        }
        Step "mindeval: her brain loads $id on the CPU runtime" {
            Write-Settings '{ "helloDone": true, "mindEnabled": true, "mindUnloadMinutes": 60, "mindInitiative": "off", "userGender": "male" }'
            $manifest = @{ models = @(@{ id = $id; name = $id; origin = 'Recommended'; role = 'default'; bytes = $pin.bytes; sha256 = $pin.sha256;
                file = $pin.file; localPath = "$model"; installed = $true; active = $false; ability = 'Unknown' }); active = $id }
            Set-Content -Path (Join-Path $home0 'models\models.json') -Value ($manifest | ConvertTo-Json -Depth 5) -Encoding UTF8
            Start-Engine
            Start-Sleep -Seconds 5
            $sw = [Diagnostics.Stopwatch]::StartNew()
            Send '{"type":"cmd","name":"mind-warm"}' | Out-Null
            Wait-Until { (Get-Content $mindLog -Raw -ErrorAction SilentlyContinue) -match 'runtime: ready' } 600000 'llama-server ready with the real model'
            Write-Host "    warm in $($sw.ElapsedMilliseconds) ms; $(([regex]::Match((Get-Content $mindLog -Raw), 'runtime: ready[^\r\n]*')).Value)"
        }
        foreach ($row in $rows) {
            Step "mindeval: $($row.id) ($($row.kind))" {
                Send '{"type":"cmd","name":"game-stop"}' | Out-Null
                Send '{"type":"cmd","name":"list-close"}' | Out-Null
                if ((Menu).open -ne 'true') { Send '{"type":"cmd","name":"menu-open"}' | Out-Null; Wait-Menu 'true' 3000 'card open' | Out-Null }
                Start-Sleep -Milliseconds 400
                $e0 = (Eval-Lines $engineLog).Count
                $t0 = Eval-Turns $mindLog
                $before = Text64 (Menu).line64
                $t64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($row.text))
                $sw = [Diagnostics.Stopwatch]::StartNew()
                Send ('{"type":"cmd","name":"menu-chat","args":{"text64":"' + $t64 + '"}}') | Out-Null
                $seen = [System.Collections.Generic.List[string]]::new()
                $where = [System.Collections.Generic.List[string]]::new()
                $mindTurn = $false; $asked = 0; $endAt = $null; $first = $null
                while ($sw.Elapsed.TotalSeconds -lt $budget + 60) {
                    Start-Sleep -Milliseconds 250
                    $new = @(Eval-Lines $engineLog | Select-Object -Skip $e0)
                    if (-not $mindTurn -and ($new -match 'mind: turn (chat|draft)')) { $mindTurn = $true }
                    $m = Menu
                    if ($m.open -eq 'true') {
                        $l = Text64 $m.line64
                        if ($l -and $l -ne $before -and $l -ne 'عم فكّر…' -and -not $seen.Contains($l)) { $seen.Add($l); $where.Add('header'); if (-not $first) { $first = $sw.ElapsedMilliseconds } }
                        if (($m.buttons | Where-Object id -eq 'ask-yes') -and $asked -lt 2) {
                            Start-Sleep -Milliseconds 900   # past the answer guard
                            Press 'ask-yes' | Out-Null; $asked++
                        }
                    }
                    $c = Kv 'debug-comment'
                    if ($c.showing -eq 'true') { $l = Text64 $c.text64; if ($l -and -not $seen.Contains($l)) { $seen.Add($l); $where.Add('bubble'); if (-not $first) { $first = $sw.ElapsedMilliseconds } } }
                    $li = Kv 'debug-list'
                    if ($li.open -eq 'true') { $l = Text64 $li.lines64; if ($l -and -not $seen.Contains($l)) { $seen.Add($l); $where.Add('list'); if (-not $first) { $first = $sw.ElapsedMilliseconds } } }
                    $turnsDone = (Eval-Turns $mindLog) -gt $t0
                    if (-not $endAt -and ((-not $mindTurn -and $sw.ElapsedMilliseconds -gt 3000) -or ($mindTurn -and $turnsDone))) { $endAt = $sw.ElapsedMilliseconds }
                    if ($endAt -and $sw.ElapsedMilliseconds -gt $endAt + 1200) { break }
                }
                $new = @(Eval-Lines $engineLog | Select-Object -Skip $e0)
                $calls = @($new | ForEach-Object { if ($_ -match 'mind: call (\S+) (\S+)') { "$($Matches[1]) $($Matches[2])" } })
                $errors = @($new | ForEach-Object { if ($_ -match 'mind: turn error (\S+)') { $Matches[1] } })
                $drops = @(Eval-Lines $mindLog | Select-Object -Last 40 | Where-Object { $_ -match ' (say|turn): (dropped|bad-output|thinking|failed)' } | ForEach-Object { ($_ -split ' ', 3)[2] })
                if ($row.key) { Screenshot "mind-eval-$($row.id).png" | Out-Null }
                $visible = if ($seen.Count) { $seen[$seen.Count - 1] } else { '' }
                $rec = [ordered]@{ id = $row.id; kind = $row.kind; prompt = $row.text; expectTool = $row.tool; visible = $visible; seen = @($seen); where = @($where);
                    calls = $calls; fastPath = -not $mindTurn; errors = $errors; drops = $drops; asked = $asked; firstMs = $first; totalMs = $endAt; model = $id }
                Add-Content -Path $outFile -Value ($rec | ConvertTo-Json -Compress -Depth 4) -Encoding utf8
                Write-Host "    [$($row.id)] $($row.text) -> $(if ($visible) { $visible } else { '(nothing visible)' })  calls=[$($calls -join '; ')] fast=$(-not $mindTurn) errors=[$($errors -join ',')] $($endAt) ms"
                if ($row.key -and -not $visible) { throw "$($row.id): nothing visible for «$($row.text)»" }
                if ($row.key -and $visible -match 'ما فهمت عليك منيح|عم فكّر كتير') { throw "$($row.id): her fallback line instead of a reply: $visible" }
            }
        }
        Step 'mindeval: her logs hold no prompt or reply text' {
            $words = @($rows | ForEach-Object { ($_.text -split '\s+' | Where-Object { $_.Length -ge 5 }) | Select-Object -First 1 })
            foreach ($f in $engineLog, $mindLog) {
                $text = Get-Content $f -Raw -Encoding utf8
                $hit = @($words | Where-Object { $text.Contains($_) })
                if ($hit.Count) { throw "$(Split-Path $f -Leaf) holds prompt words: $($hit -join ' ')" }
            }
        }
    }
    finally {
        Stop-Engine
        Get-Process -Name 'llama-server', 'Sahar.Mind' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Copy-Item $mindLog (Join-Path $Out 'mind.log') -ErrorAction SilentlyContinue
        Remove-Item Env:SAHAR_MIND_HOME, Env:SAHAR_MIND_CHAT_BUDGET_S -ErrorAction SilentlyContinue
        if ($kept) { Set-Content -Path $script:SettingsFile -Value $kept -Encoding UTF8 } else { Remove-Item $script:SettingsFile -ErrorAction SilentlyContinue }
        Remove-Item -Recurse -Force $home0 -ErrorAction SilentlyContinue
    }
}
