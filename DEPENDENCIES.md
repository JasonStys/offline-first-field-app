# Dependency policy

- Runtime dependencies are limited to Microsoft-maintained .NET MAUI and SQLite packages plus the
  native SQLite bundle they transitively require.
- Test dependencies are exact, centrally managed versions in `Directory.Packages.props`.
- `dotnet list FieldOps.CI.slnx package --vulnerable --include-transitive` must return no vulnerable
  packages before release.
- Dependabot proposes grouped NuGet and GitHub Actions updates weekly. Pull requests run the full
  build, locked NuGet vulnerability audit, and CodeQL analysis.
- GitHub Actions are pinned to immutable 40-character commit SHAs.
- Major upgrades require an ADR when they alter storage, protocol, target platforms, or support.
