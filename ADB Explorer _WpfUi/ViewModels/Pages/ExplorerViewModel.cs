using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels.Pages;

public partial class ExplorerViewModel : ObservableObject
{
    public ExplorerViewModel()
    {
        Data.FileActions.PropertyChanged += FileActions_PropertyChanged;
    }

    private void FileActions_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileActionsEnable.SelectedItemsCount):
                OnPropertyChanged(nameof(SelectedFilesTotalSize));
                OnPropertyChanged(nameof(SelectedFilesCount));
                break;

            default:
                break;
        }
    }

    public string SelectedFilesTotalSize => (Data.SelectedFiles is not null && FileHelper.TotalSize(Data.SelectedFiles) is long size and > 0) ? size.BytesToSize() : "";
    public string SelectedFilesCount => $"{(Data.FileActions.IsAppDrive ? Data.SelectedPackages.Count() : Data.SelectedFiles.Count())}";

}
