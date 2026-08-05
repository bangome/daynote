using Daynote.UiQa.Product;

namespace Daynote.UiQa.Tests;

[TestClass]
public sealed class DaynoteQaPathsTests
{
    [TestMethod]
    public void QaNamespaceRoot_ends_with_the_uiqa_segment()
    {
        Assert.IsTrue(DaynoteQaPaths.QaNamespaceRoot().EndsWith(DaynoteQaPaths.QaNamespaceSegment, StringComparison.Ordinal));
    }

    [TestMethod]
    public void A_path_inside_the_namespace_is_accepted()
    {
        string inside = Path.Combine(DaynoteQaPaths.QaNamespaceRoot(), "run-123");
        Assert.IsTrue(DaynoteQaPaths.IsInsideQaNamespace(inside));
    }

    [TestMethod]
    public void The_namespace_root_itself_is_not_a_deletable_target()
    {
        Assert.IsFalse(DaynoteQaPaths.IsInsideQaNamespace(DaynoteQaPaths.QaNamespaceRoot()));
    }

    [TestMethod]
    public void The_real_daynote_root_and_arbitrary_paths_are_rejected()
    {
        Assert.IsFalse(DaynoteQaPaths.IsInsideQaNamespace(DaynoteQaPaths.RealDaynoteRoot()));
        Assert.IsFalse(DaynoteQaPaths.IsInsideQaNamespace(@"C:\Windows"));
        Assert.IsFalse(DaynoteQaPaths.IsInsideQaNamespace(
            Path.Combine(DaynoteQaPaths.RealDaynoteRoot(), "daynote.db")));
        Assert.IsFalse(DaynoteQaPaths.IsInsideQaNamespace(string.Empty));
    }

    [TestMethod]
    public void RemoveRunRoot_refuses_a_path_outside_the_namespace()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => DaynoteQaPaths.RemoveRunRoot(@"C:\Windows"));
        Assert.ThrowsExactly<InvalidOperationException>(() => DaynoteQaPaths.RemoveRunRoot(DaynoteQaPaths.RealDaynoteRoot()));
        Assert.ThrowsExactly<InvalidOperationException>(() => DaynoteQaPaths.RemoveRunRoot(DaynoteQaPaths.QaNamespaceRoot()));
    }

    [TestMethod]
    public void RemoveRunRoot_on_a_nonexistent_namespaced_path_is_a_safe_no_op()
    {
        string ghost = Path.Combine(DaynoteQaPaths.QaNamespaceRoot(), "never-created-" + Guid.NewGuid().ToString("N"));
        DaynoteQaPaths.RemoveRunRoot(ghost); // must not throw
    }
}
