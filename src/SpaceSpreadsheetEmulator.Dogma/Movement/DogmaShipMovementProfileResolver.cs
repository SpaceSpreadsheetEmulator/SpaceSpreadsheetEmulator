using SpaceSpreadsheetEmulator.Dogma.Definitions;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Dogma.Movement;

/// <summary>
/// Resolves base ship movement values from the immutable Dogma definition catalog.
/// </summary>
public sealed class DogmaShipMovementProfileResolver(IDogmaDefinitionCatalog definitions)
    : IDogmaShipMovementProfileResolver
{
    private const string AgilityAttribute = "agility";
    private const string MassAttribute = "mass";
    private const string MaximumVelocityAttribute = "maxVelocity";

    public DogmaShipMovementProfile Resolve(int shipTypeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipTypeId);
        StaticTypeDefinition type = definitions.FindType(shipTypeId)
            ?? throw Missing(shipTypeId, "type definition");
        double mass = definitions.TryGetBaseAttribute(shipTypeId, MassAttribute, out double dogmaMass)
            ? dogmaMass
            : type.Mass ?? throw Missing(shipTypeId, "mass");
        double inertia = RequiredAttribute(shipTypeId, AgilityAttribute);
        double maximumVelocity = RequiredAttribute(shipTypeId, MaximumVelocityAttribute);

        try
        {
            return new DogmaShipMovementProfile(shipTypeId, mass, inertia, maximumVelocity);
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new InvalidDataException(
                $"Ship type {shipTypeId} has invalid base movement Dogma.",
                error);
        }
    }

    private double RequiredAttribute(int shipTypeId, string name)
        => definitions.TryGetBaseAttribute(shipTypeId, name, out double value)
            ? value
            : throw Missing(shipTypeId, $"Dogma attribute '{name}'");

    private static InvalidDataException Missing(int shipTypeId, string value)
        => new($"Ship type {shipTypeId} is missing required {value}.");
}
