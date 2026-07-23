using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tool;

/// <summary>
/// Maps protocol values to and from the tool's reviewable JSON representation.
/// </summary>
internal static class ValueJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(PyValue value) => ToNode(value).ToJsonString(Options);

    public static PyValue Deserialize(string json)
        => FromNode(JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("The value JSON must be an object."));

    public static JsonNode ToNode(PyValue value) => value switch
    {
        PyNull => Object("null"),
        PyBoolean item => Object("boolean", JsonValue.Create(item.Value)),
        PyInteger item => Object("integer", JsonValue.Create(item.Value)),
        PyBigInteger item => Object("bigInteger", JsonValue.Create(item.Value.ToString(CultureInfo.InvariantCulture))),
        PyFloat item => Object("float", JsonValue.Create(item.Value)),
        PyByte item => Object("byte", JsonValue.Create(item.Value)),
        PyText item => Object("text", JsonValue.Create(item.Value)),
        PyToken item => Object("token", JsonValue.Create(item.Value)),
        PyBuffer item => Object("buffer", JsonValue.Create(Convert.ToHexString(item.Value.AsSpan()).ToLowerInvariant())),
        PyStringTableReference item => new JsonObject
        {
            ["kind"] = "stringTableReference",
            ["index"] = item.Index,
            ["value"] = item.Value,
        },
        PySavedValueReference item => new JsonObject
        {
            ["kind"] = "savedValueReference",
            ["index"] = item.Index,
            ["value"] = ToNode(item.Value),
        },
        PyTuple item => Items("tuple", item.Items),
        PyList item => Items("list", item.Items),
        PyDictionary item => new JsonObject
        {
            ["kind"] = "dictionary",
            ["entries"] = new JsonArray(item.Entries.Select(entry => (JsonNode)new JsonObject
            {
                ["key"] = ToNode(entry.Key),
                ["value"] = ToNode(entry.Value),
            }).ToArray()),
        },
        PyObject item => ObjectValue(item.Type, item.State),
        PyExtendedObject item => new JsonObject
        {
            ["kind"] = "extendedObject",
            ["variant"] = item.Variant,
            ["header"] = ToNode(item.Header),
            ["list"] = new JsonArray(item.ListItems.Select(ToNode).ToArray()),
            ["entries"] = new JsonArray(item.DictionaryEntries.Select(entry => (JsonNode)new JsonObject
            {
                ["key"] = ToNode(entry.Key),
                ["value"] = ToNode(entry.Value),
            }).ToArray()),
        },
        PySubstructure item => new JsonObject { ["kind"] = "substructure", ["value"] = ToNode(item.Value) },
        PySubstream item => Bytes("substream", item.Data),
        PyChecksummedStream item => new JsonObject
        {
            ["kind"] = "checksummedStream",
            ["checksum"] = item.Checksum,
            ["value"] = ToNode(item.Value),
        },
        PyOpaquePickedData item => Bytes("opaquePickedData", item.Data),
        PyPackedRow item => new JsonObject
        {
            ["kind"] = "packedRow",
            ["header"] = ToNode(item.Header),
            ["columns"] = new JsonArray(item.Columns.Select(column => (JsonNode)new JsonObject
            {
                ["name"] = column.Name,
                ["encoding"] = column.Encoding,
            }).ToArray()),
            ["data"] = Convert.ToHexString(item.PackedData.AsSpan()).ToLowerInvariant(),
            ["variableValues"] = new JsonArray(item.VariableValues.Select(ToNode).ToArray()),
        },
        _ => throw new InvalidDataException($"Unsupported value {value.GetType().Name}."),
    };

    private static PyValue FromNode(JsonObject item)
    {
        string kind = item["kind"]?.GetValue<string>()
            ?? throw new InvalidDataException("A value kind is required.");
        return kind switch
        {
            "null" => PyNull.Instance,
            "boolean" => new PyBoolean(Required<bool>(item)),
            "integer" => new PyInteger(Required<long>(item)),
            "bigInteger" => new PyBigInteger(BigInteger.Parse(Required<string>(item), CultureInfo.InvariantCulture)),
            "float" => new PyFloat(Required<double>(item)),
            "byte" => new PyByte(Required<byte>(item)),
            "text" => new PyText(Required<string>(item)),
            "token" => new PyToken(Required<string>(item)),
            "buffer" => new PyBuffer(Convert.FromHexString(Required<string>(item))),
            "tuple" => new PyTuple(ReadItems(item)),
            "list" => new PyList(ReadItems(item)),
            "dictionary" => new PyDictionary(ReadEntries(item)),
            "stringTableReference" => new PyStringTableReference(
                RequiredProperty<int>(item, "index"),
                RequiredProperty<string>(item, "value")),
            "savedValueReference" => new PySavedValueReference(
                RequiredProperty<int>(item, "index"),
                FromNode(RequiredObject(item, "value"))),
            "object" => new PyObject(
                FromNode(RequiredObject(item, "type")),
                FromNode(RequiredObject(item, "state"))),
            "extendedObject" => new PyExtendedObject(
                RequiredProperty<byte>(item, "variant"),
                FromNode(RequiredObject(item, "header")),
                ReadOptionalItems(item, "list"),
                ReadOptionalEntries(item, "entries")),
            "substructure" => new PySubstructure(FromNode(RequiredObject(item, "value"))),
            "substream" => new PySubstream(ImmutableArray.Create(ReadData(item))),
            "checksummedStream" => new PyChecksummedStream(
                RequiredProperty<uint>(item, "checksum"),
                FromNode(RequiredObject(item, "value"))),
            "opaquePickedData" => new PyOpaquePickedData(ImmutableArray.Create(ReadData(item))),
            "packedRow" => ReadPackedRow(item),
            _ => throw new InvalidDataException($"Encoding JSON kind '{kind}' is not supported by the CLI."),
        };
    }

    private static T Required<T>(JsonObject item)
        => item["value"] is JsonNode value
            ? value.GetValue<T>()
            : throw new InvalidDataException("A value property is required.");

    private static PyValue[] ReadItems(JsonObject item)
        => item["items"]?.AsArray().Select(node => FromNode(node!.AsObject())).ToArray()
            ?? throw new InvalidDataException("An items array is required.");

    private static PyDictionaryEntry[] ReadEntries(JsonObject item)
        => item["entries"]?.AsArray().Select(node =>
        {
            JsonObject entry = node?.AsObject()
                ?? throw new InvalidDataException("A dictionary entry must be an object.");
            return new PyDictionaryEntry(
                FromNode(RequiredObject(entry, "key")),
                FromNode(RequiredObject(entry, "value")));
        }).ToArray() ?? throw new InvalidDataException("An entries array is required.");

    private static ImmutableArray<PyValue> ReadOptionalItems(JsonObject item, string property)
        => item[property]?.AsArray().Select(node => FromNode(node!.AsObject())).ToImmutableArray()
            ?? ImmutableArray<PyValue>.Empty;

    private static ImmutableArray<PyDictionaryEntry> ReadOptionalEntries(JsonObject item, string property)
        => item[property]?.AsArray().Select(node =>
        {
            JsonObject entry = node?.AsObject()
                ?? throw new InvalidDataException("A dictionary entry must be an object.");
            return new PyDictionaryEntry(
                FromNode(RequiredObject(entry, "key")),
                FromNode(RequiredObject(entry, "value")));
        }).ToImmutableArray() ?? ImmutableArray<PyDictionaryEntry>.Empty;

    private static PyPackedRow ReadPackedRow(JsonObject item)
    {
        ImmutableArray<PackedRowColumn> columns = item["columns"]?.AsArray().Select(node =>
        {
            JsonObject column = node?.AsObject()
                ?? throw new InvalidDataException("A packed-row column must be an object.");
            return new PackedRowColumn(
                RequiredProperty<string>(column, "name"),
                RequiredProperty<ushort>(column, "encoding"));
        }).ToImmutableArray() ?? throw new InvalidDataException("A columns array is required.");
        ImmutableArray<PyValue> values = item["variableValues"]?.AsArray()
            .Select(node => FromNode(node?.AsObject()
                ?? throw new InvalidDataException("A packed-row value must be an object.")))
            .ToImmutableArray() ?? throw new InvalidDataException("A variableValues array is required.");
        return new PyPackedRow(
            FromNode(RequiredObject(item, "header")),
            columns,
            ImmutableArray.Create(ReadData(item)),
            values);
    }

    private static JsonObject RequiredObject(JsonObject item, string property)
        => item[property]?.AsObject()
            ?? throw new InvalidDataException($"An object property '{property}' is required.");

    private static T RequiredProperty<T>(JsonObject item, string property)
        => item[property] is JsonNode value
            ? value.GetValue<T>()
            : throw new InvalidDataException($"A '{property}' property is required.");

    private static byte[] ReadData(JsonObject item)
        => Convert.FromHexString(RequiredProperty<string>(item, "data"));

    private static JsonObject Object(string kind, JsonNode? value = null)
    {
        var result = new JsonObject { ["kind"] = kind };
        if (value is not null)
        {
            result["value"] = value;
        }

        return result;
    }

    private static JsonObject Items(string kind, ImmutableArray<PyValue> items)
        => new()
        {
            ["kind"] = kind,
            ["items"] = new JsonArray(items.Select(ToNode).ToArray()),
        };

    private static JsonObject Bytes(string kind, ImmutableArray<byte> data)
        => new()
        {
            ["kind"] = kind,
            ["data"] = Convert.ToHexString(data.AsSpan()).ToLowerInvariant(),
        };

    private static JsonObject ObjectValue(PyValue type, PyValue state)
        => new()
        {
            ["kind"] = "object",
            ["type"] = ToNode(type),
            ["state"] = ToNode(state),
        };
}
