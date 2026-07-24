using System.Xml.Linq;

namespace SpaceSpreadsheetEmulator.Architecture.Tests;

public class DependencyRules
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Primitives"] = [],
            ["Protocol"] = [],
            ["Backplane.Contracts"] = ["Primitives"],
            ["StaticData"] = ["Primitives"],
            ["Content"] = ["Primitives", "StaticData"],
            ["Identity"] = ["Primitives"],
            ["Inventory"] = ["Primitives"],
            ["Gameplay"] = ["Content", "Identity", "Primitives", "StaticData"],
            ["Simulation"] = ["Primitives"],
            ["Cluster.Contracts"] = ["Primitives"],
            ["Cluster"] = ["Cluster.Contracts", "Primitives"],
            ["Persistence"] = ["Gameplay", "Identity", "Inventory", "Primitives", "Simulation"],
            ["Gateway"] = ["Backplane.Contracts", "Cluster.Contracts", "Primitives", "Protocol"],
            ["Coordinator"] = ["Cluster.Contracts", "Cluster"],
            ["Worker"] = ["Backplane.Contracts", "Content", "Gameplay", "Identity", "Persistence", "Primitives", "Simulation", "StaticData"],
        };

    [Fact]
    public void DependencyFreeProjectsDoNotReferenceOtherProjects()
    {
        string[] dependencyFreeProjects = ["Protocol", "Primitives"];
        string repositoryRoot = FindRepositoryRoot();

        foreach (string projectName in dependencyFreeProjects)
        {
            string projectFile = Path.Combine(
                repositoryRoot,
                "src",
                $"SpaceSpreadsheetEmulator.{projectName}",
                $"SpaceSpreadsheetEmulator.{projectName}.csproj");

            XDocument project = XDocument.Load(projectFile);

            Assert.Empty(project.Descendants("ProjectReference"));
        }
    }

    [Fact]
    public void EveryProductionProjectUsesOnlyItsAllowedProjectReferences()
    {
        string repositoryRoot = FindRepositoryRoot();
        foreach ((string projectName, string[] expected) in AllowedReferences)
        {
            string projectFile = Path.Combine(
                repositoryRoot,
                "src",
                $"SpaceSpreadsheetEmulator.{projectName}",
                $"SpaceSpreadsheetEmulator.{projectName}.csproj");
            XDocument project = XDocument.Load(projectFile);
            string[] actual = project.Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value))
                .Select(name => name["SpaceSpreadsheetEmulator.".Length..])
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
        }
    }

    [Fact]
    public void LibrariesNeverReferenceExecutableHosts()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] hosts = ["Gateway", "Coordinator", "Worker"];
        foreach (string projectFile in Directory.GetFiles(
                     Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(projectFile);
            if (hosts.Any(host => name.EndsWith(host, StringComparison.Ordinal)))
            {
                continue;
            }

            string projectText = File.ReadAllText(projectFile);
            Assert.DoesNotContain(hosts, host => projectText.Contains(
                $"SpaceSpreadsheetEmulator.{host}.csproj", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ProjectFilesDoNotReferenceExternalEveImplementations()
    {
        string repositoryRoot = FindRepositoryRoot();
        foreach (string projectFile in Directory.GetFiles(
                     repositoryRoot, "*.csproj", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(projectFile);
            Assert.DoesNotContain("dockers/eve-1", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EVESharp", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EvEJS", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SolarSystemBackplaneUsesIntentCommandsAndStreamedOutput()
    {
        string repositoryRoot = FindRepositoryRoot();
        string contract = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SpaceSpreadsheetEmulator.Backplane.Contracts",
            "Protos",
            "solar_system_gameplay.proto"));

        Assert.Contains("package backplane.v2;", contract, StringComparison.Ordinal);
        Assert.Contains("rpc RequestUndock ", contract, StringComparison.Ordinal);
        Assert.Contains("rpc RequestDock ", contract, StringComparison.Ordinal);
        Assert.Contains("rpc SetMovementIntent ", contract, StringComparison.Ordinal);
        Assert.Contains("MOVEMENT_INTENT_STOP", contract, StringComparison.Ordinal);
        Assert.Contains("MOVEMENT_INTENT_FOLLOW", contract, StringComparison.Ordinal);
        Assert.Contains("MOVEMENT_INTENT_ORBIT", contract, StringComparison.Ordinal);
        Assert.Contains("MOVEMENT_INTENT_GO_TO_POINT", contract, StringComparison.Ordinal);
        Assert.Contains(
            "rpc SubscribeSession (SessionSubscriptionRequest) returns (stream SessionEventEnvelope);",
            contract,
            StringComparison.Ordinal);
        Assert.DoesNotContain("rpc SetVelocity ", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc GetShipState ", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc SetShipPosition ", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc SetDockedState ", contract, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root from the test output directory.");
    }
}
