using SpaceSpreadsheetEmulator.Dogma.Definitions;
using SpaceSpreadsheetEmulator.Dogma.Movement;
using SpaceSpreadsheetEmulator.StaticData;

namespace SpaceSpreadsheetEmulator.Dogma.Tests.Definitions;

public sealed class DogmaDefinitionCatalogTests
{
    [Fact]
    public async Task LoadedCatalogQueriesTypesAttributesEffectsAndBaseValues()
    {
        await using var source = new TestStaticDataStore();

        DogmaDefinitionCatalog catalog = await DogmaDefinitionCatalog.LoadAsync(source, [601]);

        Assert.Equal("Ibis", catalog.FindType(601)!.Name);
        Assert.Equal(37, catalog.FindAttribute("maxVelocity")!.AttributeId);
        Assert.Equal("maxVelocity", catalog.FindAttribute(37)!.Name);
        Assert.Equal(5000, catalog.FindEffect("shipBonus")!.EffectId);
        Assert.Equal("shipBonus", catalog.FindEffect(5000)!.Name);
        Assert.True(catalog.TryGetBaseAttribute(601, 37, out double byId));
        Assert.True(catalog.TryGetBaseAttribute(601, "maxVelocity", out double byName));
        Assert.Equal(295, byId);
        Assert.Equal(byId, byName);
        Assert.False(catalog.TryGetBaseAttribute(601, "missing", out _));
    }

    [Fact]
    public async Task MissingReferencedDefinitionIsRejectedDuringLoad()
    {
        await using var source = new TestStaticDataStore
        {
            TypeDogma = new TypeDogmaDefinition(
                601,
                [new TypeDogmaAttributeValue(999, 1)],
                []),
        };

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => DogmaDefinitionCatalog.LoadAsync(source, [601]));

        Assert.Contains("601", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateSemanticNameRequiresIdBasedLookup()
    {
        await using var source = new TestStaticDataStore
        {
            AttributeDefinitions =
            [
                Attribute(37, "duplicate"),
                Attribute(38, "duplicate"),
                Attribute(70, "agility"),
            ],
        };

        DogmaDefinitionCatalog catalog = await DogmaDefinitionCatalog.LoadAsync(source, [601]);

        Assert.Null(catalog.FindAttribute("duplicate"));
        Assert.Equal(37, catalog.FindAttribute(37)!.AttributeId);
    }

    [Fact]
    public async Task ShipMovementProfileUsesBuildPinnedMassInertiaAndMaximumVelocity()
    {
        await using var source = new TestStaticDataStore();
        DogmaDefinitionCatalog catalog = await DogmaDefinitionCatalog.LoadAsync(source, [601]);
        var resolver = new DogmaShipMovementProfileResolver(catalog);

        DogmaShipMovementProfile profile = resolver.Resolve(601);

        Assert.Equal(601, profile.ShipTypeId);
        Assert.Equal(1_163_000, profile.Mass);
        Assert.Equal(4.5, profile.InertiaModifier);
        Assert.Equal(295, profile.MaximumVelocity);
        Assert.Equal(5.2335, profile.ResponseTimeSeconds, precision: 4);
    }

    [Fact]
    public async Task ShipMovementProfileRejectsMissingRequiredDogma()
    {
        await using var source = new TestStaticDataStore
        {
            TypeDogma = new TypeDogmaDefinition(
                601,
                [new TypeDogmaAttributeValue(37, 295)],
                []),
        };
        DogmaDefinitionCatalog catalog = await DogmaDefinitionCatalog.LoadAsync(source, [601]);
        var resolver = new DogmaShipMovementProfileResolver(catalog);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => resolver.Resolve(601));

        Assert.Contains("agility", error.Message, StringComparison.Ordinal);
    }

    private sealed class TestStaticDataStore : IStaticDataStore
    {
        public TypeDogmaDefinition TypeDogma { get; init; } = new(
            601,
            [
                new TypeDogmaAttributeValue(37, 295),
                new TypeDogmaAttributeValue(70, 4.5),
            ],
            [new TypeDogmaEffectReference(5000, false)]);

        public IReadOnlyList<DogmaAttributeDefinition> AttributeDefinitions { get; init; } =
            [
                Attribute(37, "maxVelocity"),
                Attribute(70, "agility"),
            ];

        public CompatibilityManifest Compatibility { get; } = new(
            3_396_210,
            3_396_210,
            3_396_210,
            "test",
            new string('a', 64),
            "test",
            5,
            new string('b', 64),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        public ValueTask<StaticTypeDefinition?> FindTypeAsync(
            long typeId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<StaticTypeDefinition?>(typeId == 601
                ? new StaticTypeDefinition(
                    601,
                    25,
                    "Ibis",
                    "Test ship",
                    true,
                    1_163_000,
                    47,
                    15_000,
                    125,
                    1,
                    1,
                    500_001,
                    1_817)
                : null);

        public ValueTask<IReadOnlyList<DogmaAttributeDefinition>> ListDogmaAttributesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(AttributeDefinitions);

        public ValueTask<IReadOnlyList<DogmaEffectDefinition>> ListDogmaEffectsAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<DogmaEffectDefinition>>(
            [
                new DogmaEffectDefinition(
                    5000,
                    "shipBonus",
                    "effects.shipBonus",
                    0,
                    false,
                    false,
                    false,
                    true,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null),
            ]);

        public ValueTask<TypeDogmaDefinition?> FindTypeDogmaAsync(
            long typeId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TypeDogmaDefinition?>(typeId == 601 ? TypeDogma : null);

        public ValueTask<StaticDataRecord?> FindAsync(
            StaticDataEntityKind kind,
            long id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<StaticDataRecord?>(null);

        public ValueTask<IReadOnlyList<StaticDataRecord>> ListAsync(
            StaticDataEntityKind kind,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<StaticDataRecord>>([]);

        public ValueTask<IReadOnlyList<StaticNpcAgent>> ListNpcAgentsAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<StaticNpcAgent>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static DogmaAttributeDefinition Attribute(int id, string name)
        => new(
            id,
            name,
            "Attribute",
            "Test attribute",
            17,
            4,
            0,
            true,
            false,
            true,
            false,
            11,
            null);
}
