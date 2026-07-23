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

The Worker does not apply migrations. A login-enabled Worker requires
`ConnectionStrings__GameDatabase`, for example:

```bash
export ConnectionStrings__GameDatabase='Host=127.0.0.1;Port=5432;Database=space_spreadsheet_emulator;Username=sse;Password=local-development-only'
```

## Real PostgreSQL tests

Persistence, Worker, and topology tests create disposable PostgreSQL containers
through Testcontainers. Docker works with its normal daemon. For rootless Podman,
enable the Docker-compatible user socket and point Testcontainers at it:

```bash
systemctl --user enable --now podman.socket
export DOCKER_HOST="unix:///run/user/$(id -u)/podman/podman.sock"
```

The tests fail rather than silently substituting an in-memory provider when a
compatible container runtime is unavailable.
