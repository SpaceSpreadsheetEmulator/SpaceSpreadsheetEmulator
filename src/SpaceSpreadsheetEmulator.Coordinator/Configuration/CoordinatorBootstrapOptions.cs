namespace SpaceSpreadsheetEmulator.Coordinator.Configuration;

internal sealed class CoordinatorBootstrapOptions
{
    public bool Enabled { get; init; }

    public List<CoordinatorBootstrapAssignmentOptions> Assignments { get; init; } = [];

    public bool HasValidAssignments()
        => !Enabled
            || (Assignments.Count > 0
                && Assignments.All(assignment =>
                    assignment.SolarSystemId > 0
                    && !string.IsNullOrWhiteSpace(assignment.OwnerNodeId)
                    && assignment.Epoch > 0
                    && Uri.TryCreate(assignment.Endpoint, UriKind.Absolute, out _))
                && Assignments.Select(assignment => assignment.SolarSystemId).Distinct().Count()
                    == Assignments.Count);
}

internal sealed class CoordinatorBootstrapAssignmentOptions
{
    public int SolarSystemId { get; init; }

    public string OwnerNodeId { get; init; } = "worker-local";

    public ulong Epoch { get; init; }

    public string Endpoint { get; init; } = "http://127.0.0.1:5199";
}
