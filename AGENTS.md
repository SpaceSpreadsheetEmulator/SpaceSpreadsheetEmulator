# SpaceSpreadsheetEmulator agent guidance

## Scope and priorities

This file applies to the entire repository. Prefer correctness, clear ownership,
and a small understandable change over speculative framework or feature growth.

- Keep files and types cohesive and reasonably short. Split a file when it owns
  multiple responsibilities or becomes difficult to review; do not create god
  classes, giant service registries, or catch-all `Common`, `Helpers`, or `Utils`.
- Favor modular, human-readable, maintainable, testable code with explicit
  dependencies. Use constructor injection, narrow interfaces at real boundaries,
  validated options, structured logging, and `TimeProvider`/other injected sources
  of nondeterminism.
- Use `internal` by default. Keep public cross-project APIs deliberately small.
- Keep hot paths bounded and cancellation-aware. Prefer explicit mappings and
  state machines over reflection, magic discovery, service locators, or `dynamic`.
- Add focused unit tests for rules and integration tests for transport, DI, storage,
  and host wiring. Preserve architecture tests that enforce dependency direction.
- Avoid unrelated refactors, but when touching an oversized file, extract one
  coherent responsibility when that improves the requested change. The current
  `GatewayClientConnection` is the clearest decomposition candidate.

## Temporary foundation rule

Until the project has a stable playable base, follow
`docs/architecture/SPACE_SPREADSHEET_EMULATOR_PLAN.md`. It is a working local plan,
not a repository artifact: never stage or commit it. Delete the plan and this
temporary section once the base layout and core boundaries are stable.

The current baseline contains Milestones 0 and 1 plus the first Milestone 2 login
vertical-slice implementation and tests. Do not skip ahead merely to create empty
projects. Treat Milestone 2 as incomplete until its real-client checkpoint,
loopback path, and build/profile/static-data compatibility gate are demonstrated.

Durable plan rules worth keeping even after the temporary plan is removed:

- Gateway, Coordinator, and Worker are separate processes, including locally, and
  communicate across real versioned gRPC boundaries.
- Gateway owns TCP, handshake/session state, wire translation, routing caches, and
  ordered outbound delivery. Wire types do not escape it.
- Coordinator is the low-volume control plane for membership, placement, leases,
  epochs, fencing, drain, and migration. It is not a gameplay request hop.
- Worker is the gameplay data plane. Each solar system is a bounded single-writer
  partition; every authoritative mutation and notification is fenced by its epoch.
- PostgreSQL is authoritative for mutable durable state. Static data is immutable,
  build-pinned, explicitly promoted, and locally cached. Use idempotency and
  outbox/inbox patterns instead of distributed transactions.
- Market and chat have region/channel ownership rather than solar-system ownership.
  Split them into services only when their vertical slices and invariants exist.
- Protocol, domain, persistence, and hosting concerns remain separate. Domain
  projects do not reference ASP.NET Core, gRPC, persistence adapters, or wire types.

## Research and privacy boundaries

The private local authorization documents control the research scope. In practice:

- Use only lawfully controlled accounts, devices, credentials, and data.
- For disassembly, instrumentation or injection, and traffic capture, follow the private local authorization documents without exposing their contents.
- Do not disrupt live services, bypass licensing, access other users' data, or
  publish undisclosed vulnerabilities.
- Build an independent implementation from observed behavior. Do not copy or
  distribute proprietary code, binaries, resources, captures, or decoded dumps.
- Keep private research material, secrets, keys, credentials, and session data out
  of Git, artifacts, tests, logs, issues, and chat. Use cleared minimal fixtures.
- Do not reveal author or contributor PII. Never publish personal usernames, home
  directory names, personal email addresses, signatures, account/device identifiers,
  or identifying document metadata. Use `~/` and generic placeholders, and scan
  staged changes and rewritten history before every push.

## Local research and reference material

These paths are local reference inputs, never project dependencies or distributable
artifacts:

- Decrypted/decoded packet captures:
  `~/dockers/eve-1/eve-client-capture/_local/captures`
- Capture decoder, query tools, and their documentation:
  `~/dockers/eve-1/eve-client-capture`
- Semi-working EvEJS reference:
  `~/dockers/eve-1/evejs`
- Old C# EVESharp reference:
  `~/dockers/eve-1/EVESharp`

Use captures and the owned client to establish observed wire behavior. Use EvEJS or
EVESharp only occasionally to locate a likely service name, packet shape, or question
to verify. They are old, buggy, incomplete, and never a source of truth. Do not copy
or translate their code, algorithms, naming, architecture, dependency graph, or
fixtures into this repository. Write a short independent behavior specification,
implement it with original code, and validate it against observations and tests.

For static data, prefer the matching official SDE build. For protocol behavior,
prefer current build-specific observations and independently authored compatibility
specifications. Record uncertainty rather than filling gaps from a reference server.

## Working and verification habits

- Inspect the working tree before editing and preserve unrelated user changes.
- Keep private/generated inputs below ignored `_local/` paths. Production code must
  not scan a client installation or depend on the capture/reference repositories.
- Pin compatibility by client build, protocol profile, SDE build, and artifact hash;
  never silently substitute a current `latest` dataset for an older client.
- Keep `Program.cs` as a readable composition root. Extract cohesive registration
  methods when unrelated setup accumulates; keep business rules out of the host.
- Keep `.proto` contracts protocol-neutral, versioned, and backward-conscious.
- Before handing off code, run `dotnet test SpaceSpreadsheetEmulator.slnx` and
  `git diff --check`, plus narrower tests appropriate to the change.
- Do not commit or push unless requested. When committing, verify the staged file
  list contains no plan, capture, credential, certificate, key, or research corpus.
- When pushing, use the repository's configured Git remote with `git push`.

# IMPORTAINT
Please refer to possible additional instructions under the _local/ folder.