# File catalog

| File or group | Responsibility |
| --- | --- |
| `Directory.Build.props` | Strict language, analyzer, deterministic-build, and warning policy |
| `Directory.Packages.props` | Exact centrally managed NuGet versions |
| `Directory.Build.targets` | Explicit native runtime/apphost pack pin without altering Windows SDK identity |
| `FieldOps.CI.slnx` | Platform-neutral build/test solution for Linux and Windows |
| `FieldOps.slnx` | Complete solution including the MAUI client |
| `src/FieldOps.Core/*.cs` | Validated domain types, merge policy, sync DTOs, and abstractions |
| `src/FieldOps.Infrastructure/Migrations/*.sql` | Idempotent local SQLite schema |
| `src/FieldOps.Infrastructure/LocalFieldStore.cs` | Transactional cache/outbox/conflict/cursor implementation |
| `src/FieldOps.Infrastructure/ConflictWorkflows.cs` | Bounded conflict reads and stale-checked atomic replacement mutations |
| `src/FieldOps.Core/ConflictReview.cs` | Immutable review evidence and validated explicit field-choice policy |
| `src/FieldOps.App/ConflictFieldChoice.cs` | Conservative three-way recommendations and required-choice bindings |
| `tests/FieldOps.Infrastructure.Tests/ConflictWorkflowTests.cs` | SQLite and portable MAUI review journeys, migration and stale-version regressions |
| `src/FieldOps.Infrastructure/AttachmentFileStore.cs` | Bounded streaming attachment lifecycle |
| `src/FieldOps.Infrastructure/HttpSyncGateway.cs` | Strict HTTP transport adapter |
| `src/FieldOps.Infrastructure/SyncCoordinator.cs` | Recovery, push outcomes, pull, and retry state |
| `src/FieldOps.Api/*.cs` | HTTP composition, endpoints, safe errors, and server semantics |
| `src/FieldOps.Api/ApiBootstrap.cs` | Shared original API and explicit capability/Host/Origin-bound loopback composition |
| `src/FieldOps.Api/NativeCheckpoint.cs` | Bounded strict checkpoint reads, atomic commit and validated complete state |
| `src/FieldOps.Api/NativeCompanion.cs` | Durable generation identity, exclusive ownership, actual Kestrel lifecycle and drain |
| `src/FieldOps.Infrastructure/NativeAuthority.cs` | Explicit client/server generation pairing and regressed-cursor refusal |
| `src/FieldOps.App/*` | MAUI dashboard, view model, platform hosts, and visual resources |
| `src/FieldOps.App/NativeRuntime.cs` | Explicit offline/owned-real-HTTP gateway, retained operation and profile ownership |
| `src/FieldOps.App/NativeLaunch.cs` | Select byte-marked synthetic acceptance profile before default AppData evaluation |
| `src/FieldOps.App/NativeAcceptance.cs` | Actual native controls, runtime/storage identity, HTTP, restart and pending-Quit journey |
| `src/*/packages.native-windows.lock.json` | Separate explicitly self-contained Windows RID dependency graphs |
| `src/FieldOps.Demo/*` | Deterministic recruiter scenario and performance budget |
| `tests/FieldOps.Core.Tests/*` | Validation, merge properties, and coordinator contracts |
| `tests/FieldOps.Infrastructure.Tests/*` | SQLite and attachment integration/negative tests |
| `tests/FieldOps.Api.Tests/*` | Full HTTP, idempotency, concurrency, and strict JSON tests |
| `tools/FieldOps.RepositoryChecks/*` | Generated line index and repository policy enforcement |
| `scripts/*` | Reproducible Unix/Windows verification entry points |
| `scripts/build-native.ps1` | Fresh self-contained package, NuGet/runtime byte verification, notices and source metadata |
| `scripts/portable-archive.ps1` | Complete ZIP and bounded no-overwrite/path-checked extraction |
| `scripts/archive-native.ps1` | SHA sidecar, archive relocation and actual consumer acceptance |
| `scripts/test-native.ps1` | Reduced-PATH two-process checks, exact retained records and released service/profile ownership |
| `tests/native-archive.tests.ps1` | Thirteen real ZIP path, expanded-budget and no-overwrite fixtures |
| `tests/FieldOps.Api.Tests/Native*Tests.cs` | Checkpoint corruption/commit failure and real owned listener/persistence boundaries |
| `tests/FieldOps.Infrastructure.Tests/Native*Tests.cs` | Literal search/export and real SQLite authority pairing/refusal |
| `.github/workflows/native-app.yml` | Actual self-contained Windows control journeys; bounded default artifact retention |
| `docs/native-application.md` | Operator workflow, screenshots, complete-profile recovery and build/runtime bounds |
| `docs/images/native-*.png` | Actual compiled WinUI surface captures using synthetic inspection data |
| `docs/reports/native-application-2026-11-11.md` | Observed candidate tests and defects, separate from hosted/release/installation claims |
| `.github/workflows/*` | CI, platform builds, and CodeQL analysis |
| `docs/*` | Architecture, protocol, safety, operations, limitations, and evidence |

Exact C# declaration lines are generated in [code-index.md](code-index.md).

## Reviewed documentation editions

| File | Responsibility |
| --- | --- |
| `docs/handbook.md` | Organized usage, architecture, security and evidence navigation |
| `docs/reports/project-guide.docx` | Reviewed source-bound Word guide with dated results and practical limits |
| `docs/reports/publications.json` | Exact publication hashes and page review identities |
| `docs/reports/documentation-refresh-2026-11-09.md` | Actual documentation integration checks and their scope |
