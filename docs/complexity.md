# Complexity and performance

Let `n` be stored records, `q` queued commands, `k` a bounded batch/page, `b` attachment bytes, and
`f` the four mergeable fields.

| Operation | Expected time | Extra space | Bound/reason |
| --- | ---: | ---: | --- |
| Append outbox command | `O(1)` amortized | `O(1)` plus payload | Auto-increment append; singleton capacity counter |
| Record by ID | `O(log n)` | `O(1)` | Primary-key B-tree |
| Select pending batch | `O(log q + k)` | `O(k)` | `(state, sequence)` index; `k ≤ 100` |
| Apply pull page | `O(k log n)` | `O(1)` per row | `k ≤ 200`, one transaction |
| Three-way merge | `O(f)` | `O(f)` | Fixed field count; conflicts listed |
| Server mutation lookup | `O(1)` expected | `O(1)` | Reference dictionaries |
| Attachment write/hash | `O(b)` | `O(1)` | 80 KiB buffer; `b ≤ 5 MiB` |
| Prefix search | `O(log n + k)` | `O(k)` | Title B-tree; result cap 200 |

## Budgets

- 100 local transactional writes: p95 no more than 250 ms on a hosted CI runner.
- Prefix search over that fixture: no more than 500 ms.
- Managed allocation during the fixture: no more than 64 MiB.
- API request body: one MiB.
- Client HTTP timeout: five seconds.

These are regression tripwires, not mobile performance guarantees. Results vary by device, storage,
antivirus, and build mode. The [performance report](reports/performance.md) records the measured
baseline and environment.
