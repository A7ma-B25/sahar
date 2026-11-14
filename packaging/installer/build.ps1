# Builds Sahar-Setup-<version>.exe and its .sha256 ("<hex>  <name>", as sha256sum writes) from the published engine
# folder as the 0.14 build lays it out: Sahar.Engine.exe, Sahar.Mind.exe, Sahar.Settings\ (self-contained, web\ inside),
# and the pinned llama.cpp runtime (-Runtime: vulkan\ and cpu\, default <engine>\runtime). Never a model (*.gguf).
# Inno Setup is not on the windows-latest image (runner-images #11644): the pinned official build is downloaded from
# its release page (linked from jrsoftware.org), its SHA-256 checked, and installed into a tools folder of its own —
# never an unknown copy, so every build uses the same compiler and the same Arabic.isl.
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Engine = "$PSScriptRoot\..\..\artifacts\engine",
    [string]$Runtime = "",
    [string]$Out = "$PSScriptRoot\..\..\artifacts\installer",
    [string]$Tools = $(if ($env:RUNNER_TEMP) { "$env:RUNNER_TEMP\inno-6.7.4" } else { "$env:TEMP\inno-6.7.4" })
)
$ErrorActionPreference = 'Stop'
$InnoUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe'
$InnoSha256 = 'a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c'

$iscc = Join-Path $Tools 'ISCC.exe'
if (-not (Test-Path $iscc)) {
    $download = Join-Path ([System.IO.Path]::GetTempPath()) "innosetup-6.7.4-$([guid]::NewGuid().ToString('N')).exe"
    Write-Host "downloading Inno Setup 6.7.4 (official release)"
    for ($try = 1; ; $try++) {
        try { Invoke-WebRequest -Uri $InnoUrl -OutFile $download -UseBasicParsing; break }
        catch {
            if ($try -ge 3) { throw "Inno Setup download failed 3 times: $_" }
            Write-Host "download failed ($_), trying again"
            Start-Sleep -Seconds (5 * $try)
        }
    }
    $hash = (Get-FileHash $download -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $InnoSha256) { Remove-Item $download -Force; throw "Inno Setup download has SHA-256 $hash, expected $InnoSha256" }
    $p = Start-Process -FilePath $download -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', "/DIR=`"$Tools`"" -Wait -PassThru
    Remove-Item $download -Force
    if ($p.ExitCode -ne 0) { throw "Inno Setup install failed (exit $($p.ExitCode))" }
    if (-not (Test-Path $iscc)) { throw "ISCC.exe not found in $Tools after the install" }
}
if (-not (Test-Path (Join-Path $Tools 'Languages\Arabic.isl'))) { throw "Arabic.isl missing from $Tools\Languages" }

$engineDir = (Resolve-Path $Engine).Path
$runtimeDir = (Resolve-Path $(if ($Runtime) { $Runtime } else { Join-Path $engineDir 'runtime' })).Path
foreach ($part in 'Sahar.Engine.exe', 'Sahar.Mind.exe', 'Sahar.Settings\Sahar.Settings.exe', 'Sahar.Settings\web\index.html') {
    if (-not (Test-Path (Join-Path $engineDir $part))) { throw "no $part in $engineDir" }
}
foreach ($part in 'vulkan\llama-server.exe', 'cpu\llama-server.exe') {
    if (-not (Test-Path (Join-Path $runtimeDir $part))) { throw "no $part in $runtimeDir" }
}
$models = @(Get-ChildItem $engineDir, $runtimeDir -Recurse -Filter *.gguf -ErrorAction SilentlyContinue)
if ($models.Count) { throw "a model would be bundled ($($models.Name -join ', ')): models are downloaded in the app" }
$thirdParty = (Resolve-Path "$PSScriptRoot\..\THIRD-PARTY.txt").Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$outDir = (Resolve-Path $Out).Path
$readme = (Resolve-Path "$PSScriptRoot\..\README-test-build.txt").Path
$numeric = ($Version -split '[-+]')[0]

& $iscc /Qp "/DAppVersion=$Version" "/DAppNumeric=$numeric" "/DEngineDir=$engineDir" "/DRuntimeDir=$runtimeDir" "/DReadmeFile=$readme" "/DThirdPartyFile=$thirdParty" "/DOutputDir=$outDir" "$PSScriptRoot\Sahar.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$name = "Sahar-Setup-$Version.exe"
$setup = Join-Path $outDir $name
if (-not (Test-Path $setup)) { throw "$name was not written" }
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$setup.sha256", "$sha  $name`n", [System.Text.UTF8Encoding]::new($false))
$bytes = (Get-Item $setup).Length
Write-Host "$name $bytes bytes, sha256 $sha"
Write-Host ("setup size: {0:0.0} MB" -f ($bytes / 1MB))
