using System.Text.Json;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;
using SpaceSpreadsheetEmulator.CaptureInspector.Services;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Tests;

public sealed class DecodeTreeBuilderTests
{
    [Fact]
    public async Task BuildAsyncAppendsNamesOnlyForEnabledKnownFields()
    {
        using JsonDocument document = JsonDocument.Parse("""{"typeID":34,"itemID":999,"nested":[true]}""");
        var settings = IdentifierFields.DefaultSettings();
        var resolver = new TestResolver();

        IReadOnlyList<DecodeTreeNode> tree = await DecodeTreeBuilder.BuildAsync(document.RootElement, settings, resolver);

        DecodeTreeNode root = Assert.Single(tree);
        Assert.Contains(root.Children, node => node.DisplayText == "typeID: 34 — Tritanium");
        Assert.Contains(root.Children, node => node.DisplayText == "itemID: 999");
        Assert.Equal(["typeID"], resolver.RequestedFields);
    }

    private sealed class TestResolver : IIdentifierResolver
    {
        public List<string> RequestedFields { get; } = [];

        public ValueTask<string?> ResolveAsync(string fieldName, long identifier, CancellationToken cancellationToken = default)
        {
            RequestedFields.Add(fieldName);
            return ValueTask.FromResult<string?>(fieldName == "typeID" && identifier == 34 ? "Tritanium" : null);
        }
    }
}
