<#
.SYNOPSIS
    Runs the full deterministic Daynote desktop QA suite against the packaged app and returns 0 only
    when every scenario's binary observables pass.

.DESCRIPTION
    DEFERRED: per the 2026-07-20 user decision this orchestrator runs only in a disposable Windows VM
    because it drives the installed packaged app and exercises the clipboard, process-fleet, and
    package scenarios. It is authored and static-checked here; it is not executed on the authoring
    machine.

    SAFETY: cleanup is strictly namespaced. The only tree this script ever deletes is the Daynote QA
    namespace `%LocalAppData%\Daynote\.uiqa`. It never deletes `%LocalAppData%\Daynote` itself, the
    operator's notes/images/settings, or any arbitrary path, and it performs no recursive delete
    outside that namespace.

.PARAMETER PackagePath
    Path to the installed/packaged Daynote MSIX or its app executable. Required.

.PARAMETER EvidenceDir
    Root directory for per-scenario evidence. Required.

.PARAMETER HarnessDll
    Optional explicit path to Daynote.UiQa.dll; auto-discovered under qa/Daynote.UiQa/bin otherwise.

.PARAMETER Scenarios
    Optional subset of scenario names to run; defaults to every registered scenario.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [string]$HarnessDll,

    [string[]]$Scenarios
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null

if (-not $HarnessDll) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $candidates = Get-ChildItem -Path (Join-Path $repoRoot 'qa\Daynote.UiQa\bin') -Recurse -Filter 'Daynote.UiQa.dll' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending
    if ($candidates.Count -eq 0) {
        throw 'Daynote.UiQa.dll not found. Build the solution first, or pass -HarnessDll.'
    }
    $HarnessDll = $candidates[0].FullName
}

# Resolve the ONLY tree this script is permitted to delete.
$daynoteRoot = Join-Path $env:LOCALAPPDATA 'Daynote'
$qaNamespace = Join-Path $daynoteRoot '.uiqa'

function Remove-QaNamespaceOnly {
    param([string]$Path)
    # Refuse anything that is not exactly the .uiqa namespace under the Daynote root.
    $full = [System.IO.Path]::GetFullPath($Path)
    $expected = [System.IO.Path]::GetFullPath($qaNamespace)
    if ($full -ne $expected) {
        throw "REFUSING to delete '$full': only the '$expected' QA namespace may be removed."
    }
    if (-not $full.TrimEnd('\').EndsWith('.uiqa')) {
        throw "REFUSING to delete '$full': path does not end with the .uiqa namespace segment."
    }
    if (Test-Path $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

# Discover the scenario registry from the harness (never launches the app).
$registryJson = & dotnet $HarnessDll --list
$registry = $registryJson | ConvertFrom-Json
$allNames = $registry | ForEach-Object { $_.Name }
$targets = if ($Scenarios) { $Scenarios } else { $allNames }

$results = @()
foreach ($name in $targets) {
    if ($allNames -notcontains $name) {
        throw "Unknown scenario '$name'. Known: $($allNames -join ', ')"
    }

    $scenarioEvidence = Join-Path $EvidenceDir $name
    New-Item -ItemType Directory -Path $scenarioEvidence -Force | Out-Null

    & dotnet $HarnessDll --scenario $name --evidence $scenarioEvidence --package-path $PackagePath
    $code = $LASTEXITCODE
    $results += [pscustomobject]@{ Scenario = $name; ExitCode = $code; Passed = ($code -eq 0) }
}

# Namespaced cleanup, then verify no QA data remains.
Remove-QaNamespaceOnly -Path $qaNamespace
$namespaceClean = -not (Test-Path $qaNamespace)

$summary = [pscustomobject]@{
    PackagePath        = $PackagePath
    Scenarios          = $results
    AllPassed          = (($results | Where-Object { -not $_.Passed }).Count -eq 0)
    QaNamespaceCleaned = $namespaceClean
    TimestampUtc       = (Get-Date).ToUniversalTime().ToString('o')
}
$summaryPath = Join-Path $EvidenceDir 'summary.json'
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $summaryPath -Encoding utf8

$cleanupReceipt = [pscustomobject]@{
    DeletedPath = $qaNamespace
    Clean       = $namespaceClean
    Note        = 'Only the .uiqa QA namespace was removed; user notes/images/settings untouched.'
}
$cleanupReceipt | ConvertTo-Json | Set-Content -Path (Join-Path $EvidenceDir 'cleanup-receipt.json') -Encoding utf8

$summary | ConvertTo-Json -Depth 5

if (-not $summary.AllPassed -or -not $namespaceClean) {
    exit 1
}
exit 0
