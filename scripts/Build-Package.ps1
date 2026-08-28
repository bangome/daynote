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
$manifest = Join-Path $repoRoot 'packaging\Daynote.Package\Package.appxmanifest'
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

function Assert-McpServerCoLocated {
    <#
    .SYNOPSIS
        Verifies the packaged MCP server can actually start.
    .DESCRIPTION
        The server shares Daynote.App's folder so the package carries one copy of the .NET runtime
        instead of two (see the _DaynoteCoLocateMcpServer target). That merge is only safe while every
        assembly Daynote.Mcp.deps.json names is present in that folder, so this re-derives the list
        from the produced package and fails the build if anything is missing. A missing assembly would
        otherwise surface as a stdio server that dies on first use, long after release.
    #>
    param([Parameter(Mandatory)][string] $PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("daynote-mcp-verify-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    try {
        # A Store build nests .msix inside .msixbundle inside .msixupload; unwrap to the .msix.
        $current = $PackagePath
        foreach ($inner in @('*.msixbundle', '*.msix')) {
            if ([System.IO.Path]::GetExtension($current) -eq '.msix') { break }
            $stage = Join-Path $scratch ([System.Guid]::NewGuid().ToString('N'))
            [System.IO.Compression.ZipFile]::ExtractToDirectory($current, $stage)
            $next = Get-ChildItem -Path $stage -Filter $inner -File | Select-Object -First 1
            if (-not $next) { throw "Could not find $inner inside $current." }
            $current = $next.FullName
        }

        $package = [System.IO.Compression.ZipFile]::OpenRead($current)
        try {
            $entries = @{}
            foreach ($entry in $package.Entries) { $entries[$entry.FullName.Replace('\', '/')] = $entry }

            # @() keeps a single hit an array; strict mode would otherwise fault on .Count below.
            $strays = @($entries.Keys | Where-Object { $_ -like 'Daynote.Mcp/*' })
            if ($strays.Count -gt 0) {
                throw ("The MCP server was not co-located: {0} file(s) still under Daynote.Mcp/ (e.g. {1})." -f
                    $strays.Count, ($strays | Select-Object -First 1))
            }

            $required = @('Daynote.App/Daynote.Mcp.exe', 'Daynote.App/Daynote.Mcp.runtimeconfig.json',
                'Daynote.App/Daynote.Mcp.deps.json')
            foreach ($name in $required) {
                if (-not $entries.ContainsKey($name)) { throw "Package is missing $name." }
            }

            $reader = New-Object System.IO.StreamReader($entries['Daynote.App/Daynote.Mcp.deps.json'].Open())
            try { $deps = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }

            # Every runtime asset the server's host will look for, by file name.
            $needed = New-Object System.Collections.Generic.HashSet[string]
            foreach ($target in $deps.targets.PSObject.Properties) {
                foreach ($library in $target.Value.PSObject.Properties) {
                    # Strict mode makes a missing member fatal, so look the property up instead.
                    $runtime = $library.Value.PSObject.Properties['runtime']
                    if (-not $runtime) { continue }
                    foreach ($asset in $runtime.Value.PSObject.Properties) {
                        $leaf = [System.IO.Path]::GetFileName($asset.Name)
                        if ($leaf) { [void] $needed.Add($leaf) }
                    }
                }
            }

            $missing = @()
            foreach ($leaf in $needed) {
                if (-not $entries.ContainsKey("Daynote.App/$leaf")) { $missing += $leaf }
            }
            if ($missing.Count -gt 0) {
                throw ("The co-located MCP server would fail to start: {0} assembly/assemblies named by " +
                    "Daynote.Mcp.deps.json are absent from Daynote.App/ -> {1}") -f $missing.Count, ($missing -join ', ')
            }

            Write-Log ("MCP server verified: co-located in Daynote.App/ with all {0} referenced assemblies present." -f $needed.Count)
        }
        finally { $package.Dispose() }
    }
    finally {
        try { Remove-Item -Recurse -Force $scratch -ErrorAction Stop } catch {}
    }
}

function Assert-PackageVersionMatchesManifest {
    <#
    .SYNOPSIS
        Verifies the produced package carries the version in Package.appxmanifest.
    .DESCRIPTION
        The StoreUpload path keeps its own bin\...\Upload tree, and an incremental build has been
        observed leaving that tree's generated AppxManifest.xml at the previous version while the
        bundle around it advanced. The result is a .msixupload whose file name says the new version
        and whose application package says the old one. Partner Center then rejects it as a duplicate
        of the release you already shipped -- or, worse, you believe you submitted a new build and
        did not.

        Deleting packaging\Daynote.Package\bin and \obj fixes it. This check exists so the mismatch
        surfaces at build time instead of at upload time.
    #>
    param(
        [Parameter(Mandatory)][string] $PackagePath,
        [Parameter(Mandatory)][string] $ManifestPath)

    $expected = ([xml] (Get-Content -Path $ManifestPath -Raw)).Package.Identity.Version

    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("daynote-version-verify-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    try {
        # Same nesting as the MCP check: .msixupload -> .msixbundle -> .msix.
        $current = $PackagePath
        foreach ($inner in @('*.msixbundle', '*.msix')) {
            if ([System.IO.Path]::GetExtension($current) -eq '.msix') { break }
            $stage = Join-Path $scratch ([System.Guid]::NewGuid().ToString('N'))
            [System.IO.Compression.ZipFile]::ExtractToDirectory($current, $stage)
            $next = Get-ChildItem -Path $stage -Filter $inner -File | Select-Object -First 1
            if (-not $next) { throw "Could not find $inner inside $current." }
            $current = $next.FullName
        }

        $package = [System.IO.Compression.ZipFile]::OpenRead($current)
        try {
            $entry = $package.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
            if (-not $entry) { throw 'The produced package has no AppxManifest.xml.' }
            $reader = New-Object System.IO.StreamReader($entry.Open())
            try { $actual = ([xml] $reader.ReadToEnd()).Package.Identity.Version } finally { $reader.Dispose() }
        }
        finally { $package.Dispose() }
    }
    finally {
        try { Remove-Item -Recurse -Force $scratch -ErrorAction Stop } catch {}
    }

    if ($actual -ne $expected) {
        throw ("Version mismatch: Package.appxmanifest says {0} but the packaged application says {1}. " +
            "That is stale packaging output -- delete packaging\Daynote.Package\bin and \obj, then build again.") -f $expected, $actual
    }

    Write-Log "Package version verified: $actual matches Package.appxmanifest."
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
        Assert-PackageVersionMatchesManifest -PackagePath $upload.FullName -ManifestPath $manifest
        Assert-McpServerCoLocated -PackagePath $upload.FullName
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
if ($msix) {
    Write-Log "Produced MSIX: $($msix.FullName)"
    Assert-PackageVersionMatchesManifest -PackagePath $msix.FullName -ManifestPath $manifest
    Assert-McpServerCoLocated -PackagePath $msix.FullName
}
else { Write-Log 'No .msix found after packaging.' }

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
