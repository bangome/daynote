using System.Collections.Concurrent;
using Daynote.App.Input;
using Daynote.App.Lifecycle;
using Daynote.App.Settings;
using Daynote.Core.Backup;
using Daynote.Core.Settings;
using Daynote.Core.Startup;
using Daynote.Infrastructure.Instance;

namespace Daynote.App.Tests.Lifecycle;

/// <summary>Records backup/restore calls; a test can force the restore staging outcome.</summary>
internal sealed class FakeBackupService : IBackupService
{
    public List<string> BackupCalls { get; } = [];

    public List<string> RestoreCalls { get; } = [];

    public RestoreStageResult NextRestoreResult { get; set; } = RestoreStageResult.Staged();

    public Task CreateBackupAsync(string destinationZipPath, CancellationToken cancellationToken = default)
    {
        BackupCalls.Add(destinationZipPath);
        return Task.CompletedTask;
    }

    public Task<RestoreStageResult> StageRestoreAsync(string sourceZipPath, CancellationToken cancellationToken = default)
    {
        RestoreCalls.Add(sourceZipPath);
        return Task.FromResult(NextRestoreResult);
    }
}

/// <summary>A backup picker that returns preset paths (null = user cancelled).</summary>
internal sealed class FakeBackupFilePicker : IBackupFilePicker
{
    public string? SavePath { get; set; }

    public string? OpenPath { get; set; }

    public string? PickSaveZip(string defaultFileName) => SavePath;

    public string? PickOpenZip() => OpenPath;
}

/// <summary>A recording global-hotkey double: captures TrySet calls and lets a test force a conflict.</summary>
internal sealed class RecordingHotkeyService : IGlobalHotkeyService
{
    public List<Hotkey> SetCalls { get; } = [];

    public int AttachCount { get; private set; }

    /// <summary>Result returned by the next (and subsequent) valid <see cref="TrySet"/> calls.</summary>
    public HotkeySetResult NextResult { get; set; } = HotkeySetResult.Ok;

    public Hotkey? Current { get; private set; }

    public event EventHandler? Pressed;

    public event EventHandler? QuickNotePressed;

    public void Attach(nint hwnd) => AttachCount++;

    public HotkeySetResult TrySet(Hotkey hotkey)
    {
        SetCalls.Add(hotkey);
        if (!hotkey.IsValid)
        {
            return HotkeySetResult.Invalid;
        }

        if (NextResult == HotkeySetResult.Ok)
        {
            Current = hotkey;
        }

        return NextResult;
    }

    public void RaisePressed() => Pressed?.Invoke(this, EventArgs.Empty);

    public void RaiseQuickNotePressed() => QuickNotePressed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}

internal sealed class RecordingTrayPresenter : ITrayPresenter
{
    public bool? LastWindowShown { get; private set; }

    public bool IsDisposed { get; private set; }

    public void UpdateWindowShown(bool shown) => LastWindowShown = shown;

    public event EventHandler? ShowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    public void RaiseShow() => ShowRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose() => IsDisposed = true;
}

internal sealed class RecordingWindowHost : IWindowHost
{
    public int HideCount { get; private set; }

    public int ShowCount { get; private set; }

    public int ShowSettingsCount { get; private set; }

    public void HideToTray() => HideCount++;

    public void ShowAndActivate() => ShowCount++;

    public void ShowSettings() => ShowSettingsCount++;
}

internal sealed class RecordingApplicationExit : IApplicationExit
{
    public int ShutdownCount { get; private set; }

    public void Shutdown() => ShutdownCount++;
}

/// <summary>An in-memory settings store; the dictionary can be shared to model a restart.</summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly ConcurrentDictionary<string, string> _values;

    public InMemorySettingsStore(ConcurrentDictionary<string, string>? backing = null) =>
        _values = backing ?? new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    public ConcurrentDictionary<string, string> Backing => _values;

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_values.TryGetValue(key, out string? value) ? value : null);

    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _values[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_values.TryGetValue(key, out string? value) ? value == "1" : fallback);

    public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default)
    {
        _values[key] = value ? "1" : "0";
        return ValueTask.CompletedTask;
    }
}

/// <summary>A configurable startup service for settings view-model tests.</summary>
internal sealed class FakeStartupTaskService : IStartupTaskService
{
    public FakeStartupTaskService(StartupTaskState state) => State = state;

    public StartupTaskState State { get; set; }

    public int EnableCalls { get; private set; }

    public int DisableCalls { get; private set; }

    public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(State);

    public ValueTask<StartupEnableResult> RequestEnableAsync(CancellationToken cancellationToken = default)
    {
        EnableCalls++;
        if (State == StartupTaskState.Disabled)
        {
            State = StartupTaskState.Enabled;
            return ValueTask.FromResult(new StartupEnableResult(State, Changed: true));
        }

        return ValueTask.FromResult(new StartupEnableResult(State, Changed: false));
    }

    public ValueTask<StartupEnableResult> RequestDisableAsync(CancellationToken cancellationToken = default)
    {
        DisableCalls++;
        if (State == StartupTaskState.Enabled)
        {
            State = StartupTaskState.Disabled;
            return ValueTask.FromResult(new StartupEnableResult(State, Changed: true));
        }

        return ValueTask.FromResult(new StartupEnableResult(State, Changed: false));
    }
}

/// <summary>An in-process activation channel double that records disposal for the quit path.</summary>
internal sealed class RecordingActivationChannel : IActivationChannel
{
    public bool IsDisposed { get; private set; }

    public void StartListening(Action onActivation)
    {
    }

    public Task<bool> SignalAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class AlwaysPrimaryClaim : IPrimaryClaim
{
    public bool Disposed { get; private set; }

    public bool TryClaim() => true;

    public void Dispose() => Disposed = true;
}
