using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SpaceSpreadsheetEmulator.CaptureInspector.ViewModels;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Views;

public partial class DataSourcesWindow : Window
{
    public DataSourcesWindow()
    {
        InitializeComponent();
    }

    private async void ChooseArchive(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CCP JSONL archive") { Patterns = ["*.zip"] }],
            Title = "Select CCP JSONL archive",
        });

        if (files.Count > 0 && DataContext is DataSourcesViewModel viewModel)
        {
            viewModel.CcpStaticDataArchivePath = files[0].Path.LocalPath;
        }
    }

    private async void ChooseClientExportLocation(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select client-export location",
        });
        IStorageFolder? folder = folders.Count == 0 ? null : folders[0];

        if (folder is not null && DataContext is DataSourcesViewModel viewModel)
        {
            viewModel.ClientExportLocation = folder.Path.LocalPath;
        }
    }

    private async void Apply(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (DataContext is DataSourcesViewModel viewModel && await viewModel.ApplyAsync())
        {
            Close();
        }
    }

    private void Cancel(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => Close();
}
