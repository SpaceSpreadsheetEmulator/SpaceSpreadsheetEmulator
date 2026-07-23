namespace SpaceSpreadsheetEmulator.Protocol;

public readonly record struct DecodeResult<T>(T? Value, ProtocolError? Error)
    where T : class
{
    public bool IsSuccess => Error is null;

    public static DecodeResult<T> Success(T value) => new(value, null);

    public static DecodeResult<T> Failure(ProtocolError error) => new(null, error);
}
