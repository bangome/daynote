#Requires -Version 5.1
<#
.SYNOPSIS
    Creates a DISPOSABLE self-signed development certificate for signing the Daynote
    development MSIX, and exports it (PFX + public .cer) OUTSIDE the repository.

.DESCRIPTION
    Plan Todo 11. This certificate is for LOCAL development / disposable-VM signing
    ONLY. It is never a production signing identity.

    MACHINE-MUTATING - DEFERRED. New-SelfSignedCertificate writes into
    Cert:\CurrentUser\My, and trusting the exported .cer writes into a trust store.
    Per the 2026-07-20 user decision this script is AUTHORED but must NOT be run on
    the developer workstation. Run it inside a disposable Windows VM when you are
    ready to install the package. It supports -WhatIf so you can dry-run first.

    The subject MUST match Package.appxmanifest Identity/@Publisher ("CN=Daynote.Dev")
    or Add-AppxPackage will reject the signed package.

    NOTHING is written into the repository or the evidence payload directories: the
    PFX, its password, and the .cer all land under -OutputDirectory, which defaults
    to %LOCALAPPDATA%\DaynoteDevCert and is validated to be outside this repo.

.PARAMETER PublisherSubject
    X.500 subject; defaults to the manifest publisher "CN=Daynote.Dev".

.PARAMETER OutputDirectory
    Directory (outside the repo) to receive the PFX and .cer. Created if missing.

.PARAMETER Password
    SecureString password for the exported PFX. If omitted a strong random password
    is generated and written next to the PFX as <name>.password.txt (also outside
    the repo) so the disposable VM operator can retrieve it.

.PARAMETER FriendlyName
    Certificate friendly name. Default "Daynote Development Signing".

.PARAMETER ValidDays
    Validity window in days. Default 90 (disposable).

.EXAMPLE
    # Dry run (no store mutation, no files written):
    ./scripts/New-DevelopmentCertificate.ps1 -WhatIf

.EXAMPLE
    # In a disposable VM, real run:
    ./scripts/New-DevelopmentCertificate.ps1 -OutputDirectory 'D:\daynote-cert'
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $PublisherSubject = 'CN=Daynote.Dev',
    [string] $OutputDirectory = (Join-Path $env:LOCALAPPDATA 'DaynoteDevCert'),
    [System.Security.SecureString] $Password,
    [string] $FriendlyName = 'Daynote Development Signing',
    [int] $ValidDays = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Refuse to write anything inside the repository or its evidence payload tree.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedParent = [System.IO.Path]::GetFullPath($OutputDirectory)
if ($resolvedParent.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory '$OutputDirectory' is inside the repository ('$repoRoot'). " +
          'Choose a path outside the repo so no certificate or password is ever committed.'
}

Write-Host "Repository root : $repoRoot"
Write-Host "Certificate out : $resolvedParent  (outside repo: OK)"
Write-Host "Publisher subject: $PublisherSubject"
Write-Warning 'This is machine-mutating (writes to Cert:\CurrentUser\My). Intended for a disposable VM only.'

if (-not (Test-Path $resolvedParent)) {
    if ($PSCmdlet.ShouldProcess($resolvedParent, 'Create output directory')) {
        New-Item -ItemType Directory -Path $resolvedParent -Force | Out-Null
    }
}

if (-not $Password) {
    # Generate a strong disposable password; persisted outside the repo only.
    Add-Type -AssemblyName System.Web -ErrorAction SilentlyContinue
    $plain = [System.Web.Security.Membership]::GeneratePassword(24, 6)
    $Password = ConvertTo-SecureString -String $plain -AsPlainText -Force
    $passwordPath = Join-Path $resolvedParent 'Daynote.Dev.password.txt'
    if ($PSCmdlet.ShouldProcess($passwordPath, 'Write generated PFX password (outside repo)')) {
        Set-Content -Path $passwordPath -Value $plain -Encoding UTF8
        Write-Host "Generated PFX password written to $passwordPath"
    }
}

$pfxPath = Join-Path $resolvedParent 'Daynote.Dev.pfx'
$cerPath = Join-Path $resolvedParent 'Daynote.Dev.cer'

if ($PSCmdlet.ShouldProcess("Cert:\CurrentUser\My ($PublisherSubject)", 'Create self-signed code-signing certificate')) {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $PublisherSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName $FriendlyName `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddDays($ValidDays) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

    Write-Host "Created certificate thumbprint: $($cert.Thumbprint)"

    if ($PSCmdlet.ShouldProcess($pfxPath, 'Export PFX (outside repo)')) {
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $Password | Out-Null
        Write-Host "Exported signing PFX to $pfxPath"
    }
    if ($PSCmdlet.ShouldProcess($cerPath, 'Export public .cer (outside repo)')) {
        Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
        Write-Host "Exported public certificate to $cerPath"
    }

    Write-Host ''
    Write-Host 'NEXT (in the disposable VM only):'
    Write-Host "  1. Trust the public cert (admin): Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host "  2. Build + sign: scripts\Build-Package.ps1 -Sign -CertificatePath '$pfxPath'"
    Write-Host '  Remove the cert after QA: Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq ''' + $PublisherSubject + ''' | Remove-Item'
}
