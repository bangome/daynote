<#
.SYNOPSIS
    Publishes a deterministic bitmap to the SYSTEM clipboard as CF_DIB or CF_DIBV5 for Daynote
    image-capture QA (contention, DIB/DIBV5 alpha equivalence, content-addressed asset sharing).

.DESCRIPTION
    DEFERRED / MACHINE-MUTATING: this script writes the real system clipboard. Per the 2026-07-20
    user decision it is authored here but executed only in a disposable Windows VM while the packaged
    Daynote app is listening. It never installs anything, never deletes anything, and writes no user
    payload to any log.

    The bitmap is a fixed-content gradient generated from -Width/-Height so DIB and DIBV5 publishes of
    the same size normalize to the exact same canonical PNG (one shared image_asset).

.PARAMETER Format
    DIB (CF_DIB, format 8) or DIBV5 (CF_DIBV5, format 17).

.PARAMETER Width / .PARAMETER Height
    Pixel dimensions of the generated bitmap.

.PARAMETER HoldMs
    Milliseconds to hold the clipboard open AFTER publishing, to exercise the app's retry schedule.

.PARAMETER EvidenceDir
    Directory to write a payload-free receipt JSON. Required.
#>
[CmdletBinding()]
param(
    [ValidateSet('DIB', 'DIBV5')]
    [string]$Format = 'DIB',

    [ValidateRange(1, 4096)]
    [int]$Width = 32,

    [ValidateRange(1, 4096)]
    [int]$Height = 32,

    [ValidateRange(0, 5000)]
    [int]$HoldMs = 200,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDir
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null

if (-not ('DaynoteQaImageNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class DaynoteQaImageNative
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalFree(IntPtr memory);
}
'@
}

# --- Build a BITMAPINFOHEADER (DIB) or BITMAPV5HEADER (DIBV5) + BGRA32 pixel rows -----------------
$headerSize = if ($Format -eq 'DIBV5') { 124 } else { 40 }
$pixelBytes = $Width * $Height * 4
$buffer = New-Object byte[] ($headerSize + $pixelBytes)

function Set-Int32([byte[]]$b, [int]$offset, [int]$value) {
    [System.BitConverter]::GetBytes([int]$value).CopyTo($b, $offset)
}
function Set-UInt32([byte[]]$b, [int]$offset, [uint32]$value) {
    [System.BitConverter]::GetBytes([uint32]$value).CopyTo($b, $offset)
}
function Set-Int16([byte[]]$b, [int]$offset, [int16]$value) {
    [System.BitConverter]::GetBytes([int16]$value).CopyTo($b, $offset)
}

Set-UInt32 $buffer 0 ([uint32]$headerSize)   # biSize
Set-Int32  $buffer 4 $Width                  # biWidth
Set-Int32  $buffer 8 $Height                 # biHeight (bottom-up)
Set-Int16  $buffer 12 1                       # biPlanes
Set-Int16  $buffer 14 32                      # biBitCount
Set-UInt32 $buffer 16 ([uint32]0)            # biCompression = BI_RGB

# Deterministic gradient: identical content for a given -Width/-Height regardless of DIB vs DIBV5.
$pixelStart = $headerSize
for ($y = 0; $y -lt $Height; $y++) {
    for ($x = 0; $x -lt $Width; $x++) {
        $i = $pixelStart + (($y * $Width) + $x) * 4
        $buffer[$i]     = [byte](($x * 255) / [Math]::Max(1, $Width - 1))   # Blue
        $buffer[$i + 1] = [byte](($y * 255) / [Math]::Max(1, $Height - 1))  # Green
        $buffer[$i + 2] = [byte]128                                          # Red
        $buffer[$i + 3] = [byte]255                                          # Alpha (opaque)
    }
}

$cfDib = 8
$cfDibV5 = 17
$format = if ($Format -eq 'DIBV5') { $cfDibV5 } else { $cfDib }

$opened = $false
$deadline = [System.Diagnostics.Stopwatch]::StartNew()
while (-not $opened -and $deadline.ElapsedMilliseconds -lt 5000) {
    $opened = [DaynoteQaImageNative]::OpenClipboard([IntPtr]::Zero)
    if (-not $opened) { Start-Sleep -Milliseconds 10 }
}
if (-not $opened) { throw 'QA_IMAGE_CLIPBOARD_OPEN_TIMEOUT' }

$memory = [DaynoteQaImageNative]::GlobalAlloc(0x42, [UIntPtr]$buffer.Length)  # GMEM_MOVEABLE|GMEM_ZEROINIT
if ($memory -eq [IntPtr]::Zero) {
    [void][DaynoteQaImageNative]::CloseClipboard()
    throw 'QA_IMAGE_ALLOC_FAILED'
}
try {
    $pointer = [DaynoteQaImageNative]::GlobalLock($memory)
    if ($pointer -eq [IntPtr]::Zero) { throw 'QA_IMAGE_LOCK_FAILED' }
    try {
        [System.Runtime.InteropServices.Marshal]::Copy($buffer, 0, $pointer, $buffer.Length)
    }
    finally {
        [void][DaynoteQaImageNative]::GlobalUnlock($memory)
    }
    if (-not [DaynoteQaImageNative]::EmptyClipboard()) { throw 'QA_IMAGE_EMPTY_FAILED' }
    if ([DaynoteQaImageNative]::SetClipboardData($format, $memory) -eq [IntPtr]::Zero) {
        throw 'QA_IMAGE_SET_FAILED'
    }
    $memory = [IntPtr]::Zero
    Start-Sleep -Milliseconds $HoldMs
}
finally {
    [void][DaynoteQaImageNative]::CloseClipboard()
    if ($memory -ne [IntPtr]::Zero) { [void][DaynoteQaImageNative]::GlobalFree($memory) }
}

$receipt = [pscustomobject]@{
    Kind        = 'image'
    Format      = $Format
    Width       = $Width
    Height      = $Height
    ByteLength  = $buffer.Length
    HoldMs      = $HoldMs
    Code        = 'QA_IMAGE_PUBLISH_COMPLETE'
}
$receiptPath = Join-Path $EvidenceDir 'image-publish-receipt.json'
$receipt | ConvertTo-Json -Compress | Set-Content -Path $receiptPath -Encoding utf8
$receipt | ConvertTo-Json -Compress
