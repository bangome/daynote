using System.IO;

namespace Daynote.App.Composition;

/// <summary>
/// Composition options. Defaults to the per-user data root under <c>%LocalAppData%\Daynote</c> for a
/// real run; tests inject a disposable root.
/// </summary>
public sealed class DaynoteAppOptions
{
    public DaynoteAppOptions(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        DataRoot = Path.GetFullPath(dataRoot);
        DatabasePath = Path.Combine(DataRoot, "daynote.db");
    }

    public string DataRoot { get; }

    public string DatabasePath { get; }

    /// <summary>
    /// Environment variable that redirects the per-user data root. This is the deterministic-QA
    /// seam consumed by <c>qa/Daynote.UiQa</c>: it lets the harness run the real product against a
    /// namespaced, disposable data root (under the real Daynote root) so QA never touches the
    /// operator's own notes. It is unset during a normal run and the app falls back to
    /// <c>%LocalAppData%\Daynote</c>.
    /// </summary>
    public const string DataRootEnvironmentVariable = "DAYNOTE_DATA_ROOT";

    public static DaynoteAppOptions ForCurrentUser()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        string root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Daynote")
            : overrideRoot;
        return new DaynoteAppOptions(root);
    }
}
