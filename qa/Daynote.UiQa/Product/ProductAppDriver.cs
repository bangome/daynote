using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Daynote.UiQa.Automation;

namespace Daynote.UiQa.Product;

/// <summary>
/// Launches and controls the real Daynote product process for one QA run.
///
/// The process is pointed at a disposable data root nested under the QA namespace via the
/// <see cref="DaynoteAppOptions.DataRootEnvironmentVariable"/> seam, so it exercises the shipping
/// code path end to end (real SQLite, real assets, real lifecycle) while never reading or writing
/// the operator's own notes. Restart is supported by relaunching against the same data root.
///
/// Nothing here runs until a scenario calls <see cref="Launch"/>; construction is inert, which keeps
/// build, <c>--help</c>, and <c>--list</c> launch-free.
/// </summary>
public sealed class ProductAppDriver : IDisposable
{
    /// <summary>The product's data-root override env var (see
    /// <c>Daynote.App.Composition.DaynoteAppOptions.DataRootEnvironmentVariable</c>). Kept as a
    /// literal so the harness need not reference the WPF executable project.</summary>
    private const string DataRootEnvironmentVariable = "DAYNOTE_DATA_ROOT";

    private readonly string _executablePath;
    private readonly string _dataRoot;
    private Process? _process;

    private ProductAppDriver(string executablePath, string dataRoot)
    {
        _executablePath = executablePath;
        _dataRoot = dataRoot;
    }

    /// <summary>The disposable data root this driver's app instances use. Always inside the QA namespace.</summary>
    public string DataRoot => _dataRoot;

    /// <summary>The SQLite database the launched app reads and writes.</summary>
    public string DatabasePath => Path.Combine(_dataRoot, "daynote.db");

    public int? ProcessId => _process is { HasExited: false } ? _process.Id : null;

    /// <summary>
    /// Creates a driver bound to a fresh disposable data root. Locates the built product executable
    /// next to the harness. Does not start the process.
    /// </summary>
    public static ProductAppDriver Create(string runId)
    {
        string executable = ResolveExecutable();
        string dataRoot = DaynoteQaPaths.NewRunRoot(runId);
        return new ProductAppDriver(executable, dataRoot);
    }

    /// <summary>Starts the product process and returns a UI Automation session for its window.</summary>
    public UiaSession Launch()
    {
        if (_process is { HasExited: false })
        {
            throw new InvalidOperationException("The product process is already running.");
        }

        var startInfo = new ProcessStartInfo(_executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(_executablePath),
        };
        startInfo.Environment[DataRootEnvironmentVariable] = _dataRoot;

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{_executablePath}'.");

        var session = new UiaSession(_process.Id);
        session.WaitForMainWindow();
        return session;
    }

    /// <summary>Ends the current process (graceful close then kill) and relaunches against the same
    /// data root, modeling the restart-preservation scenarios.</summary>
    public UiaSession Restart()
    {
        ShutdownCurrent();
        return Launch();
    }

    private void ShutdownCurrent()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(3000))
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process already gone.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose() => ShutdownCurrent();

    private static string ResolveExecutable()
    {
        // The product executable is published/copied alongside the harness output.
        string baseDirectory = AppContext.BaseDirectory;
        string local = Path.Combine(baseDirectory, "Daynote.App.exe");
        if (File.Exists(local))
        {
            return local;
        }

        // Fall back to the sibling App build output when running from source layout.
        DirectoryInfo? directory = new(baseDirectory);
        while (directory is not null)
        {
            string candidateRoot = Path.Combine(directory.FullName, "src", "Daynote.App", "bin");
            if (Directory.Exists(candidateRoot))
            {
                string? found = Directory
                    .EnumerateFiles(candidateRoot, "Daynote.App.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate Daynote.App.exe. Build the solution before running scenarios.");
    }
}
