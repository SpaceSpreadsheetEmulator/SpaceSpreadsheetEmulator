using System.Collections.ObjectModel;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;

namespace SpaceSpreadsheetEmulator.CaptureInspector.ViewModels;

public sealed class DataSourcesViewModel : ViewModelBase
{
    private readonly MainWindowViewModel owner;
    private string? ccpStaticDataArchivePath;
    private string? clientExportLocation;

    public DataSourcesViewModel(MainWindowViewModel owner)
    {
        this.owner = owner;
        ccpStaticDataArchivePath = owner.Settings.CcpStaticDataArchivePath;
        clientExportLocation = owner.Settings.ClientExportLocation;
        IdentifierResolution = new ObservableCollection<IdentifierResolutionOption>(
            IdentifierFields.Supported.Select(field => new IdentifierResolutionOption(
                field,
                owner.Settings.IdentifierResolution.GetValueOrDefault(field, true))));
    }

    public ObservableCollection<IdentifierResolutionOption> IdentifierResolution { get; }

    public string? CcpStaticDataArchivePath
    {
        get => ccpStaticDataArchivePath;
        set => SetProperty(ref ccpStaticDataArchivePath, value);
    }

    public string? ClientExportLocation
    {
        get => clientExportLocation;
        set => SetProperty(ref clientExportLocation, value);
    }

    public string? ErrorMessage { get; private set; }

    public async Task<bool> ApplyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (IdentifierResolutionOption option in IdentifierResolution)
            {
                owner.Settings.IdentifierResolution[option.FieldName] = option.Enabled;
            }

            await owner.UpdateDataSourcesAsync(CcpStaticDataArchivePath, ClientExportLocation, cancellationToken);
            owner.SaveIdentifierResolution();
            ErrorMessage = null;
            OnPropertyChanged(nameof(ErrorMessage));
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ErrorMessage = exception.Message;
            OnPropertyChanged(nameof(ErrorMessage));
            return false;
        }
    }
}

public sealed class IdentifierResolutionOption : ViewModelBase
{
    private bool enabled;

    public IdentifierResolutionOption(string fieldName, bool enabled)
    {
        FieldName = fieldName;
        this.enabled = enabled;
    }

    public string FieldName { get; }

    public bool Enabled
    {
        get => enabled;
        set => SetProperty(ref enabled, value);
    }
}
