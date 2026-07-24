# ADR 0009: Authored solar-system objects

Status: accepted

## Context

An in-space session needs more than dynamic ships. Stations, planets, and jump gates
provide the first stable nearby entities, navigation targets, and connections between
independently owned solar systems. Gateway must not invent these objects from
build-specific wire knowledge, and the simulation tick must not query static-data
storage.

These objects differ from dynamic ship state. Their identity, type, position, radius,
and topology are authored content promoted with a compatible server configuration.
They do not change as a consequence of ordinary movement or docking commands.

## Decision

Each Worker solar-system assignment declares an immutable set of authored objects:

- stations with stable entity and type identities;
- planets with stable entity and type identities;
- jump gates with stable entity and type identities plus a destination solar system.

Worker loads the selected type definitions before creating any runtime. Startup
requires each object type to exist in the build-pinned artifact, belong to the
expected station, planet, or jump-gate group, and have a positive finite radius.
Object identities are unique across configured assignments, every object belongs to
its assigned partition, and station entry points have a one-to-one authored station
object. Invalid content prevents Worker readiness.

The single-writer runtime holds the resulting protocol-neutral objects in memory.
They participate in movement-target lookup, so follow and orbit rules use the same
authoritative target validation as dynamic ships. A new session's bounded initial
snapshot contains both the current ships and the complete authored object set for
that solar system. Subsequent dynamic ship changes remain exactly ordered deltas.

Authored objects are deliberately absent from the mutable solar-system checkpoint.
Restart restores dynamic state, reloads authored content from the validated
build-pinned configuration, and then reconciles the two before readiness. Changing
authored objects currently requires an explicit configuration/content promotion and
Worker restart; live static-object mutation and static-object deltas are unsupported.

The internal gRPC contract carries neutral object metadata only. Gateway owns any
client-build-specific representation. The current build-3396210 graphical Destiny
encoding for a mixed nearby-object view remains capture-gated and is not inferred
from old reference implementations. Automated runtime and real-process topology
tests establish the Worker contract, not graphical-client compatibility.

## Consequences

- Two concurrently owned systems can expose distinct, isolated authored topology
  while advancing dynamic state independently.
- Gateway receives enough neutral metadata to build a client presentation without
  becoming authoritative for entity identity, type, position, or gate topology.
- Static-data compatibility failures surface during startup rather than during a
  session or simulation tick.
- Static objects can be authoritative movement targets without being misclassified
  as mutable inventory or ship state.
- A future content versioning or live-reload design must define atomic replacement,
  stream deltas, checkpoint compatibility, and client presentation ordering before
  authored objects become mutable.
