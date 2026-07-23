using System.Text;
using SpaceSpreadsheetEmulator.Backplane.Contracts.V1;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Maps Worker-owned NPC-agent static data to the mutable rowset expected by build 3396210.
/// </summary>
internal static class Build3396210AgentMapper
{
    private static readonly string[] Fields =
    [
        "agentID",
        "agentTypeID",
        "divisionID",
        "level",
        "stationID",
        "bloodlineID",
        "corporationID",
        "gender",
        "isLocatorAgent",
    ];

    public static PyObject CreateAgentCatalog(NpcAgentCatalogResponse catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var rows = new PyList(catalog.Agents
            .Select(agent => (PyValue)new PyList(
                new PyInteger(agent.AgentId),
                new PyInteger(agent.AgentTypeId),
                new PyInteger(agent.DivisionId),
                new PyInteger(agent.Level),
                agent.HasStationId ? new PyInteger(agent.StationId) : PyNull.Instance,
                agent.HasBloodlineId ? new PyInteger(agent.BloodlineId) : PyNull.Instance,
                agent.HasCorporationId ? new PyInteger(agent.CorporationId) : PyNull.Instance,
                new PyInteger(agent.Gender ? 1 : 0),
                new PyInteger(agent.IsLocatorAgent ? 1 : 0)))
            .ToArray());
        return new PyObject(
            Bytes("eve.common.script.sys.rowset.Rowset"),
            new PyDictionary(
                Entry("header", new PyList(Fields.Select(field => (PyValue)Bytes(field)).ToArray())),
                Entry("RowClass", new PyToken("carbon.common.script.sys.row.Row")),
                Entry("lines", rows)));
    }

    private static PyDictionaryEntry Entry(string key, PyValue value)
        => new(Bytes(key), value);

    private static PyBuffer Bytes(string value)
        => new(Encoding.UTF8.GetBytes(value));
}
