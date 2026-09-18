#!/usr/bin/env bash
# File: verify.sh
# Purpose: Run the reproducible platform-neutral release gate used by contributors and CI.
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

dotnet restore FieldOps.CI.slnx --locked-mode
dotnet format FieldOps.CI.slnx --verify-no-changes --no-restore
dotnet build FieldOps.CI.slnx -c Release --no-restore
bash scripts/coverage.sh
dotnet run --project src/FieldOps.Demo -c Release --no-build
dotnet run --project src/FieldOps.Demo -c Release --no-build -- --benchmark
dotnet publish src/FieldOps.Api -c Release --no-build --no-self-contained -o artifacts/api
dotnet run --project tools/FieldOps.RepositoryChecks -c Release --no-build -- index-check
dotnet run --project tools/FieldOps.RepositoryChecks -c Release --no-build -- validate
dotnet list FieldOps.CI.slnx package --vulnerable --include-transitive
