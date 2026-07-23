using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Builds the neutral docked crimewatch state consumed during build-3396210 session changes.
/// </summary>
internal static class Build3396210CrimewatchMapper
{
    private const long MinimumSafetyLevel = 1;

    public static PyTuple CreateClientStates()
        => new(
            new PyTuple(
                Timer(100),
                Timer(200),
                Timer(400),
                Timer(300),
                Timer(500)),
            new PyDictionary(),
            new PyTuple(EmptySet(), EmptySet()),
            new PyInteger(MinimumSafetyLevel));

    public static PyObject CreateNpcCorporationAggressionSettings()
        => new(
            Bytes("crimewatch.corp_aggression.settings.AggressionSettings"),
            new PyDictionary(
                Entry("_enableAfter", PyNull.Instance),
                Entry("_disableAfter", new PyInteger(0))));

    private static PyTuple Timer(long state)
        => new(new PyInteger(state), PyNull.Instance);

    private static PyExtendedObject EmptySet()
        => new(
            1,
            new PyTuple(
                new PyToken("__builtin__.set"),
                new PyTuple(new PyList())));

    private static PyDictionaryEntry Entry(string key, PyValue value)
        => new(Bytes(key), value);

    private static PyBuffer Bytes(string value)
        => new(Encoding.UTF8.GetBytes(value));
}
