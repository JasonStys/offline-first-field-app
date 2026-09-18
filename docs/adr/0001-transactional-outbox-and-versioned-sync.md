# ADR 0001: Transactional outbox with versioned synchronization

- Status: Accepted
- Date: 2026-09-18

## Context

Offline edits must survive termination and later synchronize without duplicate effects or silent
overwrites. A network call cannot participate in the local SQLite transaction.

## Decision

Store the local snapshot and an idempotent mutation in one SQLite transaction. Synchronization
claims a bounded batch, sends client mutation IDs and expected versions, records every result
explicitly, and pulls ordered server deltas after a cursor. Concurrent version changes become
conflicts carrying both snapshots. Disjoint fields can be merged deterministically; divergent text
requires user review.

## Consequences

- Local durability does not depend on connectivity.
- At-least-once transport becomes at-most-once logical mutation through idempotency replay.
- Storage and protocol state are more explicit than a simple last-write-wins CRUD design.
- The server must durably retain idempotency results in a production implementation.
- Queue compaction and cursor retention policies become necessary at larger scale.

## Alternatives rejected

- Last-write-wins by timestamp: vulnerable to clock skew and silent user-text loss.
- Immediate remote write before local commit: unavailable offline and creates ambiguous partial state.
- Whole-record automatic merge: hides divergent edits and cannot explain its decisions.
