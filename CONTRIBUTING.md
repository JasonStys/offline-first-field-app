# Contributing

## Development contract

1. Use .NET SDK `10.0.401` or a compatible patch selected by `global.json`.
2. Run `dotnet restore FieldOps.CI.slnx`.
3. Make domain behavior testable outside MAUI and avoid platform code in `FieldOps.Core`.
4. Add runtime validation at every network, database, and file boundary.
5. Update architecture, protocol, security, limitations, and evidence when behavior changes.
6. Run `./scripts/verify.sh` or `./scripts/verify.ps1` before opening a pull request.

## Code conventions

- Nullable references, analyzers, deterministic builds, and warnings-as-errors remain enabled.
- Public types and methods document inputs, outputs, invariants, and failure behavior.
- Source files begin with a purpose/state header and use the generated code index for exact lines.
- SQL is parameterized; queue, request, attachment, and query sizes remain explicitly bounded.
- Tests use generated fixtures and deterministic random seeds.

## Pull requests

Keep changes focused. Explain the failure mode being addressed, list validation commands, call out
schema or protocol compatibility, and identify manual checks. Dependency updates must retain exact
versions in `Directory.Packages.props` and pass dependency review.
