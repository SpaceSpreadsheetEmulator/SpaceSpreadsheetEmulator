using System.Text.Json;
using System.Globalization;
using SpaceSpreadsheetEmulator.CaptureInspector.Models;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public static class DecodeTreeBuilder
{
    public static async Task<IReadOnlyList<DecodeTreeNode>> BuildAsync(
        JsonElement? payload,
        IReadOnlyDictionary<string, bool> identifierResolution,
        IIdentifierResolver resolver,
        CancellationToken cancellationToken = default)
    {
        if (payload is null)
        {
            return [];
        }

        return [await BuildNodeAsync("decoded_payload", payload.Value, identifierResolution, resolver, cancellationToken)];
    }

    private static async Task<DecodeTreeNode> BuildNodeAsync(
        string name,
        JsonElement element,
        IReadOnlyDictionary<string, bool> identifierResolution,
        IIdentifierResolver resolver,
        CancellationToken cancellationToken)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var children = new List<DecodeTreeNode>();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    children.Add(await BuildNodeAsync(property.Name, property.Value, identifierResolution, resolver, cancellationToken));
                }

                return new DecodeTreeNode(name, $"object ({children.Count})", children);
            }
            case JsonValueKind.Array:
            {
                var children = new List<DecodeTreeNode>();
                var index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    children.Add(await BuildNodeAsync($"[{index++}]", item, identifierResolution, resolver, cancellationToken));
                }

                return new DecodeTreeNode(name, $"list ({children.Count})", children);
            }
            case JsonValueKind.Number when element.TryGetInt64(out long identifier):
            {
                string value = identifier.ToString(CultureInfo.InvariantCulture);
                if (identifierResolution.TryGetValue(name, out bool enabled) && enabled)
                {
                    string? resolved = await resolver.ResolveAsync(name, identifier, cancellationToken);
                    if (resolved is not null)
                    {
                        value = $"{value} — {resolved}";
                    }
                }

                return new DecodeTreeNode(name, value, []);
            }
            case JsonValueKind.String:
                return new DecodeTreeNode(name, element.GetString() ?? string.Empty, []);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new DecodeTreeNode(name, element.GetBoolean().ToString().ToLowerInvariant(), []);
            case JsonValueKind.Null:
                return new DecodeTreeNode(name, "null", []);
            default:
                return new DecodeTreeNode(name, element.GetRawText(), []);
        }
    }
}
