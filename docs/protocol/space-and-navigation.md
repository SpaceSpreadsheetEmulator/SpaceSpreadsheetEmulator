# Space bootstrap and navigation

Space becomes usable only after three things agree:

1. the client session says the character is in a solar system;
2. the solar-system remote object is bound;
3. an initial Destiny snapshot contains the active ship and surrounding objects.

The session packet alone does not create a ballpark.

## Undock

### Client request

The exact target uses a ship-access moniker. Its first bind can combine the undock
operation:

```text
client -> type 6 ship.MachoResolveObject(station scope), when unresolved
server -> type 7 owning node

client -> type 6 ship.MachoBindObject(
                   station bind parameters,
                   nested Undock(activeShipID, ignoreContraband,
                                 onlineModules=...))
```

The UI performs this as a session-changing operation. The online-module collection
and precise positional/keyword representation are build-specific and must be
validated, not normalized away.

### Required server order

For an accepted build-3396210 undock:

```text
server -> type 16 station-to-space session change
server -> type 7 correlated ship bind response:
          (shipAccessLease, nested Undock result)
```

The session change must be in the before-response portion of one atomic outbound
batch. This ordering is **3396210 wire-confirmed** and **Server-covered**.

The transition must clear station/location-as-station, retain the active ship, and
set the authoritative solar-system hierarchy. Worker performs and fences the
mutation; Gateway only translates the resulting neutral transition.

### Space bind and initial state

The session change causes the client to initialize its space services:

```text
client -> beyonce.GetFormations()
server -> cached build-appropriate formations tuple

client -> beyonce.MachoResolveObject(solarSystemID)
server -> owning node

client -> beyonce.MachoBindObject(solarSystemID, optional initial call)
server -> (solarSystemLease, nested result)

server -> DoDestinyUpdate(initial SetState batch)
```

Gateway queues the bind response before starting the Worker subscription. The
subscription must begin with a fenced snapshot; deltas before a snapshot are a
protocol error.

For the inspected consumer, the first nonempty update list must begin with
`SetState`. Its outer `DoDestinyUpdate` arguments include the update list,
`waitForBubble`, and an optional Dogma-message list. Every action in the batch uses
one timestamp.

The minimal usable inflight state must include:

- the selected ship as the client's ego object;
- its authoritative position and motion;
- snapshot bytes containing the ego ball;
- a matching typed slim item for that ball;
- solar-system item context;
- typed drone, damage, effect, and debuff state;
- industry and research values.

Empty typed collections are acceptable for features with genuinely empty
authoritative state. Every slim item that is present must reference a ball decoded
from the same snapshot. These fields are **Consumer-required** for the inspected
build; the exact build-3396210 state-bag field list and encodings remain **Open**.

If the session's ship is not present, the client waits for that ego ball and the
space view does not finish. An empty or internally mixed-timestamp snapshot is not
a successful undock.

The backend transition, binding, snapshot-first stream, and current Destiny
mapping are **Server-covered**. The full graphical build-3396210 undock remains
**Open**.

### Build-3396210 bootstrap compatibility constraints

The inspected client parses a bound-object identifier as exactly two
colon-separated fields:

```text
N=<numeric node ID>:<opaque object ID>
```

Partition identity and fencing epochs are server state and must not be appended as
additional colon-separated fields. Gateway keeps the solar-system epoch beside the
opaque lease and validates it before accepting bound movement or docking calls.

After the station-to-space session change, location-scoped `ship`, `dogmaIM`,
`invbroker`, and `crimewatch` monikers use `(solarSystemID, 5)` rather than the
station scope `(stationID, 15)`. Their old station leases are invalidated by the
transition. The `beyonce.GetFormations` cache key may encode the service through
the build string table, so cache routing compares decoded text semantics rather
than requiring one concrete marshal string representation.

Character-scoped bootstrap calls are still valid when the selected character is
already in space. They must not be guarded by the presence of `stationid`. In
particular, the inspected build requires typed results for jump timers, modified
security systems, local factional-warfare occupation, the skill queue, wallet
balance, and home-station metadata before it proceeds to the ballpark request.
The empty/default portions remain explicitly non-authoritative compatibility
values; home-station and wallet fields are mapped from Worker-owned state and the
build-pinned station catalog.

These constraints are covered by the Gateway loopback and standalone-topology
tests. They remove known pre-snapshot bootstrap failures; a graphical client
checkpoint is still required before the complete inflight state can be called
wire-proven.

### Playable environment after inflight bootstrap

A minimal ego-only `SetState` can establish camera and ship UI, but it does not
make space meaningfully playable. The next snapshot/delta checkpoint adds authored
nearby stations, planets, gates, and other ships, with a matching ball and slim for
each. Those objects enable overview, approach/orbit, docking-range, targeting,
warp-destination, and jump-gate tests; they are not prerequisites for the first
inflight view.

## Movement actions

All current movement calls target the bound solar-system lease:

| User action | Client bound call | Consumer-required Destiny action |
| --- | --- | --- |
| Double-click direction | `CmdGotoDirection(x, y, z)` | `GotoDirection(shipID, x, y, z)` |
| Manual steering | `CmdSteerDirection(x, y, z)` | Compatible heading/direction update |
| Stop ship | `CmdStop()` | `Stop(shipID)` |
| Approach | `CmdFollowBall(targetID, approachRange)` | `FollowBall(shipID, targetID, range)` |
| Keep at range | `CmdFollowBall(targetID, selectedRange)` | `FollowBall(shipID, targetID, range)` |
| Orbit | `CmdOrbit(targetID, range)` | `Orbit(shipID, targetID, range)` |
| Go to point | `CmdGotoPoint(x, y, z)` | `GotoPoint(shipID, x, y, z)` |
| Set speed | `CmdSetSpeedFraction(fraction)` | Speed-fraction update |

Approach and keep-at-range intentionally share one protocol method. Their desired
range distinguishes the behavior.

For each accepted action, both sides of this contract are required:

```text
client -> type 6 bound Cmd...
server -> type 7 correlated null result
server -> one or more type 12 DoDestinyUpdate batches
```

This block is not a hard response-before-event order. Destiny state can arrive
before the final call response; only an exact build-3396210 action trace can fix
the relative order for a particular command.

The call response means the intent was accepted for processing. The Destiny
updates are authoritative: they change motion mode, velocity, position, or final
stopped state. The client may display an optimistic local indication immediately,
but server updates must converge it to authoritative state.

Each `DoDestinyUpdate` batch must contain actions at one Destiny timestamp. State
from different timestamps must be split into separate batches. Gateway preserves
the Worker's contiguous sequence and rejects stale epoch/session output.

Direction, stop, follow, orbit, and go-to-point request families are
**3396210 wire-confirmed** and **Server-covered**. Manual steering and speed
fraction are **Consumer-required** and **Open**. The current Gateway maps Worker
deltas to authoritative position and velocity updates; whether the graphical
target also requires each higher-level Destiny action above remains an explicit
compatibility gate.

## Warp

Consumer inspection identifies the bound call:

```text
CmdWarpToStuff(subjectKind, subjectID, keyword options)
```

Options can include the requested minimum range and fleet-related context. The
caller does not use the return value, but the call is blocking and still requires
a type-`7` response or type-`15` error.

A successful warp also requires authoritative pushes:

1. a Destiny update entering warp with the correct destination/context;
2. ordered motion/position updates required by the target build;
3. a final authoritative position and non-warp motion state;
4. the target-build `OnSpecialFX` warping effect and warp-to coordinates/mode for
   the canonical visual transition.

The exact build-3396210 call arguments, Destiny action encoding, effect event,
and response/event order are **Open**. The client derives its local warp-start,
deceleration, arrival, and finish events from authoritative ball mode, speed, and
effect state; those derived names are not server notification methods. Do not
model warp as a large `CmdGotoPoint`; it is a distinct state transition with
different client presentation and gameplay rules.

## Dock

### Client request

From an active solar-system lease:

```text
client -> type 6 bound CmdDock(stationID, activeShipID)
```

The target station must exist in the current authoritative system snapshot and be
a legal docking destination.

### Required server order

The current build-3396210 compatibility contract is one atomic outbound order:

```text
1. type 7  correlated CmdDock response
2. type 12 OnDockingAccepted
3. type 12 DoDestinyUpdate with the final stop/state
4. type 12 bound-object disconnect for the solar-system lease
5. type 16 space-to-station session change
6. type 12 OnDockingFinished
```

After the batch, Gateway stops only the old solar-system subscription. The
authenticated TCP session remains open and station session handlers begin their
bootstrap again.

This order is **3396210 wire-confirmed** and **Server-covered**. The exact
graphical docking presentation remains part of the future full space checkpoint.

## Stargate jump

Consumer inspection identifies this session-changing action:

```text
CmdStargateJump(gateID, destinationCelestialID, activeShipID)
```

It must not be treated as undock followed by dock. The minimum semantic transition
is:

```text
validate current gate and destination
  -> commit departure once, fenced by the source-system epoch
  -> tear down or invalidate the old ballpark and bound object
  -> change the solar-system/constellation/region session attributes
  -> resolve and bind the destination solar system
  -> receive a new snapshot containing the same active ship at the arrival state
  -> resume destination-system deltas
```

The blocking jump call still needs a correlated response or typed error. The
exact build-3396210 call route, response value, session-response ordering,
transition effects, old-ballpark clear event, and arrival Destiny encoding are
all **Open** and require a fresh controlled capture.

Cross-system authority also needs a durable, idempotent transfer protocol between
the two Worker partitions. Gateway cannot make the jump real by changing only its
client session tuple.
