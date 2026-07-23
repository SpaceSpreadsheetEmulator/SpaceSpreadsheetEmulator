namespace SpaceSpreadsheetEmulator.Protocol;

/// <summary>
/// Represents either a decoded protocol value or a structured, non-throwing decode error.
/// </summary>
public readonly record struct DecodeResult<T>(T? Value, ProtocolError? Error)
    where T : class
{
    public bool IsSuccess => Error is null;

    public static DecodeResult<T> Success(T value) => new(value, null);

    public static DecodeResult<T> Failure(ProtocolError error) => new(null, error);
}
