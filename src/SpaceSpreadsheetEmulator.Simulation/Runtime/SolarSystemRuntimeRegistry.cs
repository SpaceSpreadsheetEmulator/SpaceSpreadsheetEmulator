using SpaceSpreadsheetEmulator.Primitives.Identifiers;

namespace SpaceSpreadsheetEmulator.Simulation.Runtime;

/// <summary>
/// Indexes the distinct solar-system runtimes configured on a Worker.
/// </summary>
public sealed class SolarSystemRuntimeRegistry : ISolarSystemRuntimeRegistry
{
    private IReadOnlyDictionary<SolarSystemId, ISolarSystemRuntime> runtimes =
        new Dictionary<SolarSystemId, ISolarSystemRuntime>();
    private IReadOnlyCollection<ISolarSystemRuntime> runtimeCollection = [];
    private int initialized;

    public SolarSystemRuntimeRegistry()
    {
    }

    public SolarSystemRuntimeRegistry(IEnumerable<ISolarSystemRuntime> runtimes)
        => Initialize(runtimes);

    public IReadOnlyCollection<ISolarSystemRuntime> Runtimes => runtimeCollection;

    public void Initialize(IEnumerable<ISolarSystemRuntime> configuredRuntimes)
    {
        ArgumentNullException.ThrowIfNull(configuredRuntimes);
        ISolarSystemRuntime[] materialized = configuredRuntimes.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "At least one solar-system runtime is required.",
                nameof(configuredRuntimes));
        }

        if (Interlocked.CompareExchange(ref initialized, 1, 0) != 0)
        {
            throw new InvalidOperationException("The solar-system runtime registry is already initialized.");
        }

        Dictionary<SolarSystemId, ISolarSystemRuntime> bySystem = [];
        foreach (ISolarSystemRuntime runtime in materialized)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (!bySystem.TryAdd(runtime.Context.SolarSystemId, runtime))
            {
                throw new ArgumentException(
                    $"Solar system {runtime.Context.SolarSystemId} is configured more than once.",
                    nameof(configuredRuntimes));
            }
        }

        this.runtimes = bySystem;
        runtimeCollection = materialized;
    }

    public bool TryGet(SolarSystemId solarSystemId, out ISolarSystemRuntime? runtime)
        => runtimes.TryGetValue(solarSystemId, out runtime);
}
