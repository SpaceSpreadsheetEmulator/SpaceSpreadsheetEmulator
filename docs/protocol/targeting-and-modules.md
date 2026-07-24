# Targeting, Dogma, and modules

Target locks and fitted-module effects sit above the same in-space ballpark used by
movement. They also depend on a bound Dogma location. Neither action is complete
with a successful RPC response alone.

All action shapes on this page are **Consumer-required** discovery contracts.
Their exact build-3396210 envelopes and order are **Open** unless stated
otherwise.

## Preconditions

Before targeting or activating a normal module, the client needs:

- a character session with an active ship in space;
- a bound solar-system object and initial Destiny snapshot;
- a ball and slim item for any referenced target;
- a resolved and bound Dogma location;
- initial Dogma character, ship, fitted-item, attribute, and effect state.

The Dogma bind can combine an initial `GetAllInfo`-style call and returns:

```text
(dogmaLocationLease, initialDogmaState)
```

The exact initial state is a build-specific structured object. Implement only
fields established by an exact target-build consumer and capture; do not invent a
large opaque replay result.

## Locking a target

The client performs local legality checks first, including current ballpark and
range, then starts its targeting UI and issues:

```text
client -> type 6 bound Dogma AddTarget(targetID)
```

The successful call result is consumed as `(flag, targetList)`. The inspected
consumer has two completion paths:

1. `flag` is false, so it applies the target addition immediately; or
2. `flag` is true, so it keeps the locking UI active and waits for a target event.

The exact target-list type and build-3396210 polarity still need wire validation
before implementing the tuple.

The authoritative push family is:

```text
OnTarget(operation, targetID, reason)
OnTargets(...)
```

Consumer-recognized operations include add, clear, lost, another targeter added,
and another targeter lost. The exact strings and batch shape are profile data.
Target events can arrive as a standalone notification, inside `__MultiEvent`, or
as Dogma messages attached to `DoDestinyUpdate`; do not require a standalone frame.

A target-add event cannot be fully applied until the target's ball/slim item is in
the current ballpark. If the event legitimately races object entry, the client can
hold it pending; the server still should prefer snapshot/delta ordering that
publishes the object before the target relation.

### Success sequence

```text
client -> AddTarget call
server owes:
  - correlated response describing immediate or pending state
  - OnTarget/OnTargets add, unless the response contract completes it
client -> target appears in locked-target state
```

The relative order of the response and target event is **Open** for build 3396210.
Returning a success-shaped tuple while publishing neither an immediate acquired
state nor a later add event leaves the targeting UI spinning.

## Unlocking and losing targets

Client actions use bound Dogma methods in these families:

```text
RemoveTarget(targetID)
RemoveTargets(targetIDs)
ClearTargets()
```

Each blocking call needs a correlated response. Authoritative target events
separately remove or clear relations from the UI and Dogma state. A target can
also be lost without a client call because it leaves range, leaves the ballpark,
cloaks, is destroyed, or the session changes.

Session transitions clear location-scoped target state and can cause the client to
refresh target and targeter collections from the newly bound Dogma location.
Never carry a target identity from the old solar-system epoch into the new one.

## Module activation

A normal fitted-module activation is a blocking bound Dogma call:

```text
client -> Activate(moduleItemID, effectName, targetID-or-null, repeat)
```

The client locks the module item while the call is outstanding. A response is
therefore mandatory even when its value is ignored by the UI.

The authoritative effect event is:

```text
OnGodmaShipEffect(
  module item,
  effect,
  start/stop and active state,
  effect environment including target,
  start time,
  duration,
  repeat state,
  optional error state)
```

The exact positional tuple is build-specific. Semantically it must let the client
identify the module/effect instance, determine whether it started, bind it to the
correct source and target, and schedule its cycle.

The effect event can be standalone, inside `__MultiEvent`, or in the Dogma-message
list attached to `DoDestinyUpdate`. These are delivery alternatives for the same
authoritative effect state. Both the correlated response/error and authoritative
effect event are required for success; their relative order is **Open**.

A successful activation can additionally require:

- `OnModuleAttributeChange` or its batch form for heat, charges, duration, damage,
  or other changed attributes;
- location/ship attribute changes caused by the effect;
- `OnSpecialFX` for visible source/target presentation;
- Destiny changes when the effect alters movement.

The minimum active-module UI is driven by `OnGodmaShipEffect`, not by the return
value of `Activate`.

## Deactivation and effect completion

Manual deactivation uses:

```text
client -> Deactivate(moduleItemID, effectName)
```

The server owes both a correlated response/error and an
`OnGodmaShipEffect` stop/completion state; their relative order is **Open**.
Natural cycle completion, target loss, charge exhaustion, ship destruction, or
session transition can stop an effect without a client deactivation call. Those
paths still need the authoritative stop event and any resulting attribute changes.

Repeat state belongs to the authoritative effect instance. Retries of the initial
call must not create duplicate active effects or duplicate resource consumption.

## Failure behavior

Range, target, capacitor, fitting, cooldown, safety, and effect legality belong in
the authoritative gameplay layer. A rejected call should return the exact
target-build typed user error so the client:

- releases its pending module or target lock;
- restores the inactive UI state;
- displays the appropriate user-facing reason;
- does not wait for an event that will never arrive.

Until the type-`15` error wrapper and the required user-error payloads are
capture-verified, these actions must remain explicitly unsupported rather than
returning successful `null`.

## Worker/Gateway split

Worker should expose neutral operations such as “request target lock” and “request
effect activation,” and publish neutral target/effect state changes with epoch and
sequence. Gateway owns:

- Dogma bound-call decoding;
- client effect-name/profile mapping;
- target and effect event tuple construction;
- Macho correlation and errors;
- build-specific time and attribute encodings.
