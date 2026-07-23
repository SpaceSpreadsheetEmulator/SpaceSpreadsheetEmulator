# Build 3396210 character selection and station entry

This is an independently authored behavioral specification from a private
build-3396210 emulator trace, an exact-build static RPC index, and a successful
newer-client live trace used only as labelled comparative evidence. It contains no
captured credentials, session material, account identifiers, client bytecode, or
third-party implementation code.

## Evidence rules

For the current compatibility target, evidence is used in this order:

1. the exact build-3396210 client communicating with this Gateway;
2. dynamic response shapes and ordering from private captures;
3. the exact build-3396210 RPC index for locating client call sites and signatures;
4. newer live captures only to identify a question to verify against the target.

The RPC index is static discovery, not a substitute for the wire trace. Positional
and keyword counts describe a Python call site; the decoded trace establishes the
actual wire envelope, value types, and packet order.

## State sequence

### Connection and account session

1. Gateway sends the pinned version tuple and the client returns the same build,
   version, project, and Macho identity.
2. Queue/version-key negotiation completes.
3. The client selects placebo framing or supplies independent inbound and outbound
   AES-CBC state.
4. Authentication completes and Gateway sends the initial account-level type-16
   session notification.
5. The client requests service information, caches, character-selection rowsets,
   owner records, corporation tickers, population-cap routing, and the character
   lock type.

These steps are specified in `login-3396210.md`.

### Character selection

The target client calls `charUnboundMgr.SelectCharacterID` with:

- positional arguments `(characterID, null, skipTutorial)`;
- keyword arguments containing `machoVersion`.

Some live proxy topologies redirect the first call and accept a retry. A direct
Gateway does not need to imitate that redirect: the exact target accepted direct
handling.

On a valid selection, Gateway sends these packets in order:

1. `SESSIONCHANGENOTIFICATION`, numeric type `16`;
2. the correlated `CALL_RSP`, numeric type `7`, with a null result.

The session-notification payload is:

```text
(
  gatewaySessionID,
  (
    0,
    {
      genderID:         (null, current),
      bloodlineID:      (null, current),
      raceID:           (null, current),
      corpid:           (null, current),
      regionid:         (null, current),
      stationid:        (null, current),
      locationid:       (null, current),
      hqID:             (null, current),
      solarsystemid2:   (null, current),
      shipid:           (null, current),
      charid:           (null, current),
      constellationid:  (null, current)
    }
  ),
  []
)
```

For a docked character, `locationid` equals `stationid`. For a stationless
character it equals the solar-system identity. The first five request extensions
are preserved, followed by `false`, `0`, `1000`, and null.

## Observed post-selection behavior

In the exact failed emulator trace:

| Frame | Direction | Meaning |
| --- | --- | --- |
| 88 | client to Gateway | `charUnboundMgr.SelectCharacterID` |
| 89 | Gateway to client | station `SESSIONCHANGENOTIFICATION` |
| 90 | Gateway to client | correlated selection response |
| 91-182 | both | 46 additional startup request/response exchanges |
| 183-184 | both | successful ping round trip |
| 185 | client to Gateway | `eventLog.LogClientStats` |
| 186-188 | client to Gateway | another ping and two startup calls already queued |

The traffic after frame 90 proves the client accepted the session change. Gateway
closed while decoding frame 185, before it could answer frames 185 through 188.

The exact client returned null from currently unsupported station bootstrap calls
and still reached the successful ping. Those null results are compatibility gaps,
but they are not evidence for this disconnect.

## Saved-value failure boundary

Frame 185 is a zlib-compressed Macho `CALL_REQ`. Its outer packet decodes correctly.
The nested RPC substream is a 671-byte Blue-marshal document whose header declares
nine saved values. The root ends at byte 635 and the final 36 bytes are nine
little-endian saved-slot entries.

The saved-slot table belongs to the marshal document:

```text
7e
uint32 savedCount
root value, bounded before the table
uint32 savedSlots[savedCount]
```

Each save-flagged value consumes the next table entry and populates that one-based
slot. References resolve populated slots, not save-flag encounter positions. Live
documents include non-sequential slot mappings, so merely skipping `4 * savedCount`
bytes or assigning sequential slots is incorrect.

The former decoder parsed the root and required immediate end-of-input. It therefore
reported the valid table at byte 635 as `protocol.trailing_data`, causing Gateway to
close the client connection. The local compatibility test also stopped at the outer
Macho packet, so it did not exercise the failing nested substream.

The decoder and tests must enforce the full rule in
`blue-marshal-3396210.md`. The explicit local-capture gate must decode every nested
client call so this class of failure cannot pass on outer-envelope coverage alone.

## Client-notification failure boundary

After saved-value support was added, a fresh exact-build trace continued through
`eventLog.LogClientStats` and more than a dozen additional station-startup calls.
The next close occurred after `stationSvc.GetStationItemBits`. The first subsequent
client packet was not another RPC call:

| Relative order | Direction | Meaning |
| --- | --- | --- |
| 1 | client to Gateway | `stationSvc.GetStationItemBits` call |
| 2 | Gateway to client | correlated null response |
| 3 | client to Gateway | type-12 `ClientHasReleasedTheseObjects` notification |

The notification has node-to-node addresses and the same two-field outer payload
envelope used by nested Macho messages. Its substream contains a four-field body:
the released object leases, method name, an empty positional tuple, and keyword
arguments containing `machoVersion`. A successful live-client capture contains the
same notification shape and does not correlate it with a response.

Gateway formerly sent every authenticated non-ping packet to the `CALL_REQ` decoder.
That decoder correctly rejected numeric type 12, but the connection layer
incorrectly treated the rejection as malformed input and closed. Gateway now
dispatches by Macho packet type first: ping requests receive ping responses, call
requests use RPC dispatch, and structurally valid client notifications are decoded,
logged by method, and consumed without a response. Malformed notification envelopes
or bodies still fail closed.

The static client RPC index does not list
`ClientHasReleasedTheseObjects` because it is an internal Macho object-lifecycle
notification rather than a direct remote-service call site. It does independently
confirm that `GetStationItemBits` is a zero-argument `stationSvc` call. The dynamic
trace remains authoritative for the notification body and ordering.

## Successful station baseline

A successful newer-client live trace provides this comparative sequence:

1. resolve and bind `dogmaIM`;
2. resolve and bind `invbroker`, then use bound `GetInventoryFromId`;
3. resolve and bind `crimewatch`, including `GetClientStates`;
4. resolve and bind `corpRegistry`, including aggression settings;
5. load standings, cached objects, settings, journal, and other auxiliary state;
6. call `stationSvc.GetStationItemBits`;
7. request inventory and station data;
8. call `map.GetStationInfo`;
9. continue telemetry and ping traffic.

The exact build RPC index confirms:

- `StationSvc.GetStationItem` makes a zero-argument
  `stationSvc.GetStationItemBits` call;
- the returned four values construct a row ordered as `ownerID`, `itemID`,
  `operationID`, `stationTypeID`;
- `UI.GetStation` uses zero-argument `map.GetStationInfo`;
- `invCache.GetInventoryFromId` accepts item, passive, and location inputs at the
  client boundary and may call bound `GetSelfInvItem`;
- the observed item-row descriptor columns are `itemID`, `typeID`, `ownerID`,
  `locationID`, `flagID`, `quantity`, `groupID`, `categoryID`, and `customInfo`.

These shapes are recorded for the next proven compatibility boundary. They must not
be copied wholesale from a newer build or filled with captured identities. Any
authoritative station, character, ship, or inventory implementation must be mapped
from Worker-owned protocol-neutral state.

## Method-cache exchange

`agentMgr.GetAgents` and `map.GetStationInfo` use the Macho method cache. Returning
their rowsets directly is not the observed contract.

The service call first returns:

```text
objectCaching.CachedMethodCallResult(
  { versionCheck: policy },
  cachedObject.CachedObject(
    ("Method Call", "server", (service, method)),
    serviceNodeID,
    referenceVersion),
  null)
```

The client then calls `objectCaching.GetCachableObject` with:

```text
(1, cacheKey, referenceVersion, serviceNodeID)
```

The exact build's cache payload has seven positional state fields:

```text
objectCaching.CachedObject(
  payloadVersion,
  null,
  payloadNodeID,
  1,
  zlib(marshal(methodResult)),
  1,
  cacheKey)
```

Both integer flag fields and the trailing key are required. A six-field payload
that omits the second flag shifts `cacheKey` into the flag position. The client
can continue far enough to construct station UI from session state, but does not
materialize the station catalog and therefore does not begin the docked ship
moniker sequence.

After the seven-field contract was restored, the exact target's next calls after
the station cache fetch were `ship.MachoResolveObject` and
`ship.MachoBindObject` with nested `GetDirtTimestamp`. Those calls had been absent
from the six-field trace. This call-graph change is the protocol assertion for
successful station-catalog consumption; it is stronger than connection liveness
or UI presence alone.

The ship identity supplied to `GetDirtTimestamp` is encoded as a Blue-marshaled
big integer in the exact target trace even when its numeric value fits a 32-bit
integer. Dispatch must accept that observed integer representation without
loosening unrelated argument validation.

Inventory item leases overlap during hangar initialization. The client first
binds the active ship through `invbroker`, later binds the station inventory, and
then calls `List` again on the original ship-item lease. Opening the station
inventory therefore must not invalidate or replace existing item leases. For an
empty active ship inventory, the observed response is an empty built-in set;
returning null prevents the subsequent ship-cosmetics and model-loading calls.

## Compatibility checkpoint

The graphical checkpoint is complete only when the exact build-3396210 client:

1. receives the station session notification before the selection response;
2. receives a correlated response to saved-table telemetry;
3. continues through later RPC and ping traffic without a `protocol.*` close;
4. displays the station view;
5. remains connected for at least 30 seconds and two subsequent ping cycles.

A fresh exact-build verification now passes both former transport boundaries:
`LogClientStats` receives a response, `ClientHasReleasedTheseObjects` is consumed
without a response, and later station-session calls continue beyond 30 seconds
without a protocol close. The exact build also completes the visual assertion:
the station interior, active ship, station-services UI, and normal client UI are
visible together. The decisive fixes were the seven-field cache payload and
retaining overlapping inventory-item leases; connection liveness alone did not
establish either requirement.

If the connection remains alive but the station does not render, stop at that new
boundary. Compare the fresh trace with the sequence above, identify the first
divergent typed result, inspect its exact-build consumer through the RPC index,
write a sanitized fixture and behavior specification, and only then add that one
contract. Core identity and gameplay state are never eligible for captured replay.

## Hangar inventory rendering boundary

The next exact-build trace accepted the Dogma bootstrap, ship inventory,
station catalog, station item, and both inventory leases. It remained connected,
but the hangar did not render. The first rendering-specific divergence was the
bound station inventory call:

```text
GetInventory(containerHangar, null)
List(flag=flagHangar)
```

The response was a correctly encoded `__builtin__.set`, but it was empty. Exact
client consumer inspection establishes the consequence:

1. `HangarView.LoadView` calls `SetUpModels`.
2. `SetUpModels` calls `GetShipItemFromHangar(session.shipid)`.
3. `GetShipItemFromHangar` iterates `GetInventory(containerHangar).List(flagHangar)`.
4. It returns only a row whose `itemID` matches the active ship and whose
   `categoryID` is the ship category.
5. The returned row supplies the `typeID` used to select the hangar behavior,
   scene, and model.

A successful comparative trace returns a one-element typed set for the same
`List(flag=flagHangar)` call. The element uses the standard inventory descriptor
and represents the active ship with:

- `locationID` equal to the current station;
- `flagID` equal to the hangar flag;
- `categoryID` equal to the ship category;
- `typeID` and `groupID` loaded from the pinned static-data artifact.

An empty set is therefore structurally valid but semantically incomplete: it
keeps the MachoNet session alive while preventing the active ship and hangar
scene from being selected. The Gateway may return an empty set for currently
unsupported ship contents, but the station hangar inventory must contain the
authoritative active-ship row when no flag filter or the hangar flag is
requested.

## Crimewatch session-change boundary

Returning the active ship is necessary but does not complete the synchronous
session-change handlers. Exact-build client inspection shows that the crimewatch
service unpacks four values immediately after location or character changes:

```text
(
    (
        (weaponsTimerState, weaponsExpiry),
        (pvpTimerState, pvpExpiry),
        (npcTimerState, npcExpiry),
        (criminalTimerState, criminalExpiry),
        (disapprovalTimerState, disapprovalExpiry)
    ),
    engagements,
    (criminalCharacters, suspectCharacters),
    safetyLevel
)
```

For a newly provisioned docked character with no authoritative timers or
engagements, the build-3396210 neutral state is:

- timer states `100`, `200`, `400`, `300`, and `500`, in that order, each with a
  null expiry;
- an empty engagements dictionary;
- two typed empty `__builtin__.set` objects;
- minimum safety level `1`.

The RPC sequence is one remote-object exchange:

1. `crimewatch.MachoResolveObject((stationID, 15))` returns the Gateway node;
2. `crimewatch.MachoBindObject` carries a nested zero-argument
   `GetClientStates` call;
3. the bind result is `(lease, clientStates)`.

Do not replace the typed sets with lists, change the timer order, or return a
shorter tuple. Those values are unpacked positionally.

The same client session-change handler refreshes corporation aggression settings
when `corpid` changes:

1. `corpRegistry.MachoResolveObject(corporationID)` returns the Gateway node;
2. `corpRegistry.MachoBindObject(corporationID, null)` returns `(lease, null)`;
3. bound `GetAggressionSettings()` returns an
   `crimewatch.corp_aggression.settings.AggressionSettings` object.

The corporation kind must come from build-pinned static data or authoritative
gameplay state. For an NPC corporation, the exact-build client's default is
disabled: `_enableAfter` is null and `_disableAfter` is zero. This is a semantic
default derived from the client contract, not a replay of a captured
corporation's live aggression state.

## NPC-standing session-change boundary

The next exact-build call after corporation aggression settings is
`standingMgr.GetNPCNPCStandings()`. Returning null is not a harmless missing
feature. `Standing.__RefreshStandings` immediately calls `Filter("toID")` and
`Filter("fromID")` on the result, so a null response raises before the standings
service finishes its synchronous session refresh.

The successful live response is an object-cache wrapper around a compressed
marshal payload. Inflating and decoding that payload establishes the underlying
contract:

- object type `carbon.common.script.sys.crowset.CRowset`;
- packed-row descriptor columns `fromID` (`I4`, encoding 3), `toID` (`I4`,
  encoding 3), and `standing` (`R8`, encoding 5);
- rows describe NPC-faction relationships.

The currently promoted build-3396210 static artifact contains faction identities
but not their standing relationships. Until a build-pinned relationship source is
promoted through the static-data boundary, Gateway returns an empty rowset with
the exact observed descriptor. This is a typed neutral result: both client
`Filter` calls produce empty mappings, while no captured identities or standings
are embedded in production code.

## Large-frame capture integrity

The capture probe can record overlapping application-read chunks when the client
reads a large inbound frame concurrently. A naive reconstruction may therefore
duplicate whole chunks and report a marshal failure even though the client
accepted the socket stream and issued later RPCs.

Before diagnosing a large response as malformed:

1. compare captured chunk sequence ranges and reconstructed byte counts;
2. check whether the client issued later calls on the same connection;
3. reproduce the exact response shape through the production codec, including
   its Macho response envelope;
4. treat a decoder-only failure as a capture-integrity issue when duplicated
   chunks explain the byte surplus.

For the build-3396210 station catalog, a production-scale sanitized catalog
round-trips through both Blue marshal and the complete Macho response envelope.
This test is the regression gate for large `map.GetStationInfo` results.
