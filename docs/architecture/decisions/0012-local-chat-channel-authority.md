# ADR 0012: Local-chat channel authority

Status: accepted

## Context

Named ships in the solar stream provide nearby in-space presence, but they are not a
chat channel. Local chat also needs an authoritative player list, message ordering,
idempotent retries, and fan-out that remains independent of solar-system simulation
ticks and Gateway wire mapping.

The target architecture gives chat its own ownership key and process. Putting channel
state in Gateway would make one client transport authoritative; putting it in Worker
would couple message load and membership lifecycle to solar simulation.

## Decision

`SpaceSpreadsheetEmulator.Chat` owns the first protocol-neutral local-channel state
machine. `SpaceSpreadsheetEmulator.Chat.Service` is a separate ASP.NET Core gRPC
host whose composition root contains only transport, DI, and health wiring.

One in-memory channel is keyed by solar-system ID. A member is keyed by Gateway and
Gateway-session identity and carries the durable character ID and name supplied by
the trusted internal caller. Join and leave are idempotent. A subscription receives
one sequence-stamped player snapshot followed by exactly ordered member-joined,
member-left, and message-posted events.

Messages have caller-supplied idempotency IDs. An identical retry returns the
original result without a second delivery; conflicting reuse is rejected. Each
channel retains a bounded 1,024-message idempotency window. Subscriber queues are
bounded and fail closed with an explicit gap when they cannot preserve ordering.
Channels are isolated by solar-system ID.

This first slice keeps membership transient. Gateway and Worker integration must
re-register online presence after Chat service recovery. Shard placement and epochs,
internal mTLS, durable custom-channel configuration, moderation, message history,
and cross-service presence reconciliation remain later work. The graphical
build-3396210 XMPP adapter is also deferred; automated tests exercise the neutral
gRPC contract and real host.

## Consequences

- Local chat now has a real authoritative player list and ordered message stream
  rather than being inferred from ship state.
- Chat can scale and recover independently of Gateway and solar simulation.
- Slow consumers cannot silently continue from an incomplete member or message
  sequence.
- Production topology wiring must add trusted presence registration and graphical
  XMPP delivery before the legacy client can display or send local chat.
