# ADR 0010: Durable starter inventory

Status: accepted

## Context

The first character slice persisted only the active ship. Gateway could list that
ship in the station hangar, but every other station or ship inventory list was an
empty synthesized response. Milestone 3 needs real container ownership and restart
durability before item movement, fitting, or economy rules can build on it.

## Decision

The build-pinned starter template declares a bounded set of item type, quantity, and
initial-container definitions. The initial profile contains one station-hangar stack
and one active-ship cargo stack. Content validation requires positive quantities,
unique type/container pairs, supported containers, and existing type definitions.

Character creation provisions the character, active ship, hangar stacks, and cargo
stacks in one serializable PostgreSQL transaction. Station items use the station as
their durable location. Cargo items use the active ship item as their durable parent.
Internal inventory flags describe these roles without embedding client wire flag
numbers in the Inventory or Persistence projects.

Repeated starter provisioning returns the existing item identities. An older
character missing the newly declared starter roles receives them in the same
serialized path; existing rows are not replaced. The character-selection application
view resolves each item's group and category from the pinned static-data artifact.
Worker sends neutral inventory metadata to Gateway, which alone maps the station
hangar and ship cargo roles to build-3396210 inventory rows.

This slice does not implement item transfer, splitting, fitting, capacity, or item
change notifications. Those operations must mutate the same authoritative rows with
idempotency and optimistic concurrency rather than reprovisioning starter content.

## Consequences

- Starter hangar and cargo contents survive fresh service providers and process
  restarts with stable item identities.
- Gateway inventory lists no longer invent or hard-code those child stacks.
- Invalid starter content fails before character creation.
- The inventory schema accepts explicit internal active-ship, station-hangar, and
  ship-cargo roles while remaining independent of a client build.
