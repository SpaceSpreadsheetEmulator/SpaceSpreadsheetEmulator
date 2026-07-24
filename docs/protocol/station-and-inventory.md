# Station bootstrap and inventory

The station view is built by synchronous session handlers plus later UI loaders.
A live TCP connection and a successful `SelectCharacterID` response are not enough:
the client needs typed station, Dogma, and inventory state.

## Minimum docked bootstrap graph

These calls can interleave. The dependencies and consumed results matter more than
one observed frame order.

| Client request | Required successful result | Why the client needs it |
| --- | --- | --- |
| `stationSvc.GetStationItemBits()` | Four values ordered as owner, station item, operation, station type | Constructs the station item used by services and UI |
| `map.GetStationInfo()` | Method-cache descriptor, then fetchable cached station rowset | Lets the client index the current station and select its environment |
| `dogmaIM.MachoResolveObject(location)` | Owning Gateway node | Locates the session-scoped Dogma moniker |
| `dogmaIM.MachoBindObject(location, initial call)` | `(dogma lease, initial Dogma state)` | Primes character, ship, and location state |
| `invbroker.MachoResolveObject(location)` | Owning Gateway node | Locates inventory authority |
| `invbroker.MachoBindObject(location, optional initial call)` | `(broker lease, nested result)` | Establishes the inventory broker |
| `invbroker.GetItemDescriptor()` | Exact packed-item descriptor | Defines the columns and encodings used by inventory rows |
| Bound `GetInventoryFromId(...)` | A usable inventory/item lease | Opens the selected container |
| Bound `GetSelfInvItem()` | Typed item row | Materializes the leased ship or station item |
| Bound `List(...)` | Iterable typed inventory rows, possibly empty where semantically valid | Populates hangar, cargo, and ship models |
| `ship.MachoResolveObject(...)` | Owning Gateway node | Locates station-scoped ship access |
| `ship.MachoBindObject(..., GetDirtTimestamp(activeShipID))` | `(ship lease, timestamp-shaped result)` | Establishes the ship moniker used by station and undock flows |
| Skill-handler resolve/bind | `(skill lease, skills or boosters dictionary)` | Completes a synchronous station session handler |
| Crimewatch resolve/bind | `(crimewatch lease, complete client-state tuple)` | Establishes timers, engagements, flags, and safety |
| Corporation registry calls | Lease and typed aggression settings | Completes corporation/crimewatch refresh |
| Standing calls | Typed rowsets, including valid empty rowsets | Allows immediate indexing/filtering |

The listed route/bind boundaries, station/cache values, descriptor, inventory
calls, and ship-access exchange are **3396210 wire-confirmed** and covered at the
boundaries described in
[station-entry-3396210.md](../compatibility/station-entry-3396210.md). The full
Dogma initial-state object is excluded from that broad label and remains
capture-gated as described below. Several auxiliary startup calls still depend on
a private, allowlisted replay bundle and are not yet understood authored contracts.

## Station and map values

`stationSvc.GetStationItemBits()` is positional. Returning a dictionary, changing
the order, or returning `null` leaves later station consumers without the row they
expect.

`map.GetStationInfo()` is cached. Its first response is not the station rowset
itself. The client follows the cache descriptor with
`objectCaching.GetCachableObject`; the fetched payload must use the exact
seven-field build-3396210 cached-object state and decode to a station rowset that
can be indexed by station identifier.

All station identities and static columns come from the pinned static artifact
and authoritative character location. Never replay captured station/account
identities into production responses.

## Dogma bootstrap

The client binds a Dogma location scoped to its character and current location.
The initial bind can contain a combined call. The return always has this semantic
shape:

```text
(dogmaLocationLease, initialCallResult)
```

The initial Dogma result is consumed as structured character, ship, and
location state; a placeholder `null` can let the bind return while breaking the
synchronous session refresh. The inspected consumer requires character and ship
mappings, modified character attributes, system-wide effects on the ship, and
four logical ship-state components: instance state, flag quantities, weapon-bank
state, and heat state. These fields are **Consumer-required**; the exact
build-3396210 `GetAllInfo` object graph and encoding remain **Open**. Do not infer
them from a different client build.

The current repository supplies only the focused station/bootstrap Dogma values
already covered by its compatibility tests. Fitted-item effect evaluation is not
implemented.

## Ship-access bootstrap

The target-build station catalog leads to a ship-access resolve/bind with nested
`GetDirtTimestamp(activeShipID)`. Its bind reply contains the ship lease and a
timestamp-shaped big integer. The observed active-ship argument's integer wire
representation is profile-sensitive even when the numeric value is small.

## Opening an inventory

Opening the station hangar or ship cargo typically follows this partial order:

```text
resolve inventory broker if not already resolved
  -> bind broker if not already bound
  -> request inventory/container by identity and optional location
  -> receive or reuse an inventory-item lease
  -> bound List(flag or no flag)
  -> consume typed item rows
```

The standard rows used by this slice carry item identity, type, owner, location,
flag, quantity, group, category, and custom information in the build-specific
descriptor. Every returned packed row must use columns and encodings that exactly
match the descriptor returned by `GetItemDescriptor`. Numeric values are
authoritative; display names are not keys.

Leases overlap. The client can list the active ship, bind the station inventory,
then list the original ship lease again. Gateway therefore keeps leases keyed by
their opaque identity and scope rather than storing one mutable “current
inventory” binding.

An empty collection is valid only when the consumer permits it:

- an empty active-ship cargo can be a typed empty set;
- the station hangar must contain the authoritative active-ship row for the
  hangar view to select and render that ship;
- `null` is not an empty iterable and usually fails at a later iteration.

This open/list flow is **3396210 wire-confirmed** and **Server-covered** for the
starter inventory slice.

## Moving or stacking items

Consumer inspection identifies these bound mutation families:

```text
Add(itemID, sourceLocationID, quantity and/or flag)
MultiAdd(itemIDs, sourceLocationID, quantity and/or flag)
StackAll(...)
```

Exact overloads and keyword shapes are container-specific. They are
**Consumer-required** but **Open** for build 3396210.

The minimum success contract has two required server outputs:

```text
client -> type 6 bound mutation call
server owes:
  - type 7 correlated response
  - OnItemChange or OnItemsChanged with committed old/new state
```

Their exact relative order and whether the event is standalone or bundled remain
capture-gated.

The response releases the client-side item lock. The notification updates
inventory caches, Dogma consumers, and visible containers. A successful response
without the matching committed-state event leaves the UI stale.

Required event rules:

- emit stable item identity and enough old values to remove it from the old
  container/filter;
- emit the complete new values needed to insert or update it;
- never emit a negative quantity;
- represent split, merge, source deletion, and destination creation consistently;
- publish only after the authoritative database transaction commits;
- make retries idempotent so the same call cannot duplicate a stack.

The exact event tuple, response value, response/event order, and split-stack
behavior need a cleared build-3396210 capture before implementation.

## Boarding or changing the active ship

The client performs a Dogma location operation to board a ship and treats it as a
session-changing action. A complete implementation needs:

1. a correlated response or typed error;
2. a session transition whose `shipid` old/new pair selects the new active ship;
3. updated inventory and Dogma state for both ships;
4. any item/location events required to remove ambiguity from the hangar;
5. invalidation or rebinding of ship-scoped monikers.

This action is **Consumer-required** and **Open** for the exact target. It must be
implemented as an authoritative Worker mutation, not as a Gateway-only session
edit.

## Leaving the station

Undock starts from a ship-access moniker and changes the session from station to
space. Its full request/response/event sequence is documented in
[space-and-navigation.md](space-and-navigation.md).
