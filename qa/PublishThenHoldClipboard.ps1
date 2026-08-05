[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Text,

    [ValidateRange(0, 5000)]
    [int]$HoldMs = 200
)

$ErrorActionPreference = 'Stop'

if (-not ('DaynoteQaClipboardNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class DaynoteQaClipboardNative
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

$utf8Bytes = [System.Text.Encoding]::UTF8.GetByteCount($Text)
$unicodeBytes = [System.Text.Encoding]::Unicode.GetBytes($Text + [char]0)
$publishOpened = $false
$publishError = 0
$publishDeadline = [System.Diagnostics.Stopwatch]::StartNew()
while (-not $publishOpened -and $publishDeadline.ElapsedMilliseconds -lt 5000) {
    $publishOpened = [DaynoteQaClipboardNative]::OpenClipboard([IntPtr]::Zero)
    if (-not $publishOpened) {
        $publishError = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
        Start-Sleep -Milliseconds 10
    }
}
if (-not $publishOpened) {
    throw "QA_CLIPBOARD_PUBLISH_TIMEOUT code=$publishError"
}

$memory = [DaynoteQaClipboardNative]::GlobalAlloc(0x42, [UIntPtr]$unicodeBytes.Length)
if ($memory -eq [IntPtr]::Zero) {
    [void][DaynoteQaClipboardNative]::CloseClipboard()
    throw 'QA_CLIPBOARD_ALLOC_FAILED'
}
try {
    $pointer = [DaynoteQaClipboardNative]::GlobalLock($memory)
    if ($pointer -eq [IntPtr]::Zero) {
        throw 'QA_CLIPBOARD_LOCK_FAILED'
    }
    try {
        [System.Runtime.InteropServices.Marshal]::Copy($unicodeBytes, 0, $pointer, $unicodeBytes.Length)
    }
    finally {
        [void][DaynoteQaClipboardNative]::GlobalUnlock($memory)
    }
    if (-not [DaynoteQaClipboardNative]::EmptyClipboard()) {
        throw 'QA_CLIPBOARD_EMPTY_FAILED'
    }
    if ([DaynoteQaClipboardNative]::SetClipboardData(13, $memory) -eq [IntPtr]::Zero) {
        throw 'QA_CLIPBOARD_SET_FAILED'
    }
    $memory = [IntPtr]::Zero
}
finally {
    [void][DaynoteQaClipboardNative]::CloseClipboard()
    if ($memory -ne [IntPtr]::Zero) {
        [void][DaynoteQaClipboardNative]::GlobalFree($memory)
    }
}

$opened = $false
$deadline = [System.Diagnostics.Stopwatch]::StartNew()
try {
    while (-not $opened -and $deadline.ElapsedMilliseconds -lt 1000) {
        $opened = [DaynoteQaClipboardNative]::OpenClipboard([IntPtr]::Zero)
        if (-not $opened) {
            Start-Sleep -Milliseconds 10
        }
    }
    if (-not $opened) {
        throw 'QA_CLIPBOARD_OPEN_TIMEOUT'
    }

    Start-Sleep -Milliseconds $HoldMs
    [pscustomobject]@{
        Kind = 'text'
        Utf8Bytes = $utf8Bytes
        HoldMs = $HoldMs
        Code = 'QA_CLIPBOARD_HOLD_COMPLETE'
    } | ConvertTo-Json -Compress
}
finally {
    if ($opened) {
        [void][DaynoteQaClipboardNative]::CloseClipboard()
    }
}
