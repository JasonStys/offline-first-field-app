# Observed native application validation — 2026-11-11

This report records local candidate observations, separate from pending hosted CI, a public release
and installation. The existing core/SQLite/API implementation remains in use. Synthetic profiles and
data were used; no installed user database, credentials, Docker service or global SDK was changed.

| Gate | Observed result | Boundary |
| --- | --- | --- |
| Platform-neutral Release build | Passed, zero warnings/errors | Existing local SDK 10.0.403 |
| Domain tests | 11 passed, zero skipped | Actual original contracts/merge/coordinator |
| SQLite/dashboard tests | 44 passed, zero skipped | Actual persistence, content search, exact visible export and authority safeguards |
| API tests | 21 passed, zero skipped | Includes actual owned loopback and durable checkpoint restart |
| Merged Coverlet line coverage | 94.00%, original 80% floor preserved | Core + Infrastructure + API; excludes native/generated MAUI code |
| Merged branch coverage | 74.43% | Observation, not a new pass threshold |
| Original deterministic conflict demo | Passed, final version 3 | Real SQLite and original protocol, network-free adapter |
| Original local budget | Passed | 100 writes: p95 9.264 ms; search 23.195 ms; 2,006,384 allocated bytes; synthetic one-machine measurement |
| Neutral NuGet audit | No vulnerable packages reported | Current configured NuGet source, point-in-time result |
| Real ZIP path/budget fixtures | 13 passed | Includes traversal/aliases/Windows names/expanded cap/no-overwrite |
| Original Android Release build | Passed, zero warnings/errors | Existing installed Android workload; compilation, not physical-device use |
| Self-contained .NET runtime | Actual 10.0.15 | Signed runtime/apphost NuGet archives and shipped DLL hashes checked |
| Actual native two-launch workflow | Passed, both exit 0 | Owned synthetic profile, actual MAUI controls/SQLite/HTTP, no runtime on PATH |

The full source test count is **76**, across three test assemblies. Those tests and their merged
coverage denominator are distinct from the actual native journey and the 13 PowerShell ZIP guards.
Hosted Windows native execution and both CodeQL configurations are recorded separately when their
actual runs complete; successful local Windows use does not certify another platform. The original
multi-platform MAUI restore remains platform/workload-resolved; the neutral solution and new explicit
Windows RID graph use separate locked dependency files.

## What the actual application did

The current local candidate creates an offline inspection, refuses an empty title, retains work
after an offline sync, explicitly starts its own real companion, accepts version 1, edits the same
row to version 2, and creates a genuine conflicting second writer. It refuses incomplete choices,
uses actual field picker controls, accepts the selected resolution at version 3, searches literal
notes, and writes a new JSON export while refusing an overwrite. The second process sees the exact
same two retained records and real restarted-server replay. Quit is invoked while a local operation
is deliberately held; the real window and exclusive profile lock remain until that operation
completes, followed by service/process exit and lock release.

The native tests also verify the actual generated MAUI storage identity remains the legacy identity,
and that the displayed text uses the intended light-surface contrast. Prepared examples stay within
the complete application rather than creating a separate demonstration executable.

## Genuine defects found during development

- Native launch originally constructed the page before application resources existed, causing an
  actual WinUI `StaticResource` parse failure. Resources now initialize before page construction.
- Dark system theme inherited unreadable editor colors on a light surface. Explicit text,
  placeholder and background colors now agree; actual native controls are checked.
- Programmatic MAUI close bypassed `AppWindow.Closing` and destroyed the window before retained work
  finished. Quit and system close now share the same completion gate before the real window closes.
- A new local server could otherwise be confused with an old remote authority. Explicit durable
  generation pairing and cursor checks now refuse a different/partial authority without resetting
  or rewriting the legacy accepted database.
- A nested collection did not reliably expose inspection rows in the actual render. The bounded
  row list now renders in the existing scroll surface with genuine per-row edit buttons.
- Export previously queried more rows than the visible list and could apply an unsubmitted search.
  It now exports exactly the displayed snapshot and applied filter. An independent 105-record SQLite
  test verifies 100 visible/exported rows, 53 filtered rows and preservation of all 105 source records.

Failed development probes are retained privately and are not reported as passing. A color-format
expectation and a network SYN liveness probe were test-oracle issues: they were corrected to the
actual color representation and OS listener table. No pass threshold or process deadline was raised.

## Honest evidence limits

Development candidates were built before a final source commit and explicitly marked dirty; exact
clean-source package provenance and hosted acceptance are subsequent gates. This is unsigned Windows
x64, not a clean-machine, mobile-device or assistive-technology certification. The native trust model
is one OS owner, with plaintext SQLite/checkpoint data and no authenticated enterprise actors. The
standalone API retains its original volatile default; only explicit native companion mode adds a
durable checkpoint. A finite test deadline is a failing harness bound, not a promised universal
application shutdown deadline.

See [native usage and reproduction](../native-application.md), [security](../security.md), and
[source declaration locations](../code-index.md). Existing reviewed Word editions retain their
historical source identity until a separately rendered and reviewed edition replaces them.
