<#
.SYNOPSIS
    Prints payload-redacted row counts and integrity observables for a Daynote SQLite database.

.DESCRIPTION
    READ-ONLY and non-mutating. This is a thin wrapper over the Daynote.UiQa harness `--inspect`
    command, which opens the database read-only and selects only counts and non-payload metadata
    (never note titles/bodies, clipboard text, or image bytes). It installs nothing, launches no app,
    and deletes nothing. Safe to run on the authoring machine.

.PARAMETER DatabasePath
    Path to daynote.db (typically %LocalAppData%\Daynote\daynote.db).

.PARAMETER EvidenceDir
    Directory to write the payload-free snapshot JSON. Required.

.PARAMETER HarnessDll
    Optional explicit path to Daynote.UiQa.dll; auto-discovered under qa/Daynote.UiQa/bin otherwise.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DatabasePath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir,

    [string]$HarnessDll
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

# --inspect is read-only; it exits 0 when the DB is absent or healthy, non-zero on FK/FTS drift.
$snapshot = & dotnet $HarnessDll --inspect $DatabasePath
$exitCode = $LASTEXITCODE

$snapshotPath = Join-Path $EvidenceDir 'database-inspection.json'
$snapshot | Set-Content -Path $snapshotPath -Encoding utf8
$snapshot

exit $exitCode
