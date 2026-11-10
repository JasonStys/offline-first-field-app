# Offline-First Field App handbook

By Jason Stys.

Resolve base/local/server conflicts using explicit choices for divergent fields, stale-version checks and atomic replacement of queued writes.

Resolve base/local/server conflicts using explicit choices for divergent fields, stale-version checks and atomic replacement of queued writes.

Makes offline editing recoverable without silently overwriting another person's changes or fabricating a missing base.

## Read the documentation

| Edition | Audience and contents |
| --- | --- |
| [project-guide.docx](reports/project-guide.docx) | User and developer guide: purpose, current usage, design, boundaries and source references. |

The editable Markdown remains the maintenance source. The Word files are readable editions. See [publication identities and review](reports/publications.json) and the [documentation refresh record](reports/documentation-refresh-2026-11-09.md).

## Current usage and help

[README quick start](../README.md) is the entry point. Use the project-specific guides below before changing inputs, operating modes or deployment assumptions.

### Usage and operations

- [operations](operations.md)

### Design and contracts

- [adr/0001 transactional outbox and versioned sync](adr/0001-transactional-outbox-and-versioned-sync.md)
- [architecture](architecture.md)
- [complexity](complexity.md)
- [data model](data-model.md)
- [research](research.md)

### Security and validation

- [security](security.md)
- [testing](testing.md)

## Historical results and reproduction

The reviewed source is [`c5471bc43c1a6296c785aeab40310ae227b2d295`](https://github.com/JasonStys/offline-first-field-app/tree/c5471bc43c1a6296c785aeab40310ae227b2d295). The observed functional workflow baseline is `c5471bc43c1a6296c785aeab40310ae227b2d295`; those observations are recorded at `2026-11-10T05:25:40.628667+00:00`. They do not certify an unrun later revision.

- [Detailed recorded validation](reports/practical-workflows-2026-11-09.md)
- [Current Actions](https://github.com/JasonStys/offline-first-field-app/actions) records later integration checks separately.
- Preserve dated measurements and follow the existing testing guide to reproduce them; a test-source inventory alone does not establish execution.

## Practical boundaries

- Isolated synthetic development API with no authentication or tenant separation; not a hosted multi-user field service.
- Local SQLite is unencrypted. Older drafts may lack a base revision, which the resolver does not fabricate.
- Conflict review shows at most 100 entries from a 200-item bounded store API.
- MAUI compilation and portable view-model journeys are not real-device GUI validation; no updated native release accompanies this upgrade.

## Maintaining these editions

Update the editable usage/design/test documents first. Rebuild and visually review every Word page and workbook tab before replacing an edition; verify the exact binary digests and evidence source in publications.json. Keep account data, logs with secrets, installed-app state and private test stores out of documentation. Source and release assets retain their existing security and licensing boundaries.
