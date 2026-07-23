using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

public sealed class SolarSystemRuntimeRegistry : ISolarSystemRuntimeRegistry
{
    private readonly IReadOnlyDictionary<SolarSystemId, ISolarSystemRuntime> runtimes;

    public SolarSystemRuntimeRegistry(IEnumerable<ISolarSystemRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        ISolarSystemRuntime[] materialized = runtimes.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("At least one solar-system runtime is required.", nameof(runtimes));
        }

        Dictionary<SolarSystemId, ISolarSystemRuntime> bySystem = [];
        foreach (ISolarSystemRuntime runtime in materialized)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (!bySystem.TryAdd(runtime.Context.SolarSystemId, runtime))
            {
                throw new ArgumentException(
                    $"Solar system {runtime.Context.SolarSystemId} is configured more than once.",
                    nameof(runtimes));
            }
        }

        this.runtimes = bySystem;
        Runtimes = materialized;
    }

    public IReadOnlyCollection<ISolarSystemRuntime> Runtimes { get; }

    public bool TryGet(SolarSystemId solarSystemId, out ISolarSystemRuntime? runtime)
        => runtimes.TryGetValue(solarSystemId, out runtime);
}
