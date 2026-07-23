# ADR 0004: PostgreSQL as authoritative mutable storage

Status: accepted

PostgreSQL will hold authoritative mutable state, leases, journals, snapshots, and
transactional outboxes. It is documented but deliberately not provisioned in the
foundation milestones. Process memory is not future authority.
