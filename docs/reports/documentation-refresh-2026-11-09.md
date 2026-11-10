# Documentation refresh — 2026-11-09

By Jason Stys.

This documentation-only edition describes source `c5471bc43c1a6296c785aeab40310ae227b2d295`. It does not change application behavior, accounts, private data, installers or published releases.

## Published editions and completed review

| File | Bytes | SHA-256 | Reviewed scope |
| --- | ---: | --- | --- |
| [project-guide.docx](project-guide.docx) | 45215 | `3dbdb58599c5b63377fbf9cf3c3ebd041660d94ae5f6bb22e8c590e82daba8f1` | 6 pages, each viewed individually |

Word review checked plain black titles, readable pagination, whole table rows, source references, practical limits and absence of clipping or overflow. Render receipts were matched to the current DOCX digest before approval.

## Evidence and change boundary

- Functional baseline: `c5471bc43c1a6296c785aeab40310ae227b2d295`.
- Workflow observation: `2026-11-10T05:25:40.628667+00:00`; exact run URLs and source identities remain in [publications.json](publications.json).
- Local document-package validation verifies exact copied bytes, Office ZIP integrity, approved page/tab identities and internal documentation links. Repository-specific documentation and source-policy commands are run separately and recorded in the integration handoff.
- Source/index metadata is regenerated using the repository’s existing tools. Existing privacy, security, coverage and workflow gates are retained.
- PR and later main workflows must pass independently. This record does not claim future or unrun integration CI success.

## Interpretation

The Word files document the cited source; dated source reports retain their original execution scope. A passing workflow is distinct from completed analysis with zero findings, and neither establishes production suitability. Checksums identify exact bytes, not a signature or publisher attestation.

Use the [handbook](../handbook.md) to choose usage, design, security and test documentation. Keep editable Markdown and reviewed editions together; do not replace historical measurements with undated claims.

## Actual integration validation

Platform-neutral Release build and dotnet format passed with zero build warnings/errors; all 57 tests passed (11 Core, 35 Infrastructure, 11 API), merged line coverage 94.61% retaining the 80% floor, exact index and repository/content/action-pin checks. No Android/MAUI device build or newly packaged installer was claimed; original hosted platform gates remain required.
