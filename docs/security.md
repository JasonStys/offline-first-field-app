# Security model

## Assets and trust boundaries

Assets are offline inspection text, generated attachment files, queue state, and sync integrity.
Untrusted boundaries are JSON requests/responses, local database rows, file streams/names, query
text, and process/network interruption.

## Implemented controls

- Runtime validation repeats below static typing at network and persistence boundaries.
- One-MiB HTTP request, 100-mutation push, 200-record pull/search, 4,000-character notes, and 5-MiB
  attachment limits bound resource use.
- All SQL uses parameters; database constraints independently enforce important field bounds.
- Attachment names are reduced to `Path.GetFileName`; stored names are generated IDs.
- Attachments accept only synthetic JPEG/PNG types, stream to `.partial`, hash incrementally, flush,
  then rename; failures remove partial files.
- Errors return generic problem details and queue reasons are capped at 500 characters.
- Diagnostics expose counts, not inspection text or stack traces.
- Android backups and cleartext traffic are disabled.
- Actions use least permissions and immutable commit pins; CodeQL and locked NuGet auditing are
  enabled.

## Deliberate non-controls

The standalone reference API has no identity, authorization, tenant separation or durable server
database by default. It is for loopback/isolated development only and must not be publicly exposed.
Local SQLite and attachments are not application-level encrypted; production would use platform
data protection, secure key storage, and an explicit retention policy.

## Native companion boundary

The Windows app starts offline and explicitly owns a fresh literal-loopback service. Its volatile
32-byte capability is required on requests; Host must match the actual bound port, and an Origin
header, if present, must match that origin. The client refuses redirects/proxies. The listener has
strict JSON, connection/body/header bounds and no external provider integration. The capability is
not an authenticated user or tenant identity, and another process running as the same OS owner can
read local memory/files.

Native client/companion profiles reject linked ancestors/state, retain exclusive owner locks and
preserve corrupt or unidentified state rather than resetting it. Atomic checkpoint commits restore
in-memory state on persistence failure. A durable generation pairing plus cursor check refuses an
unrelated/partial server restoration before synchronization. These checks are accidental-mixing and
file-safety safeguards, not tamper-proof attestation against the same OS owner. See
[native data, backup and bounds](native-application.md).

## Threats to revisit

Before production: authenticated devices, revoked credentials, object-level authorization, signed
opaque cursors, TLS pinning policy, secure local secrets, encrypted attachments, malware scanning,
audit retention, rate limits, durable idempotency, server database backups, and tenant isolation.

See [SECURITY.md](../SECURITY.md) for reporting and [limitations](limitations.md) for claim boundaries.
