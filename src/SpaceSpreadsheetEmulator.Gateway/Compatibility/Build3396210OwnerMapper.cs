using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Maps known character and corporation owners to build-3396210 lookup response shapes.
/// </summary>
internal static class Build3396210OwnerMapper
{
    private static readonly Build3396210RowField[] CorporationTickerFields =
    [
        new("corporationID", 3),
        new("tickerName", 130),
        new("shape1", 2),
        new("shape2", 2),
        new("shape3", 2),
        new("color1", 2),
        new("color2", 2),
        new("color3", 2),
    ];

    public static PyTuple CreateOwners(
        CharacterSelectionResponse? selection,
        IReadOnlySet<long> requestedIds)
    {
        var owners = new Dictionary<long, IReadOnlyList<PyValue>>();
        if (selection is not null)
        {
            foreach (CharacterSummary character in selection.Characters)
            {
                owners.TryAdd(
                    character.CorporationId,
                    [
                        new PyInteger(character.CorporationId),
                        new PyText(character.CorporationName),
                        new PyInteger(2),
                        PyNull.Instance,
                        PyNull.Instance,
                    ]);
                owners.TryAdd(
                    character.CharacterId,
                    [
                        new PyInteger(character.CharacterId),
                        new PyText(character.Name),
                        new PyInteger(character.CharacterTypeId),
                        new PyBoolean(false),
                        PyNull.Instance,
                    ]);
            }
        }

        var header = new PyList(
            new PyText("ownerID"),
            new PyText("ownerName"),
            new PyText("typeID"),
            new PyText("gender"),
            new PyText("ownerNameID"));
        var rows = new PyList(requestedIds
            .Where(owners.ContainsKey)
            .Select(id => (PyValue)new PyList(owners[id].ToArray()))
            .ToArray());
        return new PyTuple(header, rows);
    }

    public static PyTuple CreateCorporationTickers(
        CharacterSelectionResponse? selection,
        IReadOnlySet<long> requestedIds)
    {
        IReadOnlyList<PyValue>[] rows = selection?.Characters
            .Where(character => requestedIds.Contains(character.CorporationId))
            .DistinctBy(character => character.CorporationId)
            .Select(character => (IReadOnlyList<PyValue>)
            [
                new PyInteger(character.CorporationId),
                new PyText(CreateTicker(character.CorporationName)),
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
                PyNull.Instance,
            ])
            .ToArray() ?? [];
        return Build3396210PackedRowBuilder.CreateHeaderAndRows(
            CorporationTickerFields,
            rows);
    }

    private static string CreateTicker(string corporationName)
    {
        char[] initials = corporationName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]))
            .Take(5)
            .ToArray();
        return initials.Length == 0 ? "SSE" : new string(initials);
    }
}
