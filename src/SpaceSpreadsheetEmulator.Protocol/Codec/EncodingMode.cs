namespace SpaceSpreadsheetEmulator.Protocol.Codec;

/// <summary>
/// Selects exact wire-form preservation or deterministic canonical protocol encoding.
/// </summary>
public enum EncodingMode
{
    PreserveWireForm,
    Canonical,
}
