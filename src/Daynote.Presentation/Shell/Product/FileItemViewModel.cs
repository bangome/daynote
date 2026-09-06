using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.Core.Files;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One attachment card in the 파일 tab. Image files with available bytes show a thumbnail band; other
/// files show an extension badge. Size labels follow the design's fmtSize (MB / KB / B).
/// </summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    private readonly Func<FileItemViewModel, Task> _onDelete;

    public FileItemViewModel(DayFile file, object? thumbnail, Func<FileItemViewModel, Task> onDelete)
    {
        Id = file.Id;
        Name = file.DisplayName;
        Ext = ExtensionOf(file.DisplayName);
        IsImage = file.IsImage;
        Thumbnail = thumbnail;
        SizeLabel = string.Create(CultureInfo.CurrentCulture, $"{FormatSize(file.ByteLength)} · {Ext}");
        _onDelete = onDelete;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Ext { get; }

    public bool IsImage { get; }

    public object? Thumbnail { get; }

    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Image file whose bytes are missing: show the image badge instead of a thumbnail.</summary>
    public bool ShowImageBadge => IsImage && Thumbnail is null;

    /// <summary>Non-image file: show the extension badge.</summary>
    public bool ShowDocBadge => !IsImage;

    public string SizeLabel { get; }

    /// <summary>True when a body file-link click targeted this card; drives the accent border.</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    [RelayCommand]
    private Task Delete() => _onDelete(this);

    private static string ExtensionOf(string name)
    {
        int dot = name.LastIndexOf('.');
        string ext = dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : string.Empty;
        return ext.ToUpperInvariant() is { Length: > 4 } trimmed ? trimmed[..4] : ext.ToUpperInvariant();
    }

    /// <summary>Design fmtSize: &gt;1MB → "X.X MB"; &gt;1KB → "N KB"; else "N B".</summary>
    public static string FormatSize(long bytes) => bytes > 1_048_576
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1_048_576.0:0.0} MB")
        : bytes > 1024
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)Math.Round(bytes / 1024.0)} KB")
            : string.Create(CultureInfo.CurrentCulture, $"{bytes} B");
}
