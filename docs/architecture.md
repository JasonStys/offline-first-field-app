# Architecture

## Context and goals

Field work cannot assume a stable network. The client must accept validated work offline, preserve
it through process termination, explain synchronization outcomes, and avoid silent loss when two
writers edit the same record. This repository optimizes for inspectability and correctness at a
portfolio scale rather than global multi-tenant throughput.

## Components

```text
FieldOps.App (MAUI)
  │ calls
  ▼
FieldOps.Infrastructure ───────► SQLite database + attachment directory
  │ implements                       │
  │ ILocalFieldStore                 ├── inspections
  │ IRemoteSyncGateway               ├── outbox_commands
  │                                  ├── conflicts
  │                                  └── sync_checkpoint
  ▼ HTTP JSON
FieldOps.Api ──────────────────► SyncServerStore
  │                                  ├── authoritative records
  │                                  ├── idempotency results
  │                                  └── ordered cursor changes
  ▼ shares
FieldOps.Core (domain values, limits, contracts, merge policy)
```

`FieldOps.Demo` replaces HTTP with a deterministic gateway so an evaluator can observe the whole
offline-to-online lifecycle in under a minute. `FieldOps.RepositoryChecks` keeps documentation and
source metadata verifiable.

## Write data flow

1. The MAUI view model creates a client-generated ID and validates every field.
2. `LocalFieldStore.SaveDraftAsync` starts one SQLite transaction.
3. The transaction upserts the local snapshot and appends an idempotent outbox mutation.
4. The UI confirms local durability; a network is not required.
5. Synchronization marks a bounded pending batch in-flight transactionally.
6. The API replays an existing mutation result or checks `ExpectedVersion`.
7. Accepted records receive the next server version and cursor; conflicts return the server copy.
8. The client applies each result explicitly, then pulls a bounded delta page.

## Failure and recovery

| Failure | Behavior | Invariant |
| --- | --- | --- |
| Process stops after marking in-flight | Next sync returns in-flight rows to pending | No command is stranded |
| HTTP request fails | Entire selected batch returns to pending with a bounded reason | Retry remains possible |
| Duplicate mutation arrives | Server returns its cached result | At-most-once logical effect |
| Server version changed | Mutation enters conflict and stores both snapshots | No silent overwrite |
| Remote delta meets pending local edit | Remote snapshot is not applied over local state | Local work stays visible |
| Attachment exceeds 5 MiB | Partial file is removed and the operation fails | Disk growth is bounded |

## Storage choices

SQLite fits a single-user offline client: transactions couple the cache and outbox, constraints
enforce field bounds below the object layer, WAL permits readers during writes, and deployment needs
no service. Direct `Microsoft.Data.Sqlite` commands keep SQL and transaction boundaries visible.

The server uses an in-memory store deliberately. It isolates and proves protocol semantics without
pretending that volatile memory is production durability. A production adapter would put records,
idempotency results, and the cursor log in one durable database transaction.

## Scale and reliability

- Pushes are capped at 100 mutations; pulls at 200 records; local searches at 200 rows.
- Attachments stream in 80 KiB buffers and stop at 5 MiB.
- Client connection timeouts are five seconds; retries are operator-triggered in version one.
- Server mutation lookup and record lookup are expected `O(1)` in the reference dictionary.
- The synchronization critical section is intentionally small but process-local.

## Trade-offs

- Direct SQL improves transparency and control but requires hand-maintained mapping.
- One local connection per operation is simpler and reliably releases mobile file handles; a proven
  hot path could add a serialized connection owner later.
- Numeric cursors are easy to demonstrate but become signed opaque tokens in a public protocol.
- Field-level merging preserves disjoint edits but still requires a user decision for dual text
  edits; automatic semantic text merge is intentionally excluded.
- MAUI provides a real cross-platform client but hosted UI automation requires a device farm.

## Growth triggers

Revisit the design when one client holds more than 100,000 records, attachments need background
resumption, multiple server replicas are required, tenants need authorization isolation, or the
cursor log needs retention/compaction. Those changes justify durable server storage, opaque signed
cursors, authentication, background scheduling, telemetry sampling, and load/chaos testing.
