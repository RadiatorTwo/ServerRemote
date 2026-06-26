using CommunityToolkit.Mvvm.ComponentModel;
using ServerRemote.App.Models;
using ServerRemote.Contracts;

namespace ServerRemote.App.ViewModels;

/// <summary>
/// A drive row with usage status, color, and human-readable GB values.
/// Updated in-place during polling (stable instance) so the UI is not
/// rebuilt on every refresh.
/// </summary>
public sealed partial class DriveItemViewModel : ObservableObject
{
    private DriveDto _dto;

    public DriveItemViewModel(DriveDto dto) => _dto = dto;

    /// <summary>Stable key for reusing the instance (drive letter).</summary>
    public string Key => _dto.Name;

    /// <summary>Updates the underlying data and notifies all bindings.</summary>
    public void Update(DriveDto dto)
    {
        _dto = dto;
        OnPropertyChanged(string.Empty); // re-evaluate all computed properties
    }

    public string Name => string.IsNullOrWhiteSpace(_dto.VolumeLabel)
        ? _dto.Name
        : $"{_dto.Name}  ·  {_dto.VolumeLabel}";

    public double UsedPercent => _dto.UsedPercent;
    public double UsedFraction => Math.Clamp(_dto.UsedPercent / 100.0, 0, 1);

    public DriveStatus Status => DriveStatusExtensions.FromUsage(_dto.UsedPercent, _dto.FreeBytes);
    public bool IsCritical => Status == DriveStatus.Critical;
    public string StatusLabel => Status.ToLabel();

    public string UsedText => $"{ToGb(_dto.UsedBytes):0.0} GB used";
    public string FreeText => $"{ToGb(_dto.FreeBytes):0.0} GB free";
    public string SummaryText => $"{ToGb(_dto.UsedBytes):0.0} / {ToGb(_dto.TotalBytes):0.0} GB";

    public Color StatusColor => Status switch
    {
        DriveStatus.Critical => Resolve("Danger", Color.FromArgb("#E11D48")),
        DriveStatus.Warning => Resolve("Warning", Color.FromArgb("#F59E0B")),
        _ => Resolve("Success", Color.FromArgb("#22C55E"))
    };

    private static double ToGb(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;

    private static Color Resolve(string keyName, Color fallback) =>
        Application.Current?.Resources.TryGetValue(keyName, out var v) == true && v is Color c ? c : fallback;
}
