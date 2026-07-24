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
            ["Chat"] = [],
            ["Chat.Service"] = ["Backplane.Contracts", "Chat"],
            ["StaticData"] = ["Primitives"],
            ["Content"] = ["Primitives", "StaticData"],
            ["Dogma"] = ["Primitives", "StaticData"],
            ["Identity"] = ["Primitives"],
            ["Inventory"] = ["Primitives"],
            ["Gameplay"] = ["Content", "Identity", "Inventory", "Primitives", "StaticData"],
            ["Simulation"] = ["Dogma", "Primitives"],
            ["Cluster.Contracts"] = ["Primitives"],
            ["Cluster"] = ["Cluster.Contracts", "Primitives"],
            ["Persistence"] = ["Dogma", "Gameplay", "Identity", "Inventory", "Primitives", "Simulation"],
            ["Gateway"] = ["Backplane.Contracts", "Cluster.Contracts", "Primitives", "Protocol"],
            ["Coordinator"] = ["Cluster.Contracts", "Cluster"],
            ["Worker"] = ["Backplane.Contracts", "Content", "Dogma", "Gameplay", "Identity", "Persistence", "Primitives", "Simulation", "StaticData"],
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
        string[] hosts = ["Chat.Service", "Gateway", "Coordinator", "Worker"];
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
        Assert.Contains("repeated SolarSystemObjectState static_objects = 2;", contract, StringComparison.Ordinal);
        Assert.Contains("SOLAR_SYSTEM_OBJECT_KIND_STATION", contract, StringComparison.Ordinal);
        Assert.Contains("SOLAR_SYSTEM_OBJECT_KIND_PLANET", contract, StringComparison.Ordinal);
        Assert.Contains("SOLAR_SYSTEM_OBJECT_KIND_JUMP_GATE", contract, StringComparison.Ordinal);
        Assert.Contains("string character_name = 8;", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc SetVelocity ", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc GetShipState ", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc SetShipPosition ", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("rpc SetDockedState ", contract, StringComparison.Ordinal);

        string loginContract = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SpaceSpreadsheetEmulator.Backplane.Contracts",
            "Protos",
            "login_gameplay.proto"));
        Assert.Contains(
            "repeated CharacterInventoryItem inventory_items = 31;",
            loginContract,
            StringComparison.Ordinal);
        Assert.Contains(
            "CHARACTER_INVENTORY_ITEM_FLAG_STATION_HANGAR",
            loginContract,
            StringComparison.Ordinal);
        Assert.Contains(
            "CHARACTER_INVENTORY_ITEM_FLAG_SHIP_CARGO",
            loginContract,
            StringComparison.Ordinal);

        string chatContract = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SpaceSpreadsheetEmulator.Backplane.Contracts",
            "Protos",
            "local_chat.proto"));
        Assert.Contains("service LocalChat", chatContract, StringComparison.Ordinal);
        Assert.Contains("rpc SendMessage ", chatContract, StringComparison.Ordinal);
        Assert.Contains(
            "rpc Subscribe (LocalChatSubscriptionRequest) returns (stream LocalChatEventEnvelope);",
            chatContract,
            StringComparison.Ordinal);
        Assert.Contains("LocalChatSnapshot snapshot = 3;", chatContract, StringComparison.Ordinal);
        Assert.Contains("LocalChatMember member_joined = 4;", chatContract, StringComparison.Ordinal);
        Assert.Contains("LocalChatMember member_left = 5;", chatContract, StringComparison.Ordinal);
        Assert.Contains("LocalChatMessage message_posted = 6;", chatContract, StringComparison.Ordinal);
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
