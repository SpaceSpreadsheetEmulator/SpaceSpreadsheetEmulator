# Wire framing, calls, and bound objects

This page describes the reusable envelope beneath login, station, and gameplay
actions. Build-specific value shapes still belong in the compatibility profile.

## Frame decode and encode order

After TCP reassembly, one application frame is:

```text
4-byte little-endian payload length
payload bytes
```

The length counts only the payload. It must be non-negative and within the
configured bound. A partial prefix or payload is buffered; an invalid length,
invalid cipher padding, invalid compression stream, or invalid marshal value
closes that client connection.

The post-negotiation decode pipeline is:

```text
TCP bytes
  -> length-delimited payload
  -> AES-CBC decrypt, when negotiated
  -> zlib decompress, when the decrypted payload is compressed
  -> Blue-marshal value
  -> Macho packet
  -> RPC, session, ping, or notification decoder
```

Encoding is the reverse. The length prefix remains outside encryption. Inbound
and outbound CBC chaining are independent, so each direction advances its own IV
after each encrypted frame. Gateway must serialize outbound frames through one
ordered writer; two producers must never race the cipher or interleave bytes.

This pipeline is **3396210 wire-confirmed** and **Server-covered**.

## Macho packet envelope

After Blue-marshal decoding, a packet is an object whose state is a 14-item tuple:

| Index | Meaning |
| ---: | --- |
| `0` | Numeric message type |
| `1` | Source address |
| `2` | Destination address |
| `3` | User/account identity or null |
| `4` | Command payload |
| `5` through `13` | Nine ordered extension fields |

The extension fields are not an unordered metadata bag. Preserve observed request
extensions where the build contract requires them and emit exactly nine in every
new packet.

Address kinds used by the current codec are node, client, broadcast, and
any/service. Call correlation belongs to the call identifier in a client address,
not to the command payload.

## Macho message kinds needed by the playable slice

| Numeric type | Direction | Meaning | Reply rule |
| ---: | --- | --- | --- |
| `6` | client to server | Blocking call request | Must end in correlated type `7` or a typed error |
| `7` | server to client | Successful call response | A final response completes one pending call; a provisional response does not |
| `12` | either direction | One-way notification/event | No response |
| `15` | server to client | Call or service error | Completes the matching call as failure |
| `16` | server to client | Initial or changed session for build 3396210 | No acknowledgement |
| `20` | client to server | Ping request | Must receive type `21` |
| `21` | server to client | Ping response | Completes the ping exchange |

Types `6`, `7`, `12`, `16`, `20`, and `21` are used by the current
build-3396210 implementation. Type `15` is **Consumer-required** but is still an
**Open** production path: unsupported or rejected gameplay should eventually
return the exact target-build error envelope instead of an ambiguous successful
`null`.

Another inspected client build recognizes type `18` as an initial-session message.
The current build-3396210 trace and implementation use type `16` for the initial
account session. Treat that distinction as profile data; do not introduce type
`18` into the target profile without exact-build wire evidence.

## Correlated calls

A blocking client call carries a call identifier in its source address. A
successful response must:

- use type `7`;
- reverse source and destination routing appropriately;
- preserve the account and relevant extensions required by the profile;
- put the same call identifier in the client destination;
- carry exactly one result substream with the expected Blue-marshal type.

The client waits on that identifier, not merely on arrival order. A response with
the wrong identifier does not unblock the call. An unmatched or late response is
discarded; a missing response eventually times out and can stall a synchronous
session-change handler.

Server calls may finish with an error response instead. The error must retain the
same correlation and use a client-recognized exception or user-error payload.
Closing the transport is not a substitute for rejecting an action.

## Service calls

A type-`6` packet payload contains a nested substream. After decoding that
substream, the call body is a four-item tuple:

```text
object/discriminator
method name
positional arguments
keyword arguments
```

For a direct service call, the destination address identifies the service. For a
bound call, the first body value identifies the leased object. Positional
arguments are a tuple and keyword arguments are a dictionary, including the
profile's Macho version where required.

The client normally treats these calls as blocking. Even when UI code ignores the
return value, the matching response is required to release its pending call and
item/session locks. Calls deliberately sent as notifications are one-way and must
not receive a call response.

A successful type-`7` payload similarly contains a nested Blue-marshal substream
for the result. The consumer supports provisional responses in some paths, but
they do not finish the waiter; a final response is still required. The current
target slice does not emit provisional replies, so their exact build-3396210 shape
remains **Open**.

## Remote-object resolution and binding

Stateful services use a three-stage moniker pattern:

```text
client -> service.MachoResolveObject(bind parameters)
server -> correlated owner/node result

client -> service.MachoBindObject(bind parameters, optional nested call)
server -> correlated (lease, nested result)

client -> bound lease: Method(arguments)
server -> correlated method result
```

Important rules:

- The bind result is a two-item tuple even when the nested result is `null`.
- A nested call is executed as part of the bind and its value occupies the
  second tuple slot.
- A bound call carries the opaque leased object identity plus its method and
  arguments. Only Gateway translates that identity to an internal route.
- Multiple leases can coexist. Creating a station-inventory lease must not
  invalidate an already active ship-inventory lease.
- Location/session changes make location-bound monikers stale. The client may
  discard and resolve them again.
- A client may batch released lease identifiers in a type-`12`
  `ClientHasReleasedTheseObjects` notification. It is one-way; responding to it
  corrupts call ordering.

The bind pattern and release notification are **3396210 wire-confirmed**. The
client's general stale-moniker retry behavior is **Consumer-required**; exact
retry limits and error shapes remain profile-specific.

## Cached method results

Some startup methods return a cache wrapper rather than the underlying rowset or
object directly. Treat the wrapper as a protocol contract, not an optimization
that can be omitted:

- cache identity must be stable for the build and method result;
- version/hash metadata must describe the emitted payload;
- the payload field count and object type must match the target build;
- `objectCaching.GetCachableObject` must return the cached object selected by
  the client's key;
- compressed cached content must decode to the same semantic object.

The exact seven-field build-3396210 payload and current station/cache evidence
are specified in
[station-entry-3396210.md](../compatibility/station-entry-3396210.md).

## Atomic outbound ordering

One accepted inbound call can produce:

```text
zero or more notifications before the response
the correlated response
zero or more notifications after the response
```

Gateway must enqueue that batch atomically. Session selection and undock require a
session change before their call response; dock requires the response before its
notification sequence. Do not let an unrelated producer split either sequence.

Starting a Worker subscription can also race initial state with its bind response.
Queue the solar-system bind response first, then start publishing its snapshot.
