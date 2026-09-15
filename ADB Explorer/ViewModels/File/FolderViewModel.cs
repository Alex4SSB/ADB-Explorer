using ADB_Explorer.Converters;
using ADB_Explorer.Models;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.ViewModels;

public partial class FolderViewModel(FileClass file) : FileViewModelBase(file)
{
    [ObservableProperty]
    public partial bool ExtensionIsGlyph { get; set; }

    [ObservableProperty]
    public partial bool ExtensionIsFontIcon { get; set; }

    [ObservableProperty]
    public partial bool IsCalculatingSize { get; set; }

    private long? _calculatedSize;
    private bool HasCalculatedSize => _calculatedSize is not null;

    public bool ShowCalculateSizeButton =>
        _file.IsDirectory && !_file.IsLink && !IsCalculatingSize && _calculatedSize is null;

    /// <summary>Shares the size column's TextBlock between a file's size, "Calculating…", and a folder's computed size.</summary>
    public string SizeColumnText
    {
        get
        {
            if (IsCalculatingSize)
                return Strings.Resources.S_STATUS_CALCULATING_SIZE;

            if (_file.IsDirectory)
                return _calculatedSize?.BytesToSize(true) ?? "";

            return SizeString;
        }
    }

    public bool ShowSizeText =>
        !ShowCalculateSizeButton
        && (IsCalculatingSize || HasCalculatedSize || (_file.Type is AbstractFile.FileType.File && !_file.IsLink));

    private CancellationTokenSource? _sizeCalcCts;

    [RelayCommand]
    private void CalculateSize()
    {
        if (!ShowCalculateSizeButton)
            return;

        _sizeCalcCts = new CancellationTokenSource();
        var token = _sizeCalcCts.Token;
        var file = _file;

        IsCalculatingSize = true;
        NotifySizeStateChanged();

        Task.Run(() =>
        {
            FolderTree[]? tree;
            try
            {
                tree = file.GetChildren();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                tree = null;
            }

            App.SafeBeginInvoke(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                IsCalculatingSize = false;
                ApplyCalculatedSize(tree);
            });
        }, token);
    }

    /// <summary>Applies a folder tree already fetched for drag/drop, sparing the button a redundant device round trip.</summary>
    public void SetCalculatedSizeFromTree(FolderTree[]? tree)
    {
        if (IsCalculatingSize || _calculatedSize is not null)
            return;

        ApplyCalculatedSize(tree);
    }

    private void ApplyCalculatedSize(FolderTree[]? tree)
    {
        _calculatedSize = tree?.Where(t => !t.IsFolder).Sum(t => t.Size ?? 0) ?? 0;
        NotifySizeStateChanged();
    }

    private void NotifySizeStateChanged()
    {
        OnPropertyChanged(nameof(ShowCalculateSizeButton));
        OnPropertyChanged(nameof(ShowSizeText));
        OnPropertyChanged(nameof(SizeColumnText));
    }

    /// <summary>Cancels any in-flight calculation and forgets a completed result - called when the row leaves the current folder.</summary>
    public void CancelSizeCalculation()
    {
        if (_sizeCalcCts is null && _calculatedSize is null)
            return;

        _sizeCalcCts?.Cancel();
        _sizeCalcCts?.Dispose();
        _sizeCalcCts = null;
        _calculatedSize = null;
        IsCalculatingSize = false;
        _file.CachedChildren = null;

        NotifySizeStateChanged();
    }

    public override void OnSizeChanged()
    {
        base.OnSizeChanged();
        OnPropertyChanged(nameof(SizeColumnText));
    }

    public override void UpdateType()
    {
        base.UpdateType();
        UpdateExtensionDisplay();
    }

    private void UpdateExtensionDisplay()
    {
        if (_file.Type is not AbstractFile.FileType.File
            || _file.IsApk
            || string.IsNullOrEmpty(_file.FullName)
            || (_file.IsHidden && _file.FullName.Count(c => c == '.') == 1)
            || _file.Extension.Equals(".exe", StringComparison.CurrentCultureIgnoreCase))
        {
            ExtensionIsGlyph = false;
            ExtensionIsFontIcon = false;
            return;
        }

        if (!Ascii.IsValid(_file.Extension))
        {
            if (ShortExtension.Length == 1)
            {
                ExtensionIsGlyph = true;
                ExtensionIsFontIcon = false;
            }
            else if (ShortExtension.Length > 1)
            {
                ExtensionIsGlyph = false;
                ExtensionIsFontIcon = true;
            }
            else
            {
                ExtensionIsGlyph = false;
                ExtensionIsFontIcon = false;
            }
        }
        else
        {
            ExtensionIsGlyph = false;
            ExtensionIsFontIcon = false;
        }
    }

    public override void Dispose()
    {
        CancelSizeCalculation();

        ExtensionIsGlyph = false;
        ExtensionIsFontIcon = false;

        base.Dispose();
    }
}
