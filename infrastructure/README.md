# Local infrastructure

The local topology uses one PostgreSQL 18 container and one named volume. The
container binds only to loopback. Copy `.env.example` to `.env` to override the
development-only database settings; `.env` is ignored by Git.

```bash
./infrastructure/database.sh up
./infrastructure/database.sh migrate
```

`down` stops the container while retaining its database. `reset` removes only the
named volume owned by the fixed `space-spreadsheet-emulator-development` Compose
project, starts a fresh database, and applies every migration:

```bash
./infrastructure/database.sh reset
```

The Worker does not apply migrations. The local connection string is stored in the
Worker and Persistence `appsettings.CliTest.json` profiles. The database helper
selects the Persistence `CliTest` profile when it invokes Entity Framework tools.
If the Compose `.env` changes the database port or credentials, place matching
connection strings in the ignored files:

- `src/SpaceSpreadsheetEmulator.Worker/appsettings.CliTest.local.json`
- `src/SpaceSpreadsheetEmulator.Persistence/appsettings.CliTest.local.json`

Do not pass application connection strings as inline environment variables.

## Real PostgreSQL tests

Persistence, Worker, and topology tests create disposable PostgreSQL containers
through Testcontainers. Docker works with its normal daemon. For rootless Podman,
enable the Docker-compatible user socket:

```bash
systemctl --user enable --now podman.socket
```

Then put the endpoint in an ignored `appsettings.IntegrationTest.local.json` beside
each integration-test project that you run:

```json
{
  "Testcontainers": {
    "DockerEndpoint": "unix:///run/user/<uid>/podman/podman.sock"
  }
}
```

The tests fail rather than silently substituting an in-memory provider when a
compatible container runtime is unavailable.
