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

The current gameplay checkpoint also supports a single configured solar-system
owner. Coordinator publishes the bootstrap assignment, Gateway caches that route and
calls the owning Worker directly, and the Worker fences undock/dock mutations by the
assignment epoch. The development account receives one deterministic starter
character and ship; durable character storage remains future work.

## Build and verify

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet run --project tools/SpaceSpreadsheetEmulator.Protocol.Tool -- fixtures verify
```

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
