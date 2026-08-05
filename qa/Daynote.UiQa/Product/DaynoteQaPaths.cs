using System.IO;

namespace Daynote.UiQa.Product;

/// <summary>
/// Owns the single, namespaced filesystem location the harness is allowed to create and delete.
///
/// Every deterministic scenario runs the real product against a disposable data root nested
/// <em>inside</em> the real Daynote data root: <c>%LocalAppData%\Daynote\.uiqa\&lt;runId&gt;</c>.
/// Nesting under the real root means the MSIX update/uninstall/reinstall preservation checks
/// exercise the exact unvirtualized location the product ships with, while the <c>.uiqa</c>
/// namespace guarantees the operator's own notes, images, and settings are never touched.
///
/// Cleanup is deliberately narrow: the harness deletes only paths it can prove live beneath the
/// <c>.uiqa</c> namespace. There is no recursive delete of an arbitrary or caller-supplied path.
/// </summary>
public static class DaynoteQaPaths
{
    /// <summary>The <c>.uiqa</c> namespace segment. Nothing outside a directory carrying this
    /// segment is ever removed by the harness.</summary>
    public const string QaNamespaceSegment = ".uiqa";

    /// <summary>Real Daynote data root, honoring the same override the product honors so the
    /// harness and the app agree on where data lives.</summary>
    public static string RealDaynoteRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("DAYNOTE_QA_REAL_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Daynote");
    }

    /// <summary>The QA namespace root: <c>%LocalAppData%\Daynote\.uiqa</c>. The only tree the
    /// harness ever creates or clears.</summary>
    public static string QaNamespaceRoot() => Path.Combine(RealDaynoteRoot(), QaNamespaceSegment);

    /// <summary>Allocates a fresh, disposable per-run data root inside the QA namespace.</summary>
    public static string NewRunRoot(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        string root = Path.Combine(QaNamespaceRoot(), SanitizeSegment(runId));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// True only when <paramref name="candidate"/> resolves to a path strictly beneath the QA
    /// namespace root. Every delete in the harness gates on this so an accidental or malicious
    /// path can never escape the namespace.
    /// </summary>
    public static bool IsInsideQaNamespace(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string full = Path.GetFullPath(candidate);
        string namespaceRoot = Path.GetFullPath(QaNamespaceRoot());
        string prefix = namespaceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? namespaceRoot
            : namespaceRoot + Path.DirectorySeparatorChar;

        // Strictly inside: equal to the namespace root is not a deletable target.
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, namespaceRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes a QA data root. Refuses (throws) any path that is not strictly inside the QA
    /// namespace, so this method cannot be used to delete the operator's real notes or any
    /// arbitrary directory.
    /// </summary>
    public static void RemoveRunRoot(string runRoot)
    {
        if (!IsInsideQaNamespace(runRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to delete '{runRoot}': it is not inside the '{QaNamespaceSegment}' QA namespace.");
        }

        string full = Path.GetFullPath(runRoot);
        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }
    }

    private static string SanitizeSegment(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        for (int index = 0; index < value.Length; index++)
        {
            char ch = value[index];
            buffer[index] = Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch;
        }

        return new string(buffer);
    }
}
