# Sahar 0.14: fetches her brain's pinned pieces on a Windows runner and checks every byte (tools/mind/pins.json).
#
#   -Runtime vulkan,cpu   the official llama.cpp release zips from github.com/ggml-org/llama.cpp/releases/download/<build>/;
#                         each is checked against (1) the size and SHA-256 pinned in pins.json and (2) the digest GitHub
#                         publishes for the release asset (gh release view, GH_TOKEN). A mismatch fails. A pin that is
#                         still null: every variant is fetched, the SHA-256 and GitHub's digest are printed (PIN lines)
#                         for the lead to commit, and the run warns (the Core test RuntimePins_AreSet stays red).
#                         Unzipped to <Out>/runtime/<variant>/ keeping llama-server.exe, the DLLs and the licence only.
#   -Model                the CI model from Hugging Face at the pinned commit, checked against its pinned SHA-256,
#                         to <Out>/ci-model/<file>.
param(
    [string[]]$Runtime = @(),
    [switch]$Model,
    [string]$Out = 'artifacts/mind'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$pins = Get-Content (Join-Path $PSScriptRoot 'pins.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$cache = Join-Path $Out 'downloads'
New-Item -ItemType Directory -Force -Path $cache | Out-Null

function Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }

function Fetch([string]$url, [string]$file, [long]$bytes, [string]$sha) {
    if (-not $url.StartsWith('https://github.com/ggml-org/llama.cpp/releases/download/') -and -not $url.StartsWith('https://huggingface.co/')) {
        throw "not an allowed source: $url"
    }
    $path = Join-Path $cache $file
    if ((Test-Path $path) -and $sha -and (Sha256 $path) -eq $sha) { Write-Host "  $file (cached, verified)"; return $path }
    Write-Host "  downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
    $len = (Get-Item $path).Length
    if ($bytes -and $len -ne $bytes) { throw "$file is $len bytes, pinned $bytes" }
    return $path
}

$pending = @()
if ($Runtime.Count) {
    $ll = $pins.llamaCpp
    $assets = @($ll.assets)
    if ($assets | Where-Object { -not $_.sha256 }) {
        Write-Host "::warning::llama.cpp $($ll.build): some SHA-256 pins are not set yet: fetching every variant to print them"
        $Runtime = $assets.variant
    }
    # GitHub's published digests for the release (sha256:<hex>), read with the job's token.
    $published = @{}
    $json = gh release view $ll.build -R ggml-org/llama.cpp --json assets 2>$null
    if ($LASTEXITCODE -eq 0 -and $json) {
        foreach ($a in (($json | ConvertFrom-Json).assets)) { if ($a.digest) { $published[$a.name] = ($a.digest -replace '^sha256:', '').ToLowerInvariant() } }
    } else { Write-Host '::warning::could not read the release digests with gh' }
    foreach ($variant in $Runtime) {
        $a = $assets | Where-Object variant -eq $variant
        if (-not $a) { throw "no pinned asset '$variant'" }
        $path = Fetch ($ll.base + $a.name) $a.name $a.bytes $a.sha256
        $sha = Sha256 $path
        $digest = $published[$a.name]
        Write-Host "PIN $variant $($a.name) sha256=$sha github-digest=$(if ($digest) { $digest } else { 'none' }) bytes=$((Get-Item $path).Length)"
        if ($digest -and $digest -ne $sha) { throw "$($a.name): SHA-256 $sha differs from GitHub's digest $digest" }
        if ($a.sha256) {
            if ($a.sha256 -ne $sha) { throw "$($a.name): SHA-256 $sha differs from the pin $($a.sha256)" }
        } else { $pending += $variant }
        $dir = Join-Path $Out "runtime/$variant"
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        $tmp = Join-Path $Out "unzip-$variant"
        if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
        Expand-Archive -LiteralPath $path -DestinationPath $tmp
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Get-ChildItem $tmp -Recurse -File | Where-Object { $_.Name -eq 'llama-server.exe' -or $_.Extension -eq '.dll' -or $_.Name -like 'LICENSE*' } |
            ForEach-Object { Copy-Item $_.FullName $dir }
        Remove-Item -Recurse -Force $tmp
        $kept = (Get-ChildItem $dir).Name | Sort-Object
        Write-Host "  runtime/$variant keeps: $($kept -join ', ')"
        if ($variant -ne 'cudart' -and -not (Test-Path (Join-Path $dir 'llama-server.exe'))) { throw "$($a.name) has no llama-server.exe" }
    }
}

if ($Model) {
    $m = $pins.ciModel
    $path = Fetch "https://huggingface.co/$($m.repo)/resolve/$($m.commit)/$($m.file)" $m.file $m.bytes $m.sha256
    $sha = Sha256 $path
    if ($sha -ne $m.sha256) { Remove-Item $path; throw "$($m.file): SHA-256 $sha differs from the pin $($m.sha256)" }
    $dir = Join-Path $Out 'ci-model'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item $path (Join-Path $dir $m.file) -Force
    Write-Host "  ci-model/$($m.file) verified"
}

if ($pending.Count) { Write-Host "::warning::runtime pins still to commit: $($pending -join ', ') (see the PIN lines)" }
