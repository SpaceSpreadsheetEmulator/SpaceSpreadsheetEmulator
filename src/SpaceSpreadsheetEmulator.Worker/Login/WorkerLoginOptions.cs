namespace SpaceSpreadsheetEmulator.Worker.Login;

public sealed class WorkerLoginOptions
{
    public bool Enabled { get; init; }

    public string ArtifactDirectory { get; init; } = string.Empty;

    public bool DevelopmentEnrollmentEnabled { get; init; }

    public int MaximumAccounts { get; init; } = 64;

    public int MaximumSessions { get; init; } = 256;

    public int SessionLifetimeMinutes { get; init; } = 15;
}
