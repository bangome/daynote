namespace Daynote.Infrastructure.Assets;

/// <summary>The points in a content-addressed store's write/delete pipeline where a fault can be injected.</summary>
public enum ImageAssetFaultPoint
{
    TempCreate,
    Write,
    Flush,
    Rename,
    Delete,
}

/// <summary>
/// Test seam for injecting I/O faults into the content-addressed stores at a chosen pipeline point, so
/// crash-recovery and reconciliation paths can be exercised deterministically.
/// </summary>
public interface IImageAssetFaultInjector
{
    void At(ImageAssetFaultPoint point, string path);
}
