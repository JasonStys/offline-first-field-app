# File catalog

| File or group | Responsibility |
| --- | --- |
| `Directory.Build.props` | Strict language, analyzer, deterministic-build, and warning policy |
| `Directory.Packages.props` | Exact centrally managed NuGet versions |
| `FieldOps.CI.slnx` | Platform-neutral build/test solution for Linux and Windows |
| `FieldOps.slnx` | Complete solution including the MAUI client |
| `src/FieldOps.Core/*.cs` | Validated domain types, merge policy, sync DTOs, and abstractions |
| `src/FieldOps.Infrastructure/Migrations/*.sql` | Idempotent local SQLite schema |
| `src/FieldOps.Infrastructure/LocalFieldStore.cs` | Transactional cache/outbox/conflict/cursor implementation |
| `src/FieldOps.Infrastructure/AttachmentFileStore.cs` | Bounded streaming attachment lifecycle |
| `src/FieldOps.Infrastructure/HttpSyncGateway.cs` | Strict HTTP transport adapter |
| `src/FieldOps.Infrastructure/SyncCoordinator.cs` | Recovery, push outcomes, pull, and retry state |
| `src/FieldOps.Api/*.cs` | HTTP composition, endpoints, safe errors, and server semantics |
| `src/FieldOps.App/*` | MAUI dashboard, view model, platform hosts, and visual resources |
| `src/FieldOps.Demo/*` | Deterministic recruiter scenario and performance budget |
| `tests/FieldOps.Core.Tests/*` | Validation, merge properties, and coordinator contracts |
| `tests/FieldOps.Infrastructure.Tests/*` | SQLite and attachment integration/negative tests |
| `tests/FieldOps.Api.Tests/*` | Full HTTP, idempotency, concurrency, and strict JSON tests |
| `tools/FieldOps.RepositoryChecks/*` | Generated line index and repository policy enforcement |
| `scripts/*` | Reproducible Unix/Windows verification entry points |
| `.github/workflows/*` | CI, platform builds, CodeQL, and dependency review |
| `docs/*` | Architecture, protocol, safety, operations, limitations, and evidence |

Exact C# declaration lines are generated in [code-index.md](code-index.md).
