# Builds the Microsoft Store (MSIX) package from publish/DiskCheckTW.
# Run build/publish.sh --publish-only first (or the full publish).
param(
  [Parameter(Mandatory = $true)][string]$Version,          # four parts, last must be 0, e.g. 0.1.4.0
  [Parameter(Mandatory = $true)][string]$IdentityName,     # Partner Center: Package/Identity/Name
  [Parameter(Mandatory = $true)][string]$Publisher,        # Partner Center: Package/Identity/Publisher (CN=...)
  [Parameter(Mandatory = $true)][string]$PublisherDisplayName,
  [Parameter(Mandatory = $true)][string]$OutFile
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$payload = Join-Path $root 'publish/DiskCheckTW'
if (-not (Test-Path (Join-Path $payload 'DiskCheckTW.App.exe'))) { throw "Missing $payload; run build/publish.sh first" }
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version must have four parts ending in .0: $Version" }

$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\makeappx.exe' |
  Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
if (-not $sdk) { throw 'makeappx.exe not found (Windows SDK)' }
$bin = $sdk.Directory.FullName
Write-Host "Windows SDK tools: $bin"

$stage = Join-Path $root 'publish/msix-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Copy-Item $payload $stage -Recurse
Copy-Item (Join-Path $PSScriptRoot 'msix/Assets') (Join-Path $stage 'Assets') -Recurse

$manifest = Get-Content (Join-Path $PSScriptRoot 'msix/AppxManifest.xml') -Raw -Encoding UTF8
$values = @{ '@VERSION@' = $Version; '@IDENTITY_NAME@' = $IdentityName; '@PUBLISHER@' = $Publisher; '@PUBLISHER_DISPLAY_NAME@' = $PublisherDisplayName }
foreach ($key in $values.Keys) { $manifest = $manifest.Replace($key, [System.Security.SecurityElement]::Escape($values[$key])) }
if ($manifest -match '@[A-Z_]+@') { throw "Unreplaced placeholder: $($Matches[0])" }
[System.IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

$priConfig = Join-Path $root 'publish/priconfig.xml'
& "$bin\makepri.exe" createconfig /cf $priConfig /dq zh-TW_en-US /pv 10.0.0 /o
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed' }
& "$bin\makepri.exe" new /pr $stage /cf $priConfig /mn (Join-Path $stage 'AppxManifest.xml') /of (Join-Path $stage 'resources.pri') /o
if ($LASTEXITCODE -ne 0) { throw 'makepri new failed' }

New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
& "$bin\makeappx.exe" pack /d $stage /p $OutFile /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed' }
Write-Host "Created $OutFile"
