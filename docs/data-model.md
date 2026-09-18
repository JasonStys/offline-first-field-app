# Data model and SQL

## Local tables

| Table | Purpose | Key constraints/indexes |
| --- | --- | --- |
| `inspections` | Current local snapshot | Text/enum/version checks; primary-key ID; title index |
| `outbox_commands` | Durable outbound work | Unique mutation ID; state/sequence index; record foreign key |
| `queue_guard` | Constant-time durable queue bound | Singleton count and 10,000-command check |
| `conflicts` | Local and remote snapshots awaiting review | Mutation ID primary key |
| `sync_checkpoint` | Last fully applied pull cursor | Singleton check and non-negative cursor |
| `schema_history` | Applied schema versions | Integer primary key |

Migration `001_initial.sql` is embedded in `FieldOps.Infrastructure`, runs idempotently, enables
foreign keys and WAL, and records version 1. All application values use parameters; no external text
is concatenated into SQL.

## Transaction boundaries

- Saving a draft writes `inspections` and `outbox_commands` in the same transaction.
- Insert/delete triggers maintain `queue_guard`; inserts fail atomically once 10,000 commands exist.
- Selecting work and changing it from pending to in-flight occurs in one transaction.
- Accepting a push updates the local server version and removes its command atomically.
- Recording a conflict stores both snapshots and changes queue state atomically.
- Applying a pull page updates eligible records and advances the cursor atomically.

## Version semantics

`Version` is the last server version accepted by the client, not a wall-clock timestamp. Local edits
retain that version and send it as `ExpectedVersion`. The server increments versions only after an
accepted mutation. Timestamps are display/audit metadata and never decide conflict winners.

## Query behavior

- Record identity uses the SQLite primary-key B-tree: expected `O(log n)` lookup.
- Pending queue selection uses `(state, sequence)`: expected `O(log n + k)` for batch size `k`.
- Title prefixes use the title B-tree and escape SQLite GLOB metacharacters (`[`, `*`, and `?`);
  maximum results are 200.
- Each batch operation is `O(k)` with `k ≤ 100` or `k ≤ 200`.
- Three-way merge is `O(f)` time and space where `f` is four fixed mergeable fields.

The [performance report](reports/performance.md) records measured local budgets. Query plans should
be rechecked after any schema or search change.
