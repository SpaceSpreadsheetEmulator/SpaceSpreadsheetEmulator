# ADR 0006: Intent-oriented Worker commands and streamed simulation output

Status: accepted

## Context

Gateway translates a legacy, stateful EVE protocol while Worker owns authoritative
gameplay and solar-system simulation. A mechanically convenient gRPC surface can
blur that boundary if it exposes remote setters and fine-grained state getters:

```text
SetShipPosition
SetVelocity
SetDockedState
GetPosition
GetVelocity
GetCapacitor
GetNearbyEntities
```

Those operations let Gateway assemble and mutate game state one field at a time.
Gateway then quietly becomes the game server while Worker degenerates into a remote
state store. Polling several getters per client update also turns one logical
simulation observation into many gRPC calls and creates inconsistent partial views.

## Decision

Worker's gameplay API is command-oriented. Commands express authenticated player
intent, not desired authoritative state:

```text
RequestUndock
RequestDock
SetMovementIntent
ActivateModule
WarpTo
```

Gateway owns wire-protocol interpretation and choreography. It may reject malformed,
unauthenticated, out-of-sequence, or structurally invalid client traffic. It maps an
accepted wire request into a protocol-neutral intent with the gateway session,
correlation or idempotency key, target partition, and expected ownership epoch.

Worker owns gameplay authorization and legality. It resolves current authoritative
state, decides whether the intent is legal, executes accepted commands through the
solar system's bounded single-writer mailbox, commits required durable changes, and
produces a result plus ordered domain or simulation events. Gateway must not predict
or reproduce those rules.

Unary gRPC remains appropriate for commands and coarse request/response queries such
as character selection or an explicitly requested catalog. It is not used to poll
the live simulation one property or entity at a time.

Gateway subscribes to the relevant session/system output and Worker pushes a
protocol-neutral initial view followed by ordered deltas:

```text
Gateway -> Worker: SubscribeSession(systemId, characterId, sessionContext, epoch)

Worker -> Gateway:
    SessionSnapshot
    EntityEntered
    EntityMoved
    ShipStateChanged
    EntityLeft
```

The first implementation may use one cancellable server stream per subscribed
session. If measurements show excessive stream count or subscription control needs
acknowledgements, resume cursors, or multiplexing, replace it with one bidirectional
Gateway-to-Worker stream that carries multiple session subscriptions. Do not use a
unary callback RPC per notification.

Every streamed envelope carries enough neutral routing and fencing information for
Gateway to deliver safely: gateway and gateway-session identity, solar-system ID,
producing Worker identity, ownership epoch, and a monotonically ordered sequence or
cursor. Gateway drops stale-epoch output. The current implementation requires a
snapshot first and then an exactly contiguous sequence; a route, epoch, session, or
sequence mismatch fails the client connection closed. Streams and their buffers are
bounded, cancellation-aware, observable, and fail closed on gaps that cannot be recovered.
The protocol adapter maps snapshots/events into the ordering and packet shapes
required by the selected client build.

The stream is an in-space presentation subscription, not the authenticated client
session and not Worker ownership of the character. Gateway starts it only after the
solar-system bind response has entered the ordered outbound queue, so the client
cannot receive simulation state before it has a ballpark binding. Docking cancels
that in-space stream and releases the solar-system bound object. The TCP login
session remains open, and the docked character and ship remain authoritative state
owned by the same Worker solar-system partition.

Internal runtime methods may query or replace state for deterministic tests,
snapshot restore, reconciliation, and administration. They are not automatically
part of the Gateway-facing Worker API. Production diagnostics must be explicitly
authorized and must not become the ordinary player-session data path.

## Gateway protocol adapter organization

Protocol mapping remains owned by Gateway. Gameplay legality remains in Gameplay,
Inventory, Dogma, Simulation, and their Worker-hosted application services.

For the current vertical slice, cohesive mapping, session, and dispatch types may
remain in the Gateway project. If build-specific mapping grows enough to obscure the
composition root or requires an independently testable/reusable boundary, extract a
precisely named library such as `SpaceSpreadsheetEmulator.Gateway.Compatibility`.
That library may reference Protocol and protocol-neutral contracts. It must not own
authoritative gameplay state or decide whether an intent is legal. Do not create a
generic rules, common, helpers, or service-registry assembly.

## Consequences

- Worker commands are named for player intent and can reject requests without
  exposing mutable fields as an API.
- Gateway retains the substantial responsibility of understanding EVE framing,
  MachoNet, bound objects, session choreography, and build-specific mappings without
  becoming authoritative for gameplay.
- Simulation output is consistent and naturally ordered rather than assembled from
  several potentially divergent getter calls.
- Stream lifecycle, snapshot/delta consistency, backpressure, fencing, reconnection,
  and gap handling become explicit design and test obligations.
- The early-stage migration removed `SetVelocity` and `GetShipState` from the
  Gateway-facing contract. `backplane.v2` now exposes intent commands and
  `SubscribeSession`; runtime inspection remains outside the gRPC surface.
- Gateway now maps build-3396210 solar resolve/bind, initial `SetState`, movement
  deltas, and dock teardown without exposing `PyValue`, MachoNet packets, Destiny
  bytes, or bound-object identifiers to Worker.
- One connection-level sequencer atomically queues response/notification batches
  from RPC and stream producers. Undock location change precedes the ship bind
  response; dock response precedes acceptance, final simulation cleanup, object
  release, station location change, and docking completion.
