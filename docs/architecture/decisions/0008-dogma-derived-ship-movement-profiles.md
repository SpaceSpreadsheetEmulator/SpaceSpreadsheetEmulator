# ADR 0008: Dogma-derived ship movement profiles

Status: accepted

## Context

The first deterministic movement slice used one Worker-wide maneuver speed. That
made mailbox ordering and streamed updates testable, but it gave every ship the same
movement envelope and allowed a directional command to request an arbitrary speed.

Observed client behavior treats mass, inertia, and maximum velocity as authoritative
ship attributes. The base response time is proportional to `mass * inertia`, while
the exact fitted ship can later modify all three values through Dogma effects. A
simulation tick must not query the static-data database, and a recovered runtime
must continue with the same build-pinned values that governed its checkpoint.

## Decision

Worker preloads the starter ship type into `IDogmaDefinitionCatalog` when solar
simulation is enabled. `IDogmaShipMovementProfileResolver` then requires:

- positive mass from the type definition, or from an explicit `mass` Dogma value;
- the unambiguous `agility` base attribute as the inertia modifier;
- the unambiguous `maxVelocity` base attribute.

Missing, ambiguous, non-finite, or non-positive values fail startup or request
resolution. There is no fallback to a Worker-wide speed.

Every in-space ship carries an immutable `DogmaShipMovementProfile`. The simulation
uses its maximum velocity to bound direction, follow, orbit, and go-to-point
resolution. Snapshot format 3 introduced storage of the complete profile with the
ship, and later snapshot formats retain it. During
startup reconciliation, Worker replaces a legacy or checkpointed profile from the
current durable ship type and the already validated build-pinned catalog before the
runtime becomes ready.

The profile also retains the base response time
`mass * inertia / 1,000,000`. This slice does not yet integrate acceleration or
turning from that response time. It also does not apply skills, fitted-item
modifiers, propulsion effects, stacking penalties, or effect lifecycle. Those
remain explicit unsupported parts of the computed-item Dogma layer; base values
must not be presented as a fully fitted ship calculation.

## Consequences

- A client or internal caller can request less than a ship's maximum velocity but
  cannot command a larger authoritative velocity.
- Different ship types can have different movement envelopes without changing
  simulation configuration.
- Checkpoints remain deterministic and self-describing, while restart reconciliation
  prevents stale build data from silently surviving a changed durable ship type.
- Solar-enabled Worker startup now requires a schema-5 artifact containing the
  selected ship's type and movement Dogma. Login-only Workers can continue using
  older compatible artifacts.
- The next physics slice can integrate acceleration and turning from the retained
  response time without introducing static-data I/O into the tick loop.
