# SpaceSpreadsheetEmulator

SpaceSpreadsheetEmulator is an original .NET implementation of an EVE-compatible
server architecture. The current foundation contains three explicit processes:

- **Gateway** owns client TCP connections and wire framing.
- **Coordinator** owns the cluster directory and exposes a versioned internal gRPC API.
- **Worker** hosts cancellation-aware, single-writer solar-system runtimes.

Milestones 0 through 2 provide validated identifiers, cluster contracts, health
endpoints, the build-3396210 protocol profile, a bounded binary codec, framing,
compression, MachoNet envelopes, handshake and AES-CBC transport modes, and a
conventional asynchronous TCP listener. The first vertical slice promotes an
immutable SDE artifact, authenticates a development account through Worker gRPC,
creates a deterministic starter character, and authors the build-specific packed
character-selection rowsets and dependent owner records from current Worker state.
Captured startup replay is limited to explicitly allowlisted auxiliary bootstrap
queries. A fresh graphical build-3396210 run accepts the authored selection and sends
`SelectCharacterID`; the first post-selection session transition is the Milestone 3
boundary. Gateway TCP and development enrollment remain disabled by default.

The current gameplay checkpoint also supports one or more configured solar-system
partitions under a Worker owner. Coordinator publishes the bootstrap assignments,
Gateway caches those routes and calls the owning Worker directly, and the Worker
fences undock/dock mutations by each assignment epoch. PostgreSQL now owns enrolled
account identities, starter characters, their active ship items, idempotent
dock/undock receipts, and versioned solar-system snapshots. Worker restores and
reconciles owned systems before readiness, checkpoints accepted mutations and every
ten seconds, and writes a final checkpoint during normal shutdown. Credential proofs
and login tickets remain process-local.

## Build and verify

```bash
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet test SpaceSpreadsheetEmulator.slnx --configuration Release --no-build \
  --collect:"XPlat Code Coverage" --settings coverage.runsettings \
  --results-directory artifacts/test-results
dotnet reportgenerator \
  -reports:"artifacts/test-results/**/coverage.cobertura.xml" \
  -targetdir:"artifacts/coverage" \
  -reporttypes:"Cobertura;HtmlSummary;TextSummary" \
  -filefilters:"+*/src/*;-*/Migrations/*;-*/*.Designer.cs;-*/obj/*"
eng/verify-coverage.sh artifacts/coverage/Cobertura.xml 0.80 0.70
dotnet run --project tools/SpaceSpreadsheetEmulator.Protocol.Tool -- fixtures verify
```

A login-enabled Worker requires a migrated PostgreSQL database. The development
Compose lifecycle and the rootless Podman setup for real-database tests are documented
in [infrastructure/README.md](infrastructure/README.md). Persistence, Worker, and
three-process topology integration tests use the pinned PostgreSQL Testcontainers
image and reset durable rows before every test; they fail rather than fall back to an
in-memory provider. CI retains TRX, merged Cobertura, and HTML coverage artifacts and
requires at least 80% line and 70% branch coverage of hand-written production source.

Private parser exports can be linked below the gitignored `_local/protocol-captures`
directory for an additional conditional compatibility test; see
`docs/compatibility/README.md`. Normal builds and CI skip that test when no local
corpus is present.

The `sse-data` tool promotes a verified build-specific JSONL archive into a
hash-addressed SQLite artifact. The archive, resulting database, local certificates,
and end-to-end evidence stay below gitignored `_local` paths. Worker rejects an
artifact unless its client, protocol, and SDE builds all equal `3396210`.

When explicitly enabled for local client testing, Gateway also owns a loopback-only
CONNECT proxy and HTTPS public-gateway compatibility endpoint. It creates or reuses
the expected development CA, XMPP leaf, and public-gateway leaf; no certificate or
private key is stored in Git.

The default operational capacity is 256 active connections per Gateway. It is a
soft, configuration-overridable limit; four default instances provide 1,024 slots.
No operating-system tuning is required.

See the [clean-room compatibility notes](docs/compatibility/README.md) and the ADRs
in `docs/architecture/decisions/`.
