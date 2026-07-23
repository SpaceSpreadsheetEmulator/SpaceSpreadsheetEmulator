# ADR 0003: Single-writer solar systems

Status: accepted

Exactly one Worker may authoritatively mutate a solar system for an ownership epoch.
Every mutation is fenced by that epoch. Placement, leases, and migration are later
milestones; the foundational runtime contract already carries the immutable context.
