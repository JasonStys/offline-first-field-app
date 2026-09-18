# Synchronization protocol

## Endpoints

### `POST /api/sync/push`

Accepts one to 100 mutations. Each mutation includes:

- `mutationId`: client-generated idempotency key.
- `deviceId`: bounded diagnostic identifier, not a credential.
- `expectedVersion`: server version last observed by the client.
- `record`: validated complete inspection snapshot.

Each result is independent:

| Code | Meaning | Client action |
| --- | --- | --- |
| `applied` | Version matched and mutation was stored | Update local version; remove command |
| `conflict` | Server has a different version | Persist both copies; require review |
| `invalid_mutation` | Envelope failed validation | Reject and explain locally |
| `invalid_record` | Snapshot failed validation | Reject and explain locally |
| `unknown_record_version` | Client referenced a missing nonzero version | Reject; refresh/review |

A duplicate `mutationId` returns the original result. This makes transport retries idempotent.

### `GET /api/sync/pull?after={cursor}&limit={count}`

Returns ordered changes strictly after `cursor`, at most 200 records, plus `nextCursor`. A client
applies the page and cursor in one transaction. Version one uses a numeric cursor for clarity; it is
not a promise that production consumers may interpret its value.

### `GET /healthz`

Returns process health only. It does not claim dependency readiness.

### `GET /api/diagnostics`

Returns non-sensitive counts for records, idempotency entries, changes, and cursor. It never returns
notes, attachment names, device details, or exception text.

## Conflict policy

The three-way merge compares baseline, local, and remote values per field:

1. If only one side changed, use that side.
2. If both sides changed to the same value, use that value.
3. If both sides changed differently, return the field name and no merged record.

User-authored text therefore never uses silent last-write-wins. A future resolution screen can show
both snapshots and submit a new mutation at the current remote version.

## Compatibility

Contracts are version-one portfolio contracts. Adding optional fields is safe only after client JSON
behavior is considered; the reference API currently rejects unknown fields to expose skew early.
Breaking field or meaning changes require a new route version and an ADR.
