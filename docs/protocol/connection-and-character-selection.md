# Connection, login, and character selection

This sequence gets one TCP connection from version negotiation to a character
session. Exact tuple fields and build constants are defined in
[login-3396210.md](../compatibility/login-3396210.md); this page concentrates on
direction, ordering, and what unblocks the client.

## Handshake state machine

Each row is one length-delimited Blue-marshal frame. Macho call envelopes begin
only after the handshake completes.

| Step | Direction | Frame | Server obligation |
| ---: | --- | --- | --- |
| 1 | server to client | Server version identity | Send the exact configured build/profile tuple |
| 2 | client to server | Client version identity | Validate every compatibility field before creating a session |
| 3 | client to server | Queue check (`QC`) or version-key (`VK`) command | Branch without accepting unrelated commands |
| 3a | server to client | Queue position, then server version | For `QC`, restart at step 2 |
| 4 | client to server | Crypto-mode request | Validate placebo or AES parameters |
| 5 | server to client | `"OK CC"` | Send in plaintext, then enable the negotiated mode |
| 6 | client to server | Login credentials | Authenticate without logging the proof |
| 7 | server to client | Password-version value | Use the negotiated encryption mode |
| 8 | server to client | Server challenge tuple | Use the build-specific safe challenge contract |
| 9 | client to server | Challenge result tuple | Validate its outer shape and bind it to this connection |
| 10 | server to client | Session acknowledgement | Supply account, client, session, role, locale, and peer fields |
| 11 | server to client | Initial account-level type-`16` session | Establish account attributes before ordinary startup calls |

Only step 3a loops. A malformed or out-of-order value fails closed for that
connection. A `VK` command advances to crypto negotiation; it is not itself the
crypto request.

This state machine is **3396210 wire-confirmed** and **Server-covered**.

## First post-login traffic

After the account-level session, the minimum target-build bootstrap contains these
blocking dependencies:

```text
service discovery through machoNet.GetServiceInfo
  -> materialize any lazy cache object it returns
  -> synchronize time through machoNet.GetTime
  -> request character-selection data
```

The exact target trace and current implementation establish these routes. Cache
hits can remove a fetch from an individual run, and background work can interleave
with the blocking spine. Returning a lazy cache reference without implementing its
`objectCaching.GetCachableObject` fetch blocks startup rather than deferring
optional work.

Around that spine, the client can interleave:

- type-`20` pings;
- startup/cache calls;
- `charUnboundMgr.GetCharacterSelectionData`;
- owner, corporation-name, and dynamic-location lookups for identities in the
  selection result;
- telemetry notifications or calls.

Independent calls do not have a guaranteed total order. Every type-`6` call still
needs its own correlated type-`7` response or type-`15` error, even when its result
appears unimportant to the login UI. Type-`12` client notifications are consumed
without a response.

Character-selection data is authoritative and must be authored from the
authenticated account. It cannot come from a captured replay. The build-3396210
rowsets, column order, neutral values, and validation rules are specified in
[login-3396210.md](../compatibility/login-3396210.md).

When a selection or session row references a location that is not already in
build-pinned client data, the client can issue blocking
`config.GetMultiLocationsEx`. The target-build result must provide its location
identity/name, solar-system identity, coordinates, and localized-name fields in
the expected typed row shape.

## Selecting a character

Before the final click action, the client can resolve/bind the population-cap
moniker, request a load slot, and query the character lock type. The load-slot
result is collection- or mapping-like; an empty non-queued value allows ordinary
selection, while `null` is not an equivalent result. Queue pushes are required
only when the server actually queues the character. These gating calls are
blocking and need correlated replies when emitted.

On an inspected SSO path, selection also makes a conditional
`multiLoginBlocker.Login(computerHash)` call and requires a truthy result. Password
login skips that branch. This is **Consumer-required** and **Open** for the exact
target profile; never log or persist the supplied device-derived value in Gateway
protocol diagnostics.

The action begins with a blocking service call:

```text
client type 6:
  charUnboundMgr.SelectCharacterID(
    selectedCharacterID,
    secondChoiceID,
    skipTutorial,
    build-required keyword arguments)
```

The selected identifier must be one that Gateway returned for this authenticated
session. A successful docked selection has this required order:

```text
client -> CALL_REQ SelectCharacterID

server -> SESSION_CHANGE
          account session -> character + ship + station + corporation + map session

server -> CALL_RSP SelectCharacterID
          result: null
```

The session change is type `16`; the call response is type `7` with the request's
call identifier. Gateway enqueues both as one ordered batch, with the session
change first. This ordering is **3396210 wire-confirmed** and
**Server-covered**.

Selection is not successful merely because the call response arrived. The client
must accept the new session, run its synchronous session-change handlers, complete
their dependent calls, and enter the appropriate view.

## Calls induced by the session change

The docked session can immediately cause calls for:

- station identity and station catalog;
- map and object-cache data;
- Dogma location resolution, binding, and initial state;
- inventory-broker resolution, binding, active-ship inventory, and station hangar;
- skills and boosters;
- crimewatch state and corporation aggression settings;
- standings, corporation, war, agents, and other enabled UI services.

These calls are a dependency graph, not a numbered transcript. Some can run in
parallel, some are cache-dependent, and some are made from blocking
`ProcessSessionChange` handlers. Gateway must be able to serve them as soon as the
type-`16` packet is delivered; it must not wait for the client to finish handling
the following selection response.

See [station-and-inventory.md](station-and-inventory.md) for the minimum typed
contracts that make station entry visually complete.

## Selection into an existing in-space state

On reconnect, an authoritative character can already have no station and have an
active solar-system presence. The character session must then identify that
solar-system and ship without inventing a station. The client is expected to
resolve and bind the solar-system remote object and receive a fresh initial
ballpark snapshot.

The initial character session must set all of the active location attributes
consistently:

```text
stationid      = null
locationid     = solarSystemID
solarsystemid  = solarSystemID
solarsystemid2 = solarSystemID
shipid         = activeShipID
```

`solarsystemid2` alone identifies the map hierarchy but does not establish an
active space location. Without `solarsystemid`, build 3396210 rejects dependent
location monikers before it requests the solar-system ballpark.

This path is **Server-covered** by the encoded Gateway loopback and topology
tests. Its graphical build-3396210 completion is still **Open** until a reconnect
reaches and accepts the fresh Destiny snapshot. Do not assume the docked bootstrap
or a previously issued lease remains valid.

## Failure boundaries

- Version mismatch: close before authentication or durable session creation.
- Authentication rejection: close without returning account or character data.
- Unknown selected character: return a target-build typed error once that
  contract is known; do not transition the session.
- Missing session-dependent RPC response: the selecting UI stalls until the
  blocking-call timeout even though `SelectCharacterID` itself returned.
- Malformed session data: fail the connection rather than partially applying a
  character identity.
- Missing station or space view state: keep the transport diagnosis separate from
  the missing typed result or event diagnosis.
