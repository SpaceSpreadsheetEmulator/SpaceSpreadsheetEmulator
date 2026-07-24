namespace SpaceSpreadsheetEmulator.StaticData;

/// <summary>
/// Describes one immutable Dogma effect and the attributes that parameterize it.
/// </summary>
public sealed record DogmaEffectDefinition(
    int EffectId,
    string Name,
    string Guid,
    int CategoryId,
    bool Published,
    bool IsOffensive,
    bool IsAssistance,
    bool IsWarpSafe,
    bool DisallowAutoRepeat,
    int? DurationAttributeId,
    int? DischargeAttributeId,
    int? RangeAttributeId,
    int? FalloffAttributeId,
    int? TrackingSpeedAttributeId);
