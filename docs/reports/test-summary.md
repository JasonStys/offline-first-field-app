# Test summary

The baseline includes 40 passing automated tests:

- 11 domain/coordinator tests, including 1,000 deterministic property-style merge cases.
- 18 SQLite, attachment, HTTP-gateway, queue-capacity, and query-plan tests.
- 11 full HTTP tests, including twelve concurrent writers for one version-zero record.

Merged platform-neutral coverage is 93.59% lines, 73.66% branches, and 84.25% methods. Coverage is a
regression signal, not a substitute for assertions; the suite focuses on durability, idempotency,
conflicts, bounds, malformed input, cleanup, and recovery.

Android and Windows clients compile as Release builds. Device-level interaction and assistive
technology remain in the [manual checklist](manual-accessibility-checklist.md).
