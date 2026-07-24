using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.MachoNet;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Connections;

internal sealed partial class GatewayClientConnection
{
    private readonly Dictionary<string, long> inventoryBindings = new(StringComparer.Ordinal);
    private string? inventoryBrokerBinding;

    private RpcDispatchResult ResolveInventoryBroker(MachoRpcRequest request)
        => HasSelectedLocationObject(request.Arguments)
            ? Result(new PyInteger(ProxyNodeId))
            : Result(PyNull.Instance);

    private RpcDispatchResult BindInventoryBroker(MachoRpcRequest request)
    {
        if (selectedCharacter is not { } character
            || request.Arguments.Items.Length != 2
            || !HasSelectedLocationObject(new PyTuple(request.Arguments.Items[0]))
            || Unwrap(request.Arguments.Items[1]) is not PyTuple { Items.Length: 3 } nested
            || Unwrap(nested.Items[1]) is not PyTuple { Items.Length: 2 } nestedArguments
            || !TryReadInitialInventory(
                ReadText(nested.Items[0]),
                nestedArguments,
                character,
                out long itemId))
        {
            return Result(PyNull.Instance);
        }

        inventoryBrokerBinding = CreateInventoryBinding(request.CallId, suffix: 0);
        string itemBinding = CreateInventoryBinding(request.CallId, suffix: 1);
        inventoryBindings[itemBinding] = itemId;
        return Result(new PyTuple(
            CreateLease(inventoryBrokerBinding),
            CreateLease(itemBinding)));
    }

    private static bool TryReadInitialInventory(
        string? method,
        PyTuple arguments,
        CharacterSummary character,
        out long itemId)
    {
        itemId = 0;
        if (string.Equals(method, "GetInventoryFromId", StringComparison.Ordinal)
            && TryInteger(arguments.Items[0], out itemId)
            && itemId == character.ShipId)
        {
            return true;
        }

        if (string.Equals(method, "GetInventory", StringComparison.Ordinal)
            && TryInteger(arguments.Items[0], out long inventoryFlag)
            && inventoryFlag == 10_004
            && Unwrap(arguments.Items[1]) is PyNull)
        {
            itemId = character.StationId;
            return true;
        }

        return false;
    }

    private RpcDispatchResult GetInventoryFromId(MachoRpcRequest request)
    {
        if (selectedCharacter is not { } character
            || request.BoundObject != inventoryBrokerBinding
            || request.Arguments.Items.Length != 2
            || !TryInteger(request.Arguments.Items[0], out long itemId)
            || (itemId != character.CharacterId
                && itemId != character.ShipId
                && (character.HasStationId
                    ? itemId != character.StationId
                    : itemId != character.SolarSystemId)))
        {
            return Result(PyNull.Instance);
        }

        string itemBinding = CreateInventoryBinding(request.CallId, suffix: 1);
        inventoryBindings[itemBinding] = itemId;
        return Result(CreateLease(itemBinding));
    }

    private RpcDispatchResult GetSelfInventoryItem(MachoRpcRequest request)
    {
        if (selectedCharacter is not { } character
            || request.Arguments.Items.Length != 0
            || request.BoundObject is null
            || !inventoryBindings.TryGetValue(request.BoundObject, out long itemId))
        {
            return Result(PyNull.Instance);
        }

        return itemId switch
        {
            var id when id == character.ShipId
                => Result(Build3396210InventoryMapper.CreateActiveShip(character)),
            var id when character.HasStationId && id == character.StationId
                => Result(Build3396210InventoryMapper.CreateStation(character)),
            _ => Result(PyNull.Instance),
        };
    }

    private RpcDispatchResult ListInventory(MachoRpcRequest request)
    {
        if (request.Arguments.Items.Length != 0
            || request.BoundObject is null
            || !inventoryBindings.TryGetValue(request.BoundObject, out long itemId)
            || !TryReadInventoryFlag(request.KeywordArguments, out long? flag))
        {
            return Result(PyNull.Instance);
        }

        if (selectedCharacter is { HasStationId: true } character
            && itemId == character.StationId
            && flag is null or 4)
        {
            var items = new List<PyValue>
            {
                Build3396210InventoryMapper.CreateActiveShip(character),
            };
            items.AddRange(character.InventoryItems
                .Where(item => item.Flag == CharacterInventoryItemFlag.StationHangar)
                .OrderBy(item => item.ItemId)
                .Select(Build3396210InventoryMapper.CreateInventoryItem));
            return Result(Build3396210InventoryMapper.CreateItemSet([.. items]));
        }

        if (selectedCharacter is not null
            && itemId == selectedCharacter.ShipId
            && flag is null or 5)
        {
            return Result(Build3396210InventoryMapper.CreateItemSet(
                selectedCharacter.InventoryItems
                    .Where(item => item.Flag == CharacterInventoryItemFlag.ShipCargo)
                    .OrderBy(item => item.ItemId)
                    .Select(Build3396210InventoryMapper.CreateInventoryItem)
                    .Cast<PyValue>()
                    .ToArray()));
        }

        return Result(Build3396210InventoryMapper.CreateEmptyItemSet());
    }

    private RpcDispatchResult GetAvailableTurretSlots(MachoRpcRequest request)
        => selectedCharacter is not null
            && request.Arguments.Items.Length == 0
            && request.BoundObject is not null
            && inventoryBindings.TryGetValue(request.BoundObject, out long itemId)
            && itemId == selectedCharacter.ShipId
                ? Result(new PyInteger(0))
                : Result(PyNull.Instance);

    private static bool TryReadInventoryFlag(
        PyDictionary keywordArguments,
        out long? flag)
    {
        flag = null;
        bool found = false;
        foreach (PyDictionaryEntry entry in keywordArguments.Entries)
        {
            if (!string.Equals(ReadText(entry.Key), "flag", StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            found = true;
            PyValue value = Unwrap(entry.Value);
            if (value is PyNull)
            {
                continue;
            }

            if (!TryInteger(value, out long numericFlag))
            {
                return false;
            }

            flag = numericFlag;
        }

        return true;
    }

    private bool HasSelectedLocationObject(PyTuple arguments)
        => selectedCharacter is { } character
            && arguments.Items.Length == 1
            && Unwrap(arguments.Items[0]) is PyTuple { Items.Length: 2 } inventoryObject
            && TryInteger(inventoryObject.Items[0], out long locationId)
            && TryInteger(inventoryObject.Items[1], out long groupId)
            && (character.HasStationId
                ? locationId == character.StationId && groupId == 15
                : locationId == character.SolarSystemId && groupId == 5);

    private static string CreateInventoryBinding(long callId, int suffix)
        => $"N={ProxyNodeId}:{checked((callId * 2) + suffix + 10_000)}";

    private PySubstructure CreateLease(string binding)
    {
        long expiresAt = timeProvider.GetUtcNow().AddMinutes(10).UtcDateTime.ToFileTimeUtc();
        byte[] encoded = BlueMarshalCodec.Encode(
            new PyTuple(
                new PyBuffer(Encoding.UTF8.GetBytes(binding)),
                new PyBigInteger(new BigInteger(expiresAt))),
            profile);
        return new PySubstructure(new PySubstream(ImmutableArray.Create(encoded)));
    }
}
