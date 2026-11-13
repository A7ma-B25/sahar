# Writes what Sahar's update check trusts (UpdateManifest.Verify in Core): Sahar-update.json — the version, the setup's
# file name and SHA-256, the channel — and Sahar-update.json.sig, an ECDSA P-256 signature of exactly those bytes
# (base64 of r‖s), made with the private key in $env:SAHAR_UPDATE_KEY (PKCS#8 PEM). The release workflow signs only
# on main; the install UI stage signs its fake release with a throwaway key.
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Setup,
    [Parameter(Mandatory)][string]$Out,
    [ValidateSet('test-build', 'stable')][string]$Channel = 'test-build'
)
$ErrorActionPreference = 'Stop'
if (-not $env:SAHAR_UPDATE_KEY) { throw 'SAHAR_UPDATE_KEY is not set: nothing signed' }
$name = Split-Path $Setup -Leaf
if ($name -ne "Sahar-Setup-$Version.exe") { throw "the setup is '$name', expected Sahar-Setup-$Version.exe" }
$sha = (Get-FileHash $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
$json = '{"version":"' + $Version + '","setup":"' + $name + '","sha256":"' + $sha + '","channel":"' + $Channel + '"}'
$bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
$key = [System.Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem($env:SAHAR_UPDATE_KEY)
    if ($key.KeySize -ne 256) { throw "the key is $($key.KeySize)-bit, not P-256" }
    $sig = $key.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
}
finally { $key.Dispose() }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
[System.IO.File]::WriteAllBytes((Join-Path $Out 'Sahar-update.json'), $bytes)
[System.IO.File]::WriteAllText((Join-Path $Out 'Sahar-update.json.sig'), [Convert]::ToBase64String($sig) + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "signed Sahar-update.json: $json"
