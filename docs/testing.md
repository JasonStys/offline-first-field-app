# Testing strategy

## Risk model

Highest risks are lost offline work, duplicate logical effects, silent conflict overwrite, broken
migrations, partial transaction commits, unbounded input/resource use, unsafe file handling, API
contract drift, and platform-specific build breakage.

## Test layers

```text
             MAUI Android + Windows release builds
          HTTP integration and concurrency tests
       SQLite migration / recovery / attachment tests
    domain, validation, merge, and coordinator unit tests
 formatting / analyzers / repository policy / dependency audit
```

| Area | Test type | Representative evidence |
| --- | --- | --- |
| Domain validation | Unit/negative | Empty IDs, lengths, enum, version, UTC |
| Merge policy | Unit/property-style | Disjoint changes, dual text edit, 1,000 seeded cases |
| Coordinator | Unit/contract | Applied/conflict/reject routing and failed-transport retry |
| SQLite | Integration | Idempotent migration, atomic outbox, crash recovery, wildcard escaping |
| Conflict review | User journey/integration | Real SQLite + portable MAUI view model: cancel, divergent choices, empty notes, stale evidence, fresh server conflict, legacy migration |
| Attachments | Integration/negative | Streaming hash, size cap, type allowlist, partial cleanup |
| API | HTTP integration | Health, bounds, strict JSON, duplicate replay, cursor pull |
| Concurrency | Integration | Twelve simultaneous stale writers; exactly one acceptance |
| Performance | Budget | 100 transactional writes, prefix search, allocation ceiling |
| Platforms | Build + actual native journey | Android compilation, self-contained Windows archive relocation, actual controls/HTTP/restart/Quit |
| Security | Static/dependency | CodeQL, locked NuGet audit, secret/content scan |

## Coverage policy

The CI test job records line and branch coverage through Coverlet. The release gate requires at
least 80% line coverage across the platform-neutral source under test. MAUI generated/platform code
is excluded from that numeric claim and instead receives platform compilation plus the documented
manual checklist and a separate actual Windows control/SQLite/HTTP journey. No GUI code is added to
the numeric coverage denominator to inflate its result.

## Commands

```bash
dotnet format FieldOps.CI.slnx --verify-no-changes --no-restore
dotnet build FieldOps.CI.slnx -c Release --no-restore
dotnet test FieldOps.CI.slnx -c Release --no-build
dotnet run --project src/FieldOps.Demo -c Release --no-build -- --benchmark
```

Platform commands and recovery drills are in [operations](operations.md). Exact results are in
[validation.md](reports/validation.md).

## Known test gaps

- No physical-device or Appium run is claimed; the checklist remains manual.
- The standalone server remains volatile. Native checkpoint persistence/restart is tested;
  distributed database failover and production authenticated-device recovery remain out of scope.
- Network shaping is represented by deterministic exceptions, not radio-level mobile simulation.
- Attachment resumption is not implemented, so resumable-upload chaos tests would be premature.

The view-model journey tests compile the actual portable dashboard and field-choice source into the
infrastructure test project without introducing a MAUI runtime dependency. They verify the same
commands/bindings used by the screen, but do not claim native screen rendering or device interaction.
New observed local results are in [the workflow report](reports/practical-workflows-2026-11-09.md).

## Self-contained Windows application gate

The [native guide](native-application.md) reproduces the complete ZIP guards, signature/runtime-byte
verification and two actual EXE launches. It invokes real MAUI controls in an explicitly marked
synthetic profile before normal AppData is evaluated. The acceptance checks empty-input refusal,
offline retry, version-preserving editing, real second-writer conflict, missing-choice refusal,
actual picker bindings, literal notes search, new-file export, restart and a held-operation Quit.
The outer process deadline fails the test rather than passing a forced shutdown. Released locks,
process exit and the actual OS listener table are checked separately.

The original Linux CodeQL job/configuration remains present. A separate Windows manual-build
configuration extracts the actual MAUI/WinUI C# and referenced companion rather than claiming the
Linux server build covers Windows-only classes. Native CI retains bounded receipts/screenshots;
full unsigned packages require explicit trusted-main opt-in. Current local measurements and hosted
boundaries are in [the native report](reports/native-application-2026-11-11.md).
