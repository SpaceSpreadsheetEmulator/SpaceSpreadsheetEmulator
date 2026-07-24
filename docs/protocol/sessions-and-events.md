# Sessions and event delivery

Calls answer questions. Sessions and events tell the client that the world
changed. Most gameplay actions need both.

## Session-change contract

A type-`16` session notification identifies the connection session and carries a
change tuple containing a dictionary. Each changed attribute is an `(old, new)`
pair. In the observed target envelope, the value before that dictionary is `0`;
it is not a client-consumed sequence number.

Common attributes include:

- authenticated user and role;
- character and corporation;
- ship;
- station or structure;
- solar system, constellation, and region.

Use explicit null transitions. Moving from station to space means the station
attribute becomes null while the solar-system and ship remain or become valid.
Docking performs the inverse. A system jump changes the solar-system hierarchy
without inventing a station.

Session packets are one-way. The client sends no acknowledgement. Their success
is observed indirectly through the calls and view changes they trigger.

## Client processing phases

The client-consumer contract is this partial order:

```text
receive a valid session change in transport order
  -> announce that a session is about to change
  -> disconnect/invalidate objects scoped to the old values
  -> apply all old/new attribute pairs as one transition
  -> run blocking ProcessSessionChange handlers
  -> connect objects scoped to the new values
  -> scatter post-change OnSessionChanged notifications
```

This is **Consumer-required**. Exact handler membership varies by build and
enabled client features.

Consequences for the server:

- Never expose a half-updated session across separate notifications.
- Preserve transport order for session changes.
- Expect new RPCs while the session notification is being processed.
- Invalidate Gateway leases and Worker subscriptions at the same semantic
  boundary as the client invalidates its monikers.
- Do not wait for a session acknowledgement that will never arrive.

## Event dispatch classes

The event method name affects how the client dispatches it:

| Name family | Client behavior | Protocol implication |
| --- | --- | --- |
| `Process...` | Blocking, ordered processing chain | A slow or missing dependency can delay the enclosing transition |
| `Do...` | Immediate synchronous delivery; handlers are not allowed to issue blocking calls | Use for state application such as Destiny batches |
| `On...` | Asynchronous scatter to listeners | Listener completion order is not a server ordering guarantee |
| `__MultiEvent` | Unpacks several events from one envelope | Preserve the contained event order and validate every entry |

All are one-way notifications, normally carried as type `12`. They do not receive
a call response. A listener failure does not turn the event into an RPC failure,
so use observable state and subsequent client traffic to establish acceptance.

During `ProcessSessionChange`, an unanswered blocking RPC stalls that transition
until its timeout. A malformed but answered dependency usually aborts only that
subscriber while later subscribers continue. Malformed core session-envelope
decoding is different: it can escape the ordinary listener isolation and close the
transport.

## Action-to-authority matrix

| Action | Blocking call completion | Authoritative client-visible state |
| --- | --- | --- |
| Select character | `SelectCharacterID` response | Character/station or character/space session change |
| Open inventory | Inventory bind/list responses | Typed inventory rows establish the initial view |
| Move an item | Mutation response | `OnItemChange` or `OnItemsChanged` with committed values |
| Undock | Nested `Undock` bind response | Station-to-space session plus initial Destiny snapshot containing the active ship |
| Direction/follow/orbit/point/stop | Bound movement response | One or more `DoDestinyUpdate` batches |
| Warp | Bound warp response | Destiny warp state and subsequent authoritative position; visual effects as required |
| Lock target | `AddTarget` response | Immediate completed result or `OnTarget`/`OnTargets` add |
| Activate module | Dogma `Activate` response | `OnGodmaShipEffect` start, plus related attribute/FX events |
| Dock | `CmdDock` response | Docking events, final space update, object disconnect, and space-to-station session |
| Jump | Jump response | Old-space teardown, system session change, new bind, and fresh Destiny snapshot |

The exact requirements for each row are expanded in the action-specific pages.

## Ordering, duplicates, and fencing

Every authoritative event emitted from Worker must carry enough neutral metadata
for Gateway to enforce:

- the expected solar-system route and ownership epoch;
- the authenticated gameplay session;
- an initial snapshot before deltas;
- contiguous per-stream sequence numbers;
- cancellation when a dock, jump, reconnect, or migration replaces the stream.

Gateway then maps accepted neutral state to the build-specific event envelope. It
must never forward a stale-epoch update merely because it is syntactically valid.
Worker stream sequences and epochs are internal ordering controls; they are not
serialized as a nonexistent type-`16` session version.

Retries of client calls require idempotent gameplay receipts. Replaying the same
request must not apply an inventory move, dock, jump, or module transition twice.
Re-sending the resulting event may still be necessary after reconnect, but it must
describe the same committed state/version.

## Error events versus call errors

Use the correlated type-`15` path when the action itself is rejected and the
client expects its blocking call to fail. Use a one-way event when an already
accepted state later changes for an independent reason, such as losing a target.

Do not send both an unconditional success response and a contradictory failure
event merely to keep the client moving. Capture the target-build failure path,
author a cleared fixture, and map the domain rejection deliberately.
