# Builds Sahar-Setup-<version>.exe and its .sha256 ("<hex>  <name>", as sha256sum writes) from the published engine.
# Inno Setup is not on the windows-latest image (runner-images #11644): the pinned official build is downloaded from
# its release page (linked from jrsoftware.org), its SHA-256 checked, and installed into a tools folder of its own —
# never an unknown copy, so every build uses the same compiler and the same Arabic.isl.
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Engine = "$PSScriptRoot\..\..\artifacts\engine",
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
if (-not (Test-Path (Join-Path $engineDir 'Sahar.Engine.exe'))) { throw "no Sahar.Engine.exe in $engineDir" }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$outDir = (Resolve-Path $Out).Path
$readme = (Resolve-Path "$PSScriptRoot\..\README-test-build.txt").Path
$numeric = ($Version -split '[-+]')[0]

& $iscc /Qp "/DAppVersion=$Version" "/DAppNumeric=$numeric" "/DEngineDir=$engineDir" "/DReadmeFile=$readme" "/DOutputDir=$outDir" "$PSScriptRoot\Sahar.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$name = "Sahar-Setup-$Version.exe"
$setup = Join-Path $outDir $name
if (-not (Test-Path $setup)) { throw "$name was not written" }
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$setup.sha256", "$sha  $name`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "$name $((Get-Item $setup).Length) bytes, sha256 $sha"
