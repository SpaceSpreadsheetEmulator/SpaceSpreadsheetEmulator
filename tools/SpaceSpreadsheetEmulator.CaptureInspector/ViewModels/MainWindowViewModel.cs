using System.Collections.ObjectModel;
using System.IO.Abstractions;
using System.Text.Json;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int MaximumFrames = 100_000;
    private readonly StaticDataCatalog catalog;
    private readonly InspectorSettingsStore settingsStore;
    private readonly CaptureFramesReader captureFramesReader;
    private readonly IFileSystem fileSystem;
    private readonly PacketTreeBuilder packetTreeBuilder = new();
    private readonly FrameDecoder frameDecoder = new();
    private readonly List<CaptureFrame> allFrames = [];
    private CancellationTokenSource? selectionCancellation;
    private CancellationTokenSource? timelineCancellation;
    private InventoryTimeline? inventoryTimeline;
    private FrameSourceReference? pendingSourceNavigation;
    private string searchText = string.Empty;
    private bool showInbound = true;
    private bool showOutbound = true;
    private bool showAnnotations = true;
    private string selectedMessageType = "All";
    private string selectedDecodeStatus = "All";
    private CaptureFrame? selectedFrame;
    private DecodeTreeNode? selectedDecodeNode;
    private DecodeTreeNode? selectedStateNode;
    private IReadOnlyList<WireByteRange> selectedByteRanges = [];
    private byte[] decodedBytes = [];
    private byte[] capturedBytes = [];
    private bool hasDifferentCapturedBytes;
    private int selectedByteTabIndex;
    private int selectedDetailTabIndex;
    private int? captureClientBuild;

    public MainWindowViewModel()
        : this(CreateDefaultDependencies())
    {
    }

    public MainWindowViewModel(
        StaticDataCatalog catalog,
        InspectorSettingsStore settingsStore,
        CaptureFramesReader captureFramesReader,
        IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(captureFramesReader);
        ArgumentNullException.ThrowIfNull(fileSystem);
        this.catalog = catalog;
        this.settingsStore = settingsStore;
        this.captureFramesReader = captureFramesReader;
        this.fileSystem = fileSystem;
        Settings = settingsStore.Load();
    }

    private MainWindowViewModel(DefaultDependencies dependencies)
        : this(
            dependencies.Catalog,
            dependencies.SettingsStore,
            dependencies.CaptureFramesReader,
            dependencies.FileSystem)
    {
    }

    public ObservableCollection<CaptureFrame> VisibleFrames { get; } = [];

    public ObservableCollection<DecodeTreeNode> DecodeTree { get; } = [];

    public ObservableCollection<DecodeTreeNode> StateTree { get; } = [];

    public ObservableCollection<string> MessageTypes { get; } = ["All"];

    public ObservableCollection<string> DecodeStatuses { get; } = ["All"];

    public InspectorSettings Settings { get; }

    public string Title { get; private set; } = "No decoded frames file selected";

    public string Summary { get; private set; } =
        "Open a decoder schema-v2 frames.jsonl export to inspect packets locally.";

    public string SourceStatus { get; private set; } =
        $"Decoder profile build {ProtocolProfileCatalog.SupportedBuild}; no CCP static-data archive loaded.";

    public string ByteStatus { get; private set; } =
        "Select a frame to view its decoded packet bytes.";

    public string ReconstructionStatus { get; private set; } =
        "Open a capture to reconstruct inventory state.";

    public string ErrorMessage { get; private set; } = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public byte[] DecodedBytes
    {
        get => decodedBytes;
        private set => SetProperty(ref decodedBytes, value);
    }

    public byte[] CapturedBytes
    {
        get => capturedBytes;
        private set => SetProperty(ref capturedBytes, value);
    }

    public IReadOnlyList<WireByteRange> SelectedByteRanges
    {
        get => selectedByteRanges;
        private set => SetProperty(ref selectedByteRanges, value);
    }

    public bool HasDifferentCapturedBytes
    {
        get => hasDifferentCapturedBytes;
        private set => SetProperty(ref hasDifferentCapturedBytes, value);
    }

    public int SelectedByteTabIndex
    {
        get => selectedByteTabIndex;
        set => SetProperty(ref selectedByteTabIndex, value);
    }

    public int SelectedDetailTabIndex
    {
        get => selectedDetailTabIndex;
        set => SetProperty(ref selectedDetailTabIndex, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowInbound
    {
        get => showInbound;
        set
        {
            if (SetProperty(ref showInbound, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowOutbound
    {
        get => showOutbound;
        set
        {
            if (SetProperty(ref showOutbound, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowAnnotations
    {
        get => showAnnotations;
        set
        {
            if (SetProperty(ref showAnnotations, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedMessageType
    {
        get => selectedMessageType;
        set
        {
            if (SetProperty(ref selectedMessageType, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedDecodeStatus
    {
        get => selectedDecodeStatus;
        set
        {
            if (SetProperty(ref selectedDecodeStatus, value))
            {
                ApplyFilters();
            }
        }
    }

    public CaptureFrame? SelectedFrame
    {
        get => selectedFrame;
        set
        {
            if (SetProperty(ref selectedFrame, value))
            {
                _ = UpdateSelectedFrameAsync(value);
            }
        }
    }

    public DecodeTreeNode? SelectedDecodeNode
    {
        get => selectedDecodeNode;
        set
        {
            if (!SetProperty(ref selectedDecodeNode, value))
            {
                return;
            }

            if (value is null)
            {
                SelectedByteRanges = [];
                return;
            }

            SelectedByteTabIndex = 0;
            SelectedByteRanges = value.SelectionRanges;
            ByteStatus = value.SelectionRanges.Count == 0
                ? "This decoded node has no byte provenance in the exported frame."
                : FormatByteSelection(value.SelectionRanges);
            OnPropertyChanged(nameof(ByteStatus));
        }
    }

    public DecodeTreeNode? SelectedStateNode
    {
        get => selectedStateNode;
        set
        {
            if (!SetProperty(ref selectedStateNode, value))
            {
                return;
            }

            SelectedByteRanges = [];
            int sourceCount = value?.SourceFrames.Select(static source => source.FrameIndex).Distinct().Count() ?? 0;
            ByteStatus = sourceCount switch
            {
                > 1 => $"This value is reconstructed from {sourceCount} frames. Expand Sources or select an observed field, then use Go to source.",
                1 => $"This value comes from frame #{value!.SourceFrames[0].FrameIndex}. Use Go to source to inspect its bytes.",
                _ => "This derived node has no single packet byte range.",
            };
            OnPropertyChanged(nameof(ByteStatus));
            OnPropertyChanged(nameof(CanGoToSelectedSource));
        }
    }

    public bool CanGoToSelectedSource => SelectedStateNode?.CanNavigateToSource == true;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Settings.CcpStaticDataArchivePath))
        {
            UpdateSourceStatus();
            return;
        }

        try
        {
            await catalog.LoadArchiveAsync(Settings.CcpStaticDataArchivePath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            ErrorMessage = $"Saved CCP data could not be loaded: {exception.Message}";
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
        }

        UpdateSourceStatus();
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            CaptureLoadResult result = await captureFramesReader.ReadAsync(
                path,
                MaximumFrames,
                cancellationToken);
            allFrames.Clear();
            allFrames.AddRange(result.Frames);
            captureClientBuild = DetectClientBuild(result.Frames);
            UpdateFilterOptions();
            ApplyFilters();
            Title = fileSystem.Path.GetFileName(path);
            Summary = BuildSummary(result);
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
            UpdateSourceStatus();

            timelineCancellation?.Cancel();
            timelineCancellation?.Dispose();
            timelineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            inventoryTimeline = null;
            ReconstructionStatus = "Reconstructing recognized inventory events…";
            OnPropertyChanged(nameof(ReconstructionStatus));
            StateTree.Clear();
            StateTree.Add(new DecodeTreeNode(
                "Reconstructed inventory",
                "Indexing capture frames…",
                [],
                Origin: DecodeNodeOrigin.Diagnostic));

            SelectedFrame = VisibleFrames.FirstOrDefault();
            _ = BuildTimelineAsync(result.Frames, timelineCancellation.Token);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            ErrorMessage = exception.Message;
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
        }
    }

    private static DefaultDependencies CreateDefaultDependencies()
    {
        IFileSystem fileSystem = new FileSystem();
        InspectorApplicationPaths paths = InspectorApplicationPaths.CreateDefault(fileSystem);
        return new DefaultDependencies(
            new StaticDataCatalog(fileSystem, paths.CacheDirectory, TimeProvider.System),
            new InspectorSettingsStore(fileSystem, paths.ConfigurationDirectory),
            new CaptureFramesReader(fileSystem),
            fileSystem);
    }

    private sealed record DefaultDependencies(
        StaticDataCatalog Catalog,
        InspectorSettingsStore SettingsStore,
        CaptureFramesReader CaptureFramesReader,
        IFileSystem FileSystem);

    public async Task UpdateDataSourcesAsync(
        string? archivePath,
        string? clientExportLocation,
        CancellationToken cancellationToken = default)
    {
        string? selectedArchive = string.IsNullOrWhiteSpace(archivePath) ? null : archivePath;
        string? selectedClientExport = string.IsNullOrWhiteSpace(clientExportLocation)
            ? null
            : clientExportLocation;

        try
        {
            if (selectedArchive is not null)
            {
                await catalog.LoadArchiveAsync(selectedArchive, cancellationToken);
            }
            else
            {
                await catalog.ClearAsync();
            }

            Settings.CcpStaticDataArchivePath = selectedArchive;
            Settings.ClientExportLocation = selectedClientExport;
            settingsStore.Save(Settings);
            UpdateSourceStatus();
            await UpdateSelectedFrameAsync(SelectedFrame);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            ErrorMessage = exception.Message;
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
            throw;
        }
    }

    public void SaveIdentifierResolution()
    {
        settingsStore.Save(Settings);
        _ = UpdateSelectedFrameAsync(SelectedFrame);
    }

    public void GoToSelectedSource()
    {
        if (SelectedStateNode is not { CanNavigateToSource: true }
            || SelectedStateNode.SourceFrames[0] is not { } source)
        {
            return;
        }

        CaptureFrame? frame = allFrames.FirstOrDefault(item => item.FrameIndex == source.FrameIndex);
        if (frame is null)
        {
            ErrorMessage = $"Source frame #{source.FrameIndex} is not present in this export.";
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
            return;
        }

        pendingSourceNavigation = source;
        SelectedDetailTabIndex = 0;
        if (ReferenceEquals(SelectedFrame, frame))
        {
            ApplyPendingSourceNavigation(frame);
        }
        else
        {
            SelectedFrame = frame;
        }
    }

    private async Task BuildTimelineAsync(
        IReadOnlyList<CaptureFrame> frames,
        CancellationToken cancellationToken)
    {
        try
        {
            InventoryTimeline timeline = await InventoryTimeline.BuildAsync(frames, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            inventoryTimeline = timeline;
            ReconstructionStatus = timeline.EventCount == 0
                ? "No recognized inventory events were found in this capture."
                : $"{timeline.EventCount:N0} recognized inventory event(s); state follows the selected frame.";
            OnPropertyChanged(nameof(ReconstructionStatus));
            await UpdateStateTreeAsync(SelectedFrame, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException)
        {
            ReconstructionStatus = $"Inventory reconstruction stopped: {exception.Message}";
            OnPropertyChanged(nameof(ReconstructionStatus));
        }
    }

    private void ApplyFilters()
    {
        var filter = new CaptureFrameFilter(
            SearchText,
            ShowInbound,
            ShowOutbound,
            ShowAnnotations,
            SelectedMessageType,
            SelectedDecodeStatus);
        IEnumerable<CaptureFrame> filtered = filter.Apply(allFrames);
        VisibleFrames.Clear();
        foreach (CaptureFrame frame in filtered)
        {
            VisibleFrames.Add(frame);
        }

        if (SelectedFrame is not null && !VisibleFrames.Contains(SelectedFrame))
        {
            SelectedFrame = VisibleFrames.FirstOrDefault();
        }
    }

    private void UpdateFilterOptions()
    {
        ReplaceOptions(MessageTypes, allFrames.Select(static frame => frame.MessageType));
        ReplaceOptions(DecodeStatuses, allFrames.Select(static frame => frame.DecodeStatus));
        SelectedMessageType = "All";
        SelectedDecodeStatus = "All";
    }

    private static void ReplaceOptions(
        ObservableCollection<string> destination,
        IEnumerable<string> source)
    {
        destination.Clear();
        destination.Add("All");
        foreach (string value in source
                     .Where(static value => value != "—")
                     .Distinct(StringComparer.Ordinal)
                     .Order())
        {
            destination.Add(value);
        }
    }

    private async Task UpdateSelectedFrameAsync(CaptureFrame? frame)
    {
        selectionCancellation?.Cancel();
        selectionCancellation?.Dispose();
        selectionCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = selectionCancellation.Token;
        DecodeTree.Clear();
        SelectedDecodeNode = null;

        if (frame is null)
        {
            DecodedBytes = [];
            CapturedBytes = [];
            SelectedByteRanges = [];
            ByteStatus = "Select a frame to view its decoded packet bytes.";
            OnPropertyChanged(nameof(ByteStatus));
            return;
        }

        try
        {
            PacketTreeBuildResult result = await packetTreeBuilder.BuildResultAsync(
                frame,
                Settings.IdentifierResolution,
                catalog,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DecodeTreeNode node in result.Nodes)
            {
                DecodeTree.Add(node);
            }

            DecodedBytes = result.Frame.DecodedBytes;
            CapturedBytes = result.Frame.CapturedBytes;
            HasDifferentCapturedBytes = result.Frame.HasDifferentCapturedBytes;
            SelectedByteTabIndex = 0;
            SelectedByteRanges = [];
            ByteStatus = result.Frame.WasDecompressed
                ? $"Decoded marshal bytes ({DecodedBytes.Length:N0}); captured zlib payload is {CapturedBytes.Length:N0} bytes."
                : $"Packet bytes ({DecodedBytes.Length:N0}). Select a decoded field to highlight it.";
            OnPropertyChanged(nameof(ByteStatus));

            if (result.ClientBuild is int build && captureClientBuild != build)
            {
                captureClientBuild = build;
                UpdateSourceStatus();
            }

            ApplyPendingSourceNavigation(frame);
            await UpdateStateTreeAsync(frame, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task UpdateStateTreeAsync(
        CaptureFrame? frame,
        CancellationToken cancellationToken)
    {
        if (frame is null || inventoryTimeline is null)
        {
            return;
        }

        InventorySnapshot snapshot = inventoryTimeline.GetSnapshot(frame.FrameIndex);
        IReadOnlyList<DecodeTreeNode> nodes = await InventoryHierarchyBuilder.BuildAsync(
            snapshot,
            Settings.IdentifierResolution,
            catalog,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(frame, SelectedFrame))
        {
            return;
        }

        StateTree.Clear();
        foreach (DecodeTreeNode node in nodes)
        {
            StateTree.Add(node);
        }

        SelectedStateNode = null;
        ReconstructionStatus =
            $"Reconstructed state as of frame #{frame.FrameIndex} · {snapshot.Items.Count:N0} item(s).";
        OnPropertyChanged(nameof(ReconstructionStatus));
    }

    private void ApplyPendingSourceNavigation(CaptureFrame frame)
    {
        if (pendingSourceNavigation is not { } source || source.FrameIndex != frame.FrameIndex)
        {
            return;
        }

        SelectedByteRanges = source.ByteRanges;
        SelectedByteTabIndex = 0;
        ByteStatus = source.ByteRanges.Count == 0
            ? $"Frame #{frame.FrameIndex} is the source, but the export has no exact field byte range."
            : $"Source frame #{frame.FrameIndex} · {FormatByteSelection(source.ByteRanges)}";
        OnPropertyChanged(nameof(ByteStatus));
        pendingSourceNavigation = null;
    }

    private void UpdateSourceStatus()
    {
        int decoderBuild = ProtocolProfileCatalog.SupportedBuild;
        string capture = captureClientBuild is int clientBuild
            ? $"capture client build {clientBuild}"
            : "capture client build unknown";
        string sde = catalog.Compatibility is { } compatibility
            ? $"CCP SDE build {compatibility.SdeBuild}"
            : "no CCP SDE loaded";
        int[] knownBuilds =
        [
            .. new int?[]
            {
                captureClientBuild,
                decoderBuild,
                catalog.Compatibility?.SdeBuild,
            }.Where(static value => value.HasValue).Select(static value => value!.Value),
        ];
        string warning = knownBuilds.Distinct().Count() > 1
            ? " · BUILD MISMATCH: decoding and resolved names are best effort"
            : string.Empty;
        SourceStatus = $"{capture} · decoder profile {decoderBuild} · {sde}{warning}.";
        OnPropertyChanged(nameof(SourceStatus));
    }

    private int? DetectClientBuild(IEnumerable<CaptureFrame> frames)
    {
        foreach (CaptureFrame frame in frames)
        {
            DecodedFrame decoded = frameDecoder.Decode(frame);
            if (decoded.MachoPacket is not null
                || decoded.RootValue is not PyTuple { Items.Length: 7 } tuple
                || tuple.Items[4] is not PyInteger build
                || build.Value is < int.MinValue or > int.MaxValue)
            {
                continue;
            }

            return (int)build.Value;
        }

        return null;
    }

    private static string BuildSummary(CaptureLoadResult result)
    {
        string summary = $"{result.Frames.Count:N0} supported frame(s) loaded.";
        if (result.Diagnostics.Count > 0)
        {
            summary += $" {result.Diagnostics.Count:N0} unsupported or malformed record(s) skipped.";
        }

        return result.IsTruncated
            ? $"{summary} Display limited to {MaximumFrames:N0} frames."
            : summary;
    }

    private static string FormatByteSelection(IReadOnlyList<WireByteRange> ranges)
    {
        long total = ranges.Sum(static range => (long)range.Length);
        string offsets = string.Join(
            ", ",
            ranges.Select(range => range.Length == 1
                ? $"0x{range.Offset:X}"
                : $"0x{range.Offset:X}–0x{range.End - 1:X}"));
        return $"Selected {total:N0} byte(s) at {offsets}.";
    }

    public void Dispose()
    {
        selectionCancellation?.Cancel();
        selectionCancellation?.Dispose();
        timelineCancellation?.Cancel();
        timelineCancellation?.Dispose();
    }
}
