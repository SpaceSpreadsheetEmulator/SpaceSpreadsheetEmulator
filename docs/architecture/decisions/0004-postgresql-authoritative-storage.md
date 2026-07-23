# ADR 0004: PostgreSQL as authoritative mutable storage

Status: accepted

PostgreSQL will hold authoritative mutable state, leases, journals, snapshots, and
transactional outboxes. Process memory is not authority.

The Milestone 3 durability checkpoint applies this decision to character location and
simulation recovery:

- one serializable transaction changes the character station, active-ship inventory
  location, optimistic versions, and an idempotency receipt; serialization failures,
  deadlocks, and duplicate-delivery races receive a bounded three-attempt retry;
- a repeated command key returns the committed result, while reuse for another
  command is rejected;
- versioned solar-system snapshots contain deterministic ship position and velocity
  state, a fencing epoch, tick, sequence, and payload hash;
- Worker reconciles a snapshot with authoritative in-space character and inventory
  rows before it reports ready. Missing ships are rebuilt only from a matching
  committed undock receipt and configured entry point;
- an acknowledged dock, undock, or velocity change is checkpointed before success is
  returned. Ordinary movement ticks can roll back only to the most recent periodic
  checkpoint, currently ten seconds;
- corrupt, conflicting, or newer-epoch recovery data fails startup instead of
  silently starting an empty system.

This is atomic local commit plus idempotent retry, not an “exactly once” claim.
