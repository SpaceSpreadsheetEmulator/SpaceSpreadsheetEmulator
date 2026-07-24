# ADR 0011: Streamed in-space player presence

Status: accepted

## Context

The protocol-neutral solar stream already exposes authoritative ship snapshots and
enter/leave deltas. Character identity was limited to a numeric identifier, so a
consumer could not build a useful nearby-player or provisional local-presence list
without a separate lookup or Gateway-owned cache.

The target architecture assigns channel membership, messages, and fan-out to a
separate Chat service. Creating a hidden chat authority inside Gateway or the solar
runtime would violate that boundary.

## Decision

Every in-space ship state carries the durable character name alongside its character
and ship identities. The name enters the runtime through authenticated durable
character resolution; Gateway cannot supply or override it.

The initial bounded session snapshot therefore provides the complete ordered
in-space player set for that solar-system partition. Existing entity-entered and
entity-left events are the membership deltas, and retain the same epoch, sequence,
backpressure, and fail-closed rules as movement output. Snapshot format 4 checkpoints
the name with each in-space ship, while startup reconciliation refreshes it from
durable character state before the runtime becomes ready.

This is deliberately an in-space presence view, not local chat. Docked characters,
chat messages, access rules, moderation, channel-shard ownership, XMPP delivery, and
presence recovery across Gateway sessions remain responsibilities of the future
Chat vertical slice. Gateway does not yet map this metadata to the graphical
build-3396210 local-chat member panel.

## Consequences

- Automated consumers can maintain a named, ordered in-space player list without
  polling or joining data in Gateway.
- Renames and restart recovery use durable character state rather than stale
  checkpoint metadata.
- The solar-system Worker remains authoritative only for nearby in-space presence;
  it does not become the chat-channel owner.
