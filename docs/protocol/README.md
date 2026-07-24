# Real-client protocol map

This directory is an implementation map for getting the owned client through a
complete playable session. It describes the protocol as observable contracts:
what the client sends, what the server must return, which notifications make a
state change authoritative, and which ordering relationships matter.

It is not a captured transcript. A real startup is concurrent and conditional:
cache state, enabled UI services, the selected character, and session-change
handlers can all change the order of independent calls. Implement the partial
orders and typed contracts documented here, not a fixed packet-number script.

## Evidence labels

Every requirement should be read with one of these labels:

| Label | Meaning |
| --- | --- |
| **3396210 wire-confirmed** | Seen in a sanitized observation or represented by a build-3396210 compatibility fixture. This is the current target profile. |
| **Consumer-required** | Established from how an owned client consumes a result or event. It explains why a shape or notification matters, but is not by itself proof of the exact build-3396210 wire encoding or order. |
| **Server-covered** | Implemented and exercised through the repository's codecs, Gateway, or loopback topology. It is not automatically graphical-client proof. |
| **Open** | The exact build-3396210 request, response, event shape, or order still needs a fresh capture and a cleared fixture. |

The consumer inspection used to discover missing behavior is from a different
owned client build than the current target. It is useful for forming questions,
but it must never be used to silently fill a build-3396210 wire gap. Promote a
contract only after an exact-build observation confirms it.

## Reading order

1. [Wire framing, calls, and bound objects](wire-and-rpc.md)
2. [Connection, login, and character selection](connection-and-character-selection.md)
3. [Sessions and event delivery](sessions-and-events.md)
4. [Station bootstrap and inventory](station-and-inventory.md)
5. [Space bootstrap and navigation](space-and-navigation.md)
6. [Targeting, Dogma, and modules](targeting-and-modules.md)
7. [Implementation and capture checklist](implementation-checklist.md)

The existing build-specific specifications remain authoritative for details that
are already verified:

- [build-3396210 login](../compatibility/login-3396210.md)
- [build-3396210 station entry](../compatibility/station-entry-3396210.md)
- [build-3396210 Blue marshal](../compatibility/blue-marshal-3396210.md)

## Three meanings of success

A correlated call response means only that the blocking client call completed.
It does not necessarily mean that the requested gameplay change happened.

For each action, keep these outcomes distinct:

1. **Transport success:** the frame decoded and the connection stayed open.
2. **Call success:** the matching call response arrived, or a typed error ended
   the call cleanly.
3. **State success:** the required session, Destiny, inventory, targeting, or
   Dogma event made the new state visible to the client.

Returning `null` to every unknown call can satisfy the first two outcomes while
leaving the client stuck, visually stale, or internally inconsistent. An action
is implemented only when its consumer-visible state transition is also complete.

## Ownership boundary

All names, numeric message kinds, Blue-marshal shapes, leases, session tuples, and
client event envelopes belong in Gateway. Coordinator owns placement and fencing.
Worker receives protocol-neutral requests and publishes protocol-neutral state
changes. Worker must never need to understand a raw client packet, a Macho address,
a bound-object lease, or a Destiny command.
