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
| Attachments | Integration/negative | Streaming hash, size cap, type allowlist, partial cleanup |
| API | HTTP integration | Health, bounds, strict JSON, duplicate replay, cursor pull |
| Concurrency | Integration | Twelve simultaneous stale writers; exactly one acceptance |
| Performance | Budget | 100 transactional writes, prefix search, allocation ceiling |
| Platforms | Build smoke | Release Android APK inputs and unpackaged Windows x64 client |
| Security | Static/dependency | CodeQL, NuGet audit, dependency review, secret/content scan |

## Coverage policy

The CI test job records line and branch coverage through Coverlet. The release gate requires at
least 80% line coverage across the platform-neutral source under test. MAUI generated/platform code
is excluded from that numeric claim and instead receives platform compilation plus the documented
manual checklist.

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
- The server reference store is intentionally volatile, so database failover is out of scope.
- Network shaping is represented by deterministic exceptions, not radio-level mobile simulation.
- Attachment resumption is not implemented, so resumable-upload chaos tests would be premature.
