using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.Compatibility;

/// <summary>
/// Creates build-3396210 client monikers without leaking wire objects into the backplane.
/// </summary>
internal static class Build3396210MonikerMapper
{
    public static PyObject CreateSkillHandler(long nodeId, long characterId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(characterId);
        return new PyObject(
            Bytes("carbon.common.script.net.moniker.Moniker"),
            new PyTuple(
                Bytes("skillMgr2"),
                new PyInteger(nodeId),
                new PyInteger(characterId),
                PyNull.Instance));
    }

    private static PyBuffer Bytes(string value)
        => new(Encoding.UTF8.GetBytes(value));
}
