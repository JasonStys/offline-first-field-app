# Offline-First Field App

[![CI](https://github.com/JasonStys/offline-first-field-app/actions/workflows/ci.yml/badge.svg)](https://github.com/JasonStys/offline-first-field-app/actions/workflows/ci.yml)
[![CodeQL](https://github.com/JasonStys/offline-first-field-app/actions/workflows/codeql.yml/badge.svg)](https://github.com/JasonStys/offline-first-field-app/actions/workflows/codeql.yml)

A .NET 10 and C# reference application for capturing synthetic field inspections without
connectivity, preserving them transactionally in SQLite, and synchronizing them through a bounded,
idempotent ASP.NET Core API when a network becomes available.

The project is designed for software, application, full-stack, and reliability engineering review.
It emphasizes explicit failure states and evidence: offline work is durable, transport failures
return commands to a retryable state, version conflicts require review, and no timestamp silently
chooses a winner for user-authored text.

**FieldOps Offline 1.1.0** adds a self-contained Windows application using these real components:
edit accepted inspection rows, search notes, export visible data, prepare a real two-technician
conflict, and restart the owned durable local companion without losing accepted versions. The
application starts offline and explains its single-OS-owner boundary in **Help & privacy**.
See [native usage, screenshots and reproduction](docs/native-application.md) and
[observed native validation](docs/reports/native-application-2026-11-11.md). Packaging and local
candidate validation are implemented; a public download and PC installation require separate
exact-source acceptance.

## Sixty-second quickstart

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet restore FieldOps.CI.slnx
dotnet test FieldOps.CI.slnx --configuration Release
dotnet run --project src/FieldOps.Demo --configuration Release
```

The deterministic demo creates a record offline, writes it and its outbox command in one SQLite
transaction, synchronizes it, then simulates a second writer, records a text conflict, explicitly
chooses fields, and synchronizes a fresh resolution to version 3 without requiring a running server.

Run the API separately:

```bash
dotnet run --project src/FieldOps.Api --urls http://127.0.0.1:5080
```

For the complete Windows package, extract every file and open `FieldOps.App.exe`; no development
runtime or IDE is required. To build from source, use the [native build guide](docs/native-application.md).
On Windows with the MAUI workload, developers can also launch `src/FieldOps.App/FieldOps.App.csproj`. Android source is
also compiled in CI; connecting an emulator requires trusting a development HTTPS certificate as
described in [operations](docs/operations.md).

## What the repository demonstrates

- A real .NET MAUI client targeting Android and Windows with semantic labels, large controls,
  screen-reader announcements, responsive layout, and complete offline draft capture.
- Direct parameterized SQLite access, WAL journaling, constraints, migrations, transactions,
  bounded queries, escaped GLOB-prefix search, and a durable outbox capped at 10,000 commands.
- Constant-time queue-capacity enforcement, an indexed check preventing overlapping drafts, indexed
  `O(log n)` identity lookup, `O(k)` bounded batch handling, and `O(f)` three-way field merge where
  `f` is fixed.
- Idempotency keys, optimistic versions, ordered delta cursors, duplicate replay, partial outcomes,
  crash recovery, bounded attachments, and redacted diagnostics.
- A small ASP.NET Core API with strict JSON, one-MiB request limits, safe problem responses, and
  deterministic concurrent-write behavior.
- Unit, property-style, database, integration, concurrency, negative, performance, and platform
  build verification.

## Architecture

```text
┌──────────────────── Android / Windows MAUI client ────────────────────┐
│ Dashboard → validated snapshot → SQLite transaction                   │
│                                  ├── inspection cache                 │
│                                  └── durable outbox (pending)         │
└──────────────────────────────────────────────┬─────────────────────────┘
                                               │ bounded push / delta pull
                                               ▼
┌──────────────────────── ASP.NET Core sync API ────────────────────────┐
│ strict JSON → idempotency lookup → version check → accept/conflict    │
│                                              └── ordered cursor log    │
└────────────────────────────────────────────────────────────────────────┘

failure: in-flight → pending + reason     conflict: no silent overwrite
```

See [architecture](docs/architecture.md), [data model](docs/data-model.md), and
[sync protocol](docs/sync-protocol.md) for invariants and trade-offs.

## Major features

| Feature | Behavior |
| --- | --- |
| Offline inspection capture | Validates and stores a record plus outbox mutation atomically |
| Crash recovery | Resets interrupted in-flight commands to pending on the next sync |
| Bounded synchronization | Pushes at most 100 and pulls at most 200 records per request |
| Conflict review | Shows original/local/remote field evidence, requires choices for divergent edits, and queues a fresh version-checked resolution offline |
| Duplicate delivery | Replays the original result by mutation ID without creating a second change |
| Attachments | Streams JPEG/PNG fixtures, caps them at 5 MiB, and records SHA-256 integrity |
| Diagnostics | Reports non-sensitive counts and explicit applied/conflicted/rejected totals |
| Local search | Native literal title/notes search is bounded to 100 displayed rows; the original indexed prefix API remains available |
| Native local companion | Explicitly owns its fresh loopback service, durable records/idempotency checkpoint and generation pairing |
| Native editing and export | Edits accepted rows without changing identity; exports visible rows to a new bounded JSON file |
| Completion-based Quit | Real local operations and owned service drain before window/profile ownership is released |

### Resolve an inspection conflict

After **Sync now** reports a conflict, select it in **Resolve synchronization conflicts**. Review the recorded original, your local value and the server value for each field. Disjoint changes are preselected; fields changed differently by both writers require an explicit local/remote choice. Empty notes can be selected deliberately. **Cancel review** leaves both copies untouched. **Save resolution offline** replaces the blocked command atomically with a new idempotency key and the reviewed server version. Sync it when connected; a newer server edit produces another review rather than being overwritten. Older pending drafts with no recorded base display that fact and never invent previous content.

This remains an isolated synthetic reference app: the native service uses an ephemeral capability
and strict loopback/Host/Origin checks, but has no authenticated enterprise actors, tenant separation
or encrypted local storage. The standalone API retains its original development behavior. See the
[security model](docs/security.md) before using any real data.

## Verification

```bash
./scripts/verify.sh
# Windows PowerShell:
./scripts/verify.ps1
```

The verification sequence restores a locked dependency graph, checks formatting, builds with
warnings treated as errors, runs all tests with coverage, executes the deterministic scenario and
performance budget, publishes the API, validates repository policy, and confirms the generated code
index. MAUI platform builds run in dedicated GitHub Actions jobs.

Current exact results are in [validation.md](docs/reports/validation.md), with the machine-readable
summary in [validation.json](docs/reports/generated/validation.json). Automated accessibility checks
do not replace the [manual checklist](docs/reports/manual-accessibility-checklist.md).
The latest offline review changes and observed local results are in the
[2026-11-09 workflow report](docs/reports/practical-workflows-2026-11-09.md); native platform CI is
tracked separately from the portable view-model tests.

## Repository map

| Path | Purpose |
| --- | --- |
| `src/FieldOps.Core/` | Domain values, sync contracts, and deterministic merge policy |
| `src/FieldOps.Infrastructure/` | SQLite store, streaming attachment store, HTTP gateway, coordinator |
| `src/FieldOps.Api/` | Strict reference synchronization API and thread-safe server semantics |
| `src/FieldOps.App/` | Android/Windows .NET MAUI client and accessible dashboard |
| `src/FieldOps.Demo/` | Network-free scenario and local performance budget |
| `tests/` | Unit, database, API, concurrency, negative, and property-style tests |
| `tools/FieldOps.RepositoryChecks/` | Code-index generator and repository policy validation |
| `.github/workflows/` | Least-privilege CI and CodeQL analysis |
| `docs/` | Architecture, protocol, operations, security, limitations, ADRs, and evidence |

[The file catalog](docs/file-catalog.md) summarizes every authored file group. The generated
[code index](docs/code-index.md) records declaration line locations.

## Documentation

- [Architecture](docs/architecture.md)
- [API and sync protocol](docs/sync-protocol.md)
- [Data model and SQL](docs/data-model.md)
- [Testing strategy](docs/testing.md)
- [Security model](docs/security.md)
- [Operations and recovery](docs/operations.md)
- [Native application usage and screenshots](docs/native-application.md)
- [Accessibility](docs/accessibility.md)
- [Complexity and performance](docs/complexity.md)
- [Limitations](docs/limitations.md)
- [Research references](docs/research.md)
- [Validation evidence](docs/reports/validation.md)

## Safety and scope

All records and images are generated fixtures. The repository contains no location, biometric,
medical, customer, or employer data. The API is a local reference service without authentication;
it must not be exposed publicly. Review [SECURITY.md](SECURITY.md) and the documented
[limitations](docs/limitations.md) before reuse.

## Roadmap

1. Add an authenticated production adapter and encrypted platform secret storage.
2. Extend the implemented conflict-resolution screens with real-device usability and accessibility validation.
3. Add signed resumable attachment upload and background platform scheduling.
4. Add Appium device-farm automation after a hosted device environment is selected.

## License

[MIT](LICENSE)

## Documentation editions

Start with the [organized handbook](docs/handbook.md) for current usage, design, security and test guidance. Reviewed downloadable editions: [project-guide.docx](docs/reports/project-guide.docx).

[Publication identities and source scope](docs/reports/publications.json) · [Documentation validation record](docs/reports/documentation-refresh-2026-11-09.md). Historical results retain their own tested revision and date.
