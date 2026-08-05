#Requires -Version 5.1
<#
.SYNOPSIS
    Restores locked, builds Release (warnings-as-errors), publishes Daynote.App
    self-contained x64, and produces the UNSIGNED x64 development MSIX artifact.

.DESCRIPTION
    Plan Todo 11. Default behavior is safe for authoring and CI: it never signs and
    never installs. It:
      1. dotnet restore Daynote.sln --locked-mode
      2. dotnet build Daynote.sln -c Release -warnaserror
      3. dotnet publish src/Daynote.App self-contained win-x64
      4. MSBuild the packaging/.wapproj to produce the .msix under -OutputDirectory

    Step 4 needs the DesktopBridge MSBuild targets from the Visual Studio "Windows
    application packaging" component (or the standalone MSIX Packaging Tools) and a
    full MSBuild.exe (located via vswhere). The plain dotnet SDK cannot build the
    .wapproj. If MSBuild/DesktopBridge is unavailable this script completes the
    self-contained publish, prints exactly why packaging was skipped, and exits 0 so
    CI still produces the framework artifact.

    SIGNING and INSTALL are opt-in and MACHINE-MUTATING:
      -Sign     : signs the produced MSIX with -CertificatePath (touches no store,
                  but requires a PFX produced by New-DevelopmentCertificate.ps1).
      -Install  : runs Add-AppxPackage. DEFERRED per the 2026-07-20 user decision;
                  intended for a disposable VM. Refuses to run unless you also pass
                  -IAcceptMachineMutation, and is documented in docs/PACKAGING.md.

.PARAMETER Configuration
    Build configuration. Default Release.

.PARAMETER Architecture
    Target architecture. Only x64 is supported (Todo 11 Must NOT ship x86/Arm64).

.PARAMETER EvidenceDir
    Directory to receive the build transcript. Default the task-11 evidence path.

.PARAMETER OutputDirectory
    Directory to receive the .msix artifact. Default <repo>\artifacts.

.PARAMETER Sign
    Sign the produced package with -CertificatePath / -CertificatePassword.

.PARAMETER Install
    Install the produced package via Add-AppxPackage (DEFERRED; VM only).

.EXAMPLE
    # Authoring / CI (no signing, no install):
    ./scripts/Build-Package.ps1 -Configuration Release -Architecture x64 `
        -EvidenceDir .omo\evidence\daynote-desktop-app\task-11
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64')]
    [string] $Architecture = 'x64',

    [string] $EvidenceDir,

    [string] $OutputDirectory,

    [switch] $Sign,
    [string] $CertificatePath,
    [System.Security.SecureString] $CertificatePassword,

    [switch] $Install,
    [switch] $IAcceptMachineMutation,

    # Produce an UNSIGNED .msixupload bundle for Microsoft Store submission (Store re-signs).
    # Ignores -Sign/-Install. See docs/STORE.md.
    [switch] $Store
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repoRoot 'Daynote.sln'
$appProject = Join-Path $repoRoot 'src\Daynote.App\Daynote.App.csproj'
$wapProject = Join-Path $repoRoot 'packaging\Daynote.Package\Daynote.Package.wapproj'
$rid = "win-$Architecture"

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts' }
if (-not $EvidenceDir) { $EvidenceDir = Join-Path $repoRoot '.omo\evidence\daynote-desktop-app\task-11' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null
$transcript = Join-Path $EvidenceDir 'build-transcript.txt'

function Write-Log([string] $message) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'), $message
    Write-Host $line
    Add-Content -Path $transcript -Value $line -Encoding UTF8
}

Set-Content -Path $transcript -Value "Daynote Build-Package transcript" -Encoding UTF8
Write-Log "Configuration=$Configuration Architecture=$Architecture Sign=$Sign Install=$Install"
Write-Log "Repo=$repoRoot Output=$OutputDirectory"

# 1. Locked restore.
Write-Log 'Step 1: dotnet restore --locked-mode'
& dotnet restore $solution --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }

# 2. Release build, warnings as errors.
Write-Log 'Step 2: dotnet build -c Release -warnaserror'
& dotnet build $solution -c $Configuration --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

# 3. Self-contained x64 publish of the app.
$publishDir = Join-Path $OutputDirectory "app-$rid"
Write-Log "Step 3: dotnet publish self-contained $rid -> $publishDir"
& dotnet publish $appProject -c $Configuration -r $rid --self-contained true `
    -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }

# 4. Package the .wapproj into an MSIX (needs full MSBuild + DesktopBridge targets).
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    Write-Log 'Step 4 SKIPPED: MSBuild with DesktopBridge targets not found (VS "Windows application packaging" component absent).'
    Write-Log 'The self-contained publish above is complete. Build the MSIX in an environment that has the VS packaging component or MSIX Packaging Tools.'
    Write-Log 'DONE (publish-only).'
    return
}

Write-Log "Step 4: MSBuild package via $msbuild"

if ($Store) {
    # Store submission: UNSIGNED .msixupload bundle (the Store re-signs with the app's Store identity).
    Write-Log 'STORE MODE: building unsigned .msixupload (StoreUpload). -Sign/-Install ignored.'
    & $msbuild $wapProject `
        /p:Configuration=$Configuration `
        /p:Platform=$Architecture `
        /p:AppxBundle=Always `
        /p:AppxBundlePlatforms=x64 `
        /p:UapAppxPackageBuildMode=StoreUpload `
        /p:AppxPackageSigningEnabled=False `
        /p:AppxPackageDir=$OutputDirectory\
    if ($LASTEXITCODE -ne 0) { throw 'Store packaging failed.' }

    $upload = Get-ChildItem -Path $OutputDirectory -Recurse -Filter '*.msixupload' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($upload) {
        Write-Log "Produced Store package: $($upload.FullName)"
        Write-Log 'Upload this .msixupload in Partner Center. See docs/STORE.md.'
    }
    else {
        Write-Log 'No .msixupload found after packaging.'
    }

    Write-Log 'DONE (store).'
    return
}

$signArgs = @('/p:AppxPackageSigningEnabled=False')
if ($Sign) {
    if (-not $CertificatePath) { throw '-Sign requires -CertificatePath (produced by New-DevelopmentCertificate.ps1).' }
    $signArgs = @('/p:AppxPackageSigningEnabled=True', "/p:PackageCertificateKeyFile=$CertificatePath")
    if ($CertificatePassword) {
        $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword))
        $signArgs += "/p:PackageCertificatePassword=$plain"
    }
    Write-Log 'Signing ENABLED with supplied certificate.'
}

& $msbuild $wapProject `
    /p:Configuration=$Configuration `
    /p:Platform=$Architecture `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxPackageDir=$OutputDirectory\ `
    @signArgs
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }

$msix = Get-ChildItem -Path $OutputDirectory -Recurse -Filter '*.msix' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($msix) { Write-Log "Produced MSIX: $($msix.FullName)" } else { Write-Log 'No .msix found after packaging.' }

# 5. Install is DEFERRED and machine-mutating; refuse unless explicitly accepted.
if ($Install) {
    if (-not $IAcceptMachineMutation) {
        throw '-Install is machine-mutating (Add-AppxPackage) and DEFERRED. Re-run in a disposable VM with -IAcceptMachineMutation. See docs/PACKAGING.md.'
    }
    if (-not $msix) { throw 'No MSIX to install.' }
    Write-Log "Step 5: Add-AppxPackage $($msix.FullName)"
    Add-AppxPackage -Path $msix.FullName
    Write-Log 'Installed.'
}

Write-Log 'DONE.'
