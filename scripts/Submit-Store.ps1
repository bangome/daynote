<#
.SYNOPSIS
    Builds the Store package and submits it to Partner Center with the Microsoft Store Developer CLI.

.DESCRIPTION
    The repetitive half of a release: bump the version, build the .msixupload, upload it, commit the
    submission, and wait. What it deliberately does NOT do is write the Store listing — see NOTES.

    Everything here is a thin, checked wrapper around `msstore`. The value is not the commands, which
    are three lines; it is the preflight checks and the ordering, both of which have cost releases:

      * `msstore publish` DELETES the pending draft and recreates it from the last published
        submission. Any listing edit staged in Partner Center and not yet submitted is discarded.
        That is why this script refuses to run when a draft is pending unless told otherwise.
      * The Store rejects a package whose version matches one already published, and the packaging
        path has been seen producing a file *named* for the new version containing the old one
        (see Build-Package.ps1). The version is therefore read back out of what was built.

.PARAMETER ProductId
    The Store product ID from Partner Center (`msstore apps list`). Required.

.PARAMETER BumpVersion
    Increment the manifest's build number before building (1.5.0.0 -> 1.6.0.0). Without this the
    manifest is used as it stands, which is what you want when the version was already committed.

.PARAMETER NoCommit
    Upload the package but leave the submission in draft, so the listing can be reviewed in Partner
    Center before submitting. Use this for the first few releases.

.PARAMETER WhatIf
    Print what would happen and change nothing.

.NOTES
    Prerequisites, none of which this script can do for you:

      1. `winget install "Microsoft Store Developer CLI"` (needs the .NET 9 Desktop Runtime).
      2. A Microsoft Entra ID tenant ASSOCIATED WITH the Partner Center account. A personal
         Microsoft account is not enough; Partner Center can create a tenant for you. This is the
         step that usually blocks a solo publisher.
      3. `msstore reconfigure --tenantId ... --sellerId ... --clientId ... --clientSecret ...`
         (in CI, from secrets).
      4. The age-rating questionnaire and the product declarations completed once in the dashboard.

    The Store listing — description, features, screenshots — is not written here. It CAN be
    automated (`msstore submission get` -> edit the JSON -> `msstore submission update`), but the
    copy lives in docs/store-listing.md and changes far less often than the package does, and a
    script that rewrites it on every release is a script that can silently publish the wrong words.
    Set the listing by hand, then let this move the bits.

    App updates through the CLI are documented as supported for FREE products only. Daynote is free
    (the subscription is sold outside the Store by Paddle), so this applies; if that ever changes,
    check the CLI's current limitations before relying on this.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $ProductId,

    [switch] $BumpVersion,

    [switch] $NoCommit,

    [switch] $AllowDiscardingDraft
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'packaging\Daynote.Package\Package.appxmanifest'

function Write-Step([string] $message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Invoke-MSStore {
    param([Parameter(Mandatory)][string[]] $Arguments)

    Write-Verbose "msstore $($Arguments -join ' ')"
    $output = & msstore @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Write-Host
        throw "msstore $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }

    return $output
}

# ---- preflight -------------------------------------------------------------------------------

Write-Step 'Checking the Microsoft Store Developer CLI'
if (-not (Get-Command msstore -ErrorAction SilentlyContinue)) {
    throw 'msstore is not on PATH. Install it with: winget install "Microsoft Store Developer CLI"'
}

# `info` prints the configured tenant/seller/client. It fails when the CLI has never been
# configured, which is a far clearer error here than a 401 in the middle of an upload.
Invoke-MSStore -Arguments @('info') | Out-Null

Write-Step "Checking for a pending draft on $ProductId"
$status = (Invoke-MSStore -Arguments @('submission', 'status', $ProductId)) -join "`n"
Write-Host $status
if ($status -notmatch 'None|Published|Failed' -and -not $AllowDiscardingDraft) {
    throw @'
A submission is already pending. `msstore publish` would delete that draft and recreate it from the
last published submission, discarding any listing edits staged in Partner Center.

Submit or delete the draft first, or pass -AllowDiscardingDraft if the draft holds nothing you want.
'@
}

# ---- version ---------------------------------------------------------------------------------

[xml] $manifest = Get-Content -Raw -Path $manifestPath
$version = [Version] $manifest.Package.Identity.Version

if ($BumpVersion) {
    # The Store requires the revision to stay 0, so the build number is the one that moves.
    $next = [Version]::new($version.Major, $version.Minor, $version.Build + 1, 0)
    Write-Step "Version $version -> $next"
    if ($PSCmdlet.ShouldProcess($manifestPath, "set version to $next")) {
        $manifest.Package.Identity.Version = $next.ToString()
        $manifest.Save($manifestPath)
    }

    $version = $next
}
else {
    Write-Step "Version $version (pass -BumpVersion to increment)"
}

if ($version.Revision -ne 0) {
    throw "The Store requires a revision of 0; the manifest says $version."
}

# ---- build -----------------------------------------------------------------------------------

Write-Step 'Building the Store package'
if ($PSCmdlet.ShouldProcess('Build-Package.ps1 -Store', 'build')) {
    & (Join-Path $PSScriptRoot 'Build-Package.ps1') -Store
    if ($LASTEXITCODE -ne 0) {
        throw "Build-Package.ps1 failed with exit code $LASTEXITCODE."
    }
}

$package = Get-ChildItem -Path (Join-Path $repoRoot 'artifacts') -Filter "*_$($version)_*.msixupload" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package -and -not $WhatIfPreference) {
    throw "No .msixupload for version $version under artifacts\. The build writes one file per version; check that the version was bumped before building."
}

if ($package) {
    Write-Step "Uploading $($package.Name) ($([math]::Round($package.Length / 1MB, 1)) MB)"
}

# ---- submit ----------------------------------------------------------------------------------

if ($PSCmdlet.ShouldProcess($ProductId, "publish $($package.Name)")) {
    $publishArgs = @('publish', $repoRoot, '--inputFile', $package.FullName, '--appId', $ProductId)
    if ($NoCommit) {
        $publishArgs += '--noCommit'
    }

    Invoke-MSStore -Arguments $publishArgs | Write-Host

    if ($NoCommit) {
        Write-Step 'Left in draft. Review the listing in Partner Center, then submit there or run:'
        Write-Host "    msstore submission publish $ProductId"
        return
    }

    Write-Step 'Waiting for certification'
    Invoke-MSStore -Arguments @('submission', 'poll', $ProductId) | Write-Host
}

Write-Step 'Done'
