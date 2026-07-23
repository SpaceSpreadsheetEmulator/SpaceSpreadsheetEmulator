using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Services;

public interface IIdentifierResolver
{
    ValueTask<string?> ResolveAsync(string fieldName, long identifier, CancellationToken cancellationToken = default);
}
