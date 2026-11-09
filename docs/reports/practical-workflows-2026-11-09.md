# Offline conflict review verification — 2026-11-09

The MAUI dashboard now provides original/local/server field evidence, conservative three-way recommendations, required choices for divergent changes, cancel without writes, and an offline save that atomically queues a fresh idempotent mutation at the reviewed server version. A stale review is refused and reloaded. A server edit after resolution produces another conflict rather than a force write.

An additive migration records original snapshots for new mutations. Existing queued work is preserved, with unknown baselines labeled unavailable. Record/state indexing prevents overlapping unsynchronized drafts. Resolved commands are replaced in one SQLite transaction, preserving the existing capacity bound. An injected insert-failure regression demonstrates rollback of command deletion, conflict removal and snapshot changes.

## Observed local evidence

On Windows with .NET SDK 10.0.403, the locked restore, formatter, Release build (zero warnings/errors), 57 tests (11 core, 35 infrastructure/portable-view-model, 11 API), API publication, repository checks and code-index check passed. The merged platform-neutral coverage gate passed at 94.61% lines and 77.27% branches (line threshold 80%). The actual portable MAUI view-model source is exercised by the journey tests; generated/native UI is outside that coverage claim.

The console demonstration completes offline create -> server acceptance -> independent second writer -> conflict -> explicit field selection -> new mutation -> accepted version 3. The existing 100-write performance guard passed: 7.757 ms write p95, 23.386 ms search and 2,006,656 allocated bytes in the recorded run. These timings depend on host load. The locked NuGet audit reported no vulnerable packages from its current sources.

Journeys cover incomplete choices, cancel, disjoint remote changes, explicit empty notes, missing baseline, legacy migration, stale evidence reload, renewed server conflict, wrong identity, duplicate pending edits, bounded conflict queries, cancellation, invalid choices and transactional rollback.

## Practical limits

No native device interaction or Android/Windows MAUI platform build was performed locally; those remain required in the existing dedicated CI jobs. XAML was checked as well-formed XML, which does not replace MAUI compilation. No GitHub Actions were triggered by this subtask.

This remains a synthetic isolated development reference. Its server has no authentication, authorization, tenancy or durable server database, and local files are not application-encrypted. The update adds no network exposure and makes no production identity claim. No personal records or live device connections were used.

Machine-readable results: [practical-workflows-2026-11-09.json](generated/practical-workflows-2026-11-09.json).
