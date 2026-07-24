using System.Numerics;
using System.Text;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Builds the minimal starter-ship dogma state observed for build 3396210.
/// </summary>
internal static class Build3396210DogmaMapper
{
    public static PyObject CreateAllInfo(CharacterSummary character, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.ShipId <= 0 || character.SolarSystemId <= 0)
        {
            throw new ArgumentException("Dogma bootstrap requires an active ship.", nameof(character));
        }

        var timestamp = new PyBigInteger(new BigInteger(observedAt.UtcDateTime.ToFileTimeUtc()));
        var shipInfo = new PyDictionary(new PyDictionaryEntry(
            new PyInteger(character.ShipId),
            CreateItemInfo(
                character.ShipId,
                Build3396210InventoryMapper.CreateActiveShip(character),
                timestamp,
                CreateShipAttributes(character))));
        var characterInfo = new PyTuple(
            new PyDictionary(new PyDictionaryEntry(
                new PyInteger(character.CharacterId),
                CreateItemInfo(
                    character.CharacterId,
                    Build3396210InventoryMapper.CreateCharacter(character),
                    timestamp,
                    new PyDictionary()))),
            new PyTuple(
                new PyInteger(4),
                new PyList(),
                new PyList(),
                new PyList()));

        return KeyValue(
            Entry("systemWideEffectsOnShip", new PyDictionary()),
            Entry("shipModifiedCharAttribs", PyNull.Instance),
            Entry("structureInfo", new PyDictionary()),
            Entry("locationInfo", PyNull.Instance),
            Entry("shipInfo", shipInfo),
            Entry("activeShipID", new PyInteger(character.ShipId)),
            Entry("charInfo", characterInfo),
            Entry("shipState", CreateEmptyShipState()));
    }

    private static PyObject CreateItemInfo(
        long itemId,
        PyPackedRow item,
        PyBigInteger timestamp,
        PyDictionary attributes)
        => KeyValue(
            Entry("itemID", new PyInteger(itemId)),
            Entry("invItem", item),
            Entry("activeEffects", new PyDictionary()),
            Entry("time", timestamp),
            Entry("attributes", attributes),
            Entry("wallclockTime", timestamp));

    private static PyDictionary CreateShipAttributes(CharacterSummary character)
    {
        if (character.ShipDogmaAttributes.Any(attribute =>
                attribute.AttributeId <= 0 || !double.IsFinite(attribute.Value)))
        {
            throw new ArgumentException(
                "The selected ship contains invalid Dogma attributes.",
                nameof(character));
        }

        return new PyDictionary(character.ShipDogmaAttributes
            .OrderBy(attribute => attribute.AttributeId)
            .Select(attribute => new PyDictionaryEntry(
                new PyInteger(attribute.AttributeId),
                new PyFloat(attribute.Value)))
            .ToArray());
    }

    private static PyTuple CreateEmptyShipState()
        => new(
            new PyDictionary(),
            new PyDictionary(),
            new PyExtendedObject(
                1,
                new PyTuple(
                    new PyToken("collections.defaultdict"),
                    new PyTuple(new PyToken("__builtin__.set")))),
            new PyDictionary());

    private static PyObject KeyValue(params PyDictionaryEntry[] entries)
        => new(Bytes("utillib.KeyVal"), new PyDictionary(entries));

    private static PyDictionaryEntry Entry(string key, PyValue value)
        => new(Bytes(key), value);

    private static PyBuffer Bytes(string value)
        => new(Encoding.UTF8.GetBytes(value));
}
