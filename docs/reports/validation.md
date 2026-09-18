# Validation report

## Scope

Release `1.0.0` was validated locally on September 18, 2026 with .NET SDK 10.0.401 and runtime
10.0.12. Package versions are centrally pinned and represented by committed lock files.

## Local results

| Gate | Result |
| --- | --- |
| Restore and locked dependency graph | Pass |
| `dotnet format` | Pass |
| Release build with warnings as errors | Pass: zero warnings, zero errors |
| Core tests | Pass: 11 |
| SQLite/transport tests | Pass: 18 |
| HTTP/API tests | Pass: 11 |
| Merged coverage | Pass: 93.59% lines, 73.66% branches, 84.25% methods |
| Deterministic offline/sync/conflict scenario | Pass |
| Local performance/allocation budget | Pass |
| Windows MAUI x64 release build | Pass |
| Android MAUI release build | Pass |
| Repository headers, links, policy, action pins | Pass |
| NuGet vulnerability audit | Pass: no known vulnerable packages reported |
| API publish | Pass |
| Non-root container and HTTP smoke | Pending hosted workflow |
| GitHub Actions CI and CodeQL | Pending publication |

## Commands

```bash
./scripts/verify.sh
```

Windows platform verification uses `scripts/verify.ps1`. Generated coverage artifacts are excluded
from Git but uploaded by CI. The machine-readable release summary is
[`generated/validation.json`](generated/validation.json).

## Claim boundary

Successful automation does not establish production readiness, security certification, or complete
mobile accessibility. The [manual accessibility checklist](manual-accessibility-checklist.md) is
intentionally separate and remains unsigned until physical/emulated assistive-technology runs are
recorded.
