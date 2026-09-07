<#
.SYNOPSIS
  Builds the unpackaged Windows app from src/Daynote.Desktop.

.DESCRIPTION
  The Windows counterpart of Build-MacApp.sh: a self-contained folder with the .NET runtime inside,
  so there is nothing for the user to install first, plus a zip of it.

  NOT the shipping channel. docs/WINDOWS_ON_AVALONIA.md §3 settled on the Microsoft Store for
  Windows, because the Store re-signs at ingestion and there is no code-signing certificate to buy.
  This script exists so that decision stays reversible: it is kept working and unshipped, and it is
  the mechanism macOS uses. Turning the unpackaged channel on means buying a certificate and filling
  in Program.UpdateFeedUrl - not writing code.

  Self-contained and NOT single-file, for the same reason as the Mac bundle: Daynote.Mcp ships beside
  the app and a client has to be able to launch it by path. Single-file would bury it in a temp
  extraction directory that changes every run.

  Signing is optional and driven by the environment, so no certificate or password ever appears in
  this file or in a build log:

    DAYNOTE_SIGN_THUMBPRINT   a code-signing certificate already in the current user's store
    DAYNOTE_SIGN_PFX          path to a .pfx, with DAYNOTE_SIGN_PFX_PASSWORD

  With neither set the output is unsigned, which runs here and shows SmartScreen elsewhere — the
  same trade-off as an ad-hoc signed Mac bundle. That is acceptable precisely because this is not the
  shipping channel; a real unpackaged release would need the certificate first.

.EXAMPLE
  scripts/Build-WindowsApp.ps1
  scripts/Build-WindowsApp.ps1 -Rid win-arm64 -Out dist/win-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$Out = 'dist/win',

    [string]$Version = '1.5.0',

    # Produces Setup.exe and the release feed with Velopack. Off by default: it needs the vpk tool,
    # and a plain folder plus zip is what most builds want.
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Daynote.Desktop/Daynote.Desktop.csproj'
$publish = Join-Path $root "artifacts/win-publish/$Rid"
$stage = Join-Path $root $Out
$appName = 'Daynote'

Write-Host "==> publish ($Rid, $Configuration)"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c $Configuration -r $Rid --self-contained `
    -p:PublishSingleFile=false -p:DebugType=none -p:Version=$Version `
    -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }

Write-Host '==> stage'
$appDir = Join-Path $stage $appName
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Copy-Item (Join-Path $publish '*') $appDir -Recurse -Force

# Both executables are signed: the app the user launches, and the MCP server a client launches on
# its own. An unsigned helper beside a signed app is the kind of gap that gets flagged later.
$targets = @('Daynote.Desktop.exe', 'Daynote.Mcp.exe') |
    ForEach-Object { Join-Path $appDir $_ } |
    Where-Object { Test-Path $_ }

Write-Host '==> sign'
$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

$thumbprint = $env:DAYNOTE_SIGN_THUMBPRINT
$pfx = $env:DAYNOTE_SIGN_PFX

if (-not $signtool) {
    Write-Warning 'signtool.exe not found (Windows SDK). Output is UNSIGNED.'
}
elseif (-not $thumbprint -and -not $pfx) {
    Write-Warning 'No DAYNOTE_SIGN_THUMBPRINT or DAYNOTE_SIGN_PFX. Output is UNSIGNED - SmartScreen will warn.'
}
else {
    $common = @('/fd', 'sha256', '/tr', 'http://timestamp.digicert.com', '/td', 'sha256')
    $identity = if ($thumbprint) { @('/sha1', $thumbprint) } else { @('/f', $pfx, '/p', $env:DAYNOTE_SIGN_PFX_PASSWORD) }
    foreach ($target in $targets) {
        & $signtool.FullName sign @identity @common $target
        if ($LASTEXITCODE -ne 0) { throw "signing failed for $target ($LASTEXITCODE)" }
    }
    foreach ($target in $targets) {
        & $signtool.FullName verify /pa $target
        if ($LASTEXITCODE -ne 0) { throw "signature verification failed for $target" }
    }
    Write-Host "    signed $($targets.Count) executable(s)"
}

Write-Host '==> zip'
$zip = Join-Path $stage "$appName-$Version-$Rid.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $appDir -DestinationPath $zip

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)

if ($Installer) {
    Write-Host '==> installer (Velopack)'

    # The app id is NOT "Daynote". Velopack installs into %LocalAppData%\<id>, and %LocalAppData%\Daynote
    # is where the database lives — the installer would be unpacking itself on top of the user's notes.
    $appId = 'Daynote.Desktop'
    $releases = Join-Path $root 'artifacts/win-releases'

    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw 'vpk not found. Install it once: dotnet tool install -g vpk'
    }

    $vpkArgs = @(
        'pack',
        '--packId', $appId,
        '--packVersion', $Version,
        '--packDir', $appDir,
        '--mainExe', 'Daynote.Desktop.exe',
        '--packTitle', 'Daynote',
        '--outputDir', $releases
    )
    if ($thumbprint) { $vpkArgs += @('--signParams', "/sha1 $thumbprint /fd sha256 /tr http://timestamp.digicert.com /td sha256") }

    & vpk @vpkArgs
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }
    Write-Host "    releases: $releases"
}

Write-Host "==> done"
Write-Host "    folder : $appDir"
Write-Host "    zip    : $zip ($size MB)"
