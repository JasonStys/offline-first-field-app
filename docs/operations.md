# Operations and recovery

## Local API

```bash
dotnet run --project src/FieldOps.Api --urls http://127.0.0.1:5080
curl http://127.0.0.1:5080/healthz
```

Keep the service on loopback. The reference server has no authentication or durable state.

## MAUI builds

Windows x64:

```powershell
dotnet workload restore src/FieldOps.App/FieldOps.App.csproj
dotnet build src/FieldOps.App/FieldOps.App.csproj -c Release `
  -f net10.0-windows10.0.19041.0 -r win-x64 --no-self-contained
```

Android:

```bash
dotnet workload restore src/FieldOps.App/FieldOps.App.csproj
dotnet build src/FieldOps.App/FieldOps.App.csproj -c Release -f net10.0-android
```

The Android default endpoint uses emulator host alias `10.0.2.2` over HTTPS. Trusting a development
certificate is an explicit local setup step; the application does not bypass certificate checks.

## Crash-recovery drill

1. Save a generated draft offline.
2. Start synchronization and terminate the process after batch selection.
3. Restart and synchronize.
4. Confirm `RecoverInterruptedCommandsAsync` reports the in-flight row and returns it to pending.
5. Confirm the server accepts it once or replays its cached idempotent result.
6. Confirm the local command is removed only after an applied response.

The automated database test performs the same state transition without relying on timing.

## Backup and restore drill

Version one has only client-side durable state. For a consistent development backup:

1. Stop the MAUI app so no connection is active.
2. Copy `fieldops.db`, `fieldops.db-wal`, `fieldops.db-shm`, and the attachment directory together.
3. Restore them together into a new app-data directory.
4. Start the app, run migrations, inspect local history, and synchronize.

Never copy only the main database while WAL mode is active. This repository does not claim a
production backup system for the intentionally volatile reference server.

## Observability

- Client status distinguishes applied, conflicted, rejected, pulled, offline, and timeout outcomes.
- Queue errors contain only bounded exception type/code information.
- `/api/diagnostics` returns counts and current cursor.
- Structured ASP.NET Core console logs omit record bodies by application design.

## Release procedure

1. Update `CHANGELOG.md` and validation evidence.
2. Run core verification and both platform builds.
3. Run NuGet vulnerability checks and inspect CodeQL/dependency review.
4. Publish the API and verify `/healthz` from the artifact.
5. Create an annotated version tag only after the exact commit is green.

Rollback means returning to the prior tag and its compatible database schema. Version-one migration
is additive and has no down migration; destructive rollbacks require a verified backup restore.
