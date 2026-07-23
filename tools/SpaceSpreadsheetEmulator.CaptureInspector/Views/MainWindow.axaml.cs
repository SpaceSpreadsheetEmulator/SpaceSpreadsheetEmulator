using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SpaceSpreadsheetEmulator.CaptureInspector.ViewModels;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OpenCaptureFile(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Decoded frame exports")
                {
                    Patterns = ["frames.jsonl", "frames-*.jsonl", "*.frames.jsonl"],
                },
            ],
            Title = "Open decoded EVE frames export",
        });

        if (files.Count == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.LoadAsync(files[0].Path.LocalPath);
    }

    private async void OpenDataSources(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new DataSourcesWindow { DataContext = new DataSourcesViewModel(viewModel) };
        await dialog.ShowDialog(this);
    }

    private void GoToSelectedSource(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.GoToSelectedSource();
        }
    }
}
