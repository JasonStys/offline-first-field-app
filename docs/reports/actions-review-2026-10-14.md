# Actions maintenance review — 2026-10-14

Reviewed by Jason Stys. Runs 38458394515 and 38458394419 failed at locked NuGet
restore (NU1004), before application testing/CodeQL analysis. The proposed central
package updates changed SQLite 10.0.12 to 10.0.13, but the Demo project's transitive
lockfile remained at 10.0.12. Re-evaluating the complete platform-neutral solution
updated that missing graph. The fix is on the existing NuGet update branch; it
does not bypass locked restore in CI or merge unrelated updates automatically.

Local verification with .NET SDK 10.0.401: locked restore passed, release build
completed with zero warnings/errors, and all 40 tests passed (11 core, 18
infrastructure, 11 API; no skips). Native Windows/Android MAUI, containers and
CodeQL must pass on Actions independently. Local workload and sandbox access
errors were environmental; no local workload, Docker or OS security repair was
performed. Accepted source: 88d237f773d421e7454b133b1cda3e47ea47ad53.
[CI 38639670119](https://github.com/JasonStys/offline-first-field-app/actions/runs/38639670119)
passed core/API/evidence, Windows MAUI, Android MAUI and non-root container jobs.
[CodeQL 38639670137](https://github.com/JasonStys/offline-first-field-app/actions/runs/38639670137)
passed. The repaired existing PR remains available for separate merge review;
no unrelated updates were merged. Documentation-only commits use current path
filters, so the source acceptance above remains the measured native evidence.
Approved cleanup may remove historical URLs; all available evidence is privately archived.

Old dependency-review run 35406175627 failed because the provider feature was
unavailable. That workflow has already been removed from current source; it is
historical evidence, not a current broken job. Existing NuGet auditing remains.

See [NuGet's locked restore reference](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files).
