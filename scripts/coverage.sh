#!/usr/bin/env bash
# File: coverage.sh
# Purpose: Merge coverage from all test assemblies and enforce the platform-neutral line threshold.
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
coverage_root="$repository_root/artifacts/coverage"
mkdir -p "$coverage_root"
rm -f "$coverage_root/coverage.json" "$coverage_root/coverage.cobertura.xml"

dotnet test "$repository_root/tests/FieldOps.Core.Tests/FieldOps.Core.Tests.csproj" -c Release --no-build \
  /p:CollectCoverage=true /p:CoverletOutput="$coverage_root/coverage" /p:CoverletOutputFormat=json
dotnet test "$repository_root/tests/FieldOps.Infrastructure.Tests/FieldOps.Infrastructure.Tests.csproj" -c Release --no-build \
  /p:CollectCoverage=true /p:MergeWith="$coverage_root/coverage.json" \
  /p:CoverletOutput="$coverage_root/coverage" /p:CoverletOutputFormat=json
dotnet test "$repository_root/tests/FieldOps.Api.Tests/FieldOps.Api.Tests.csproj" -c Release --no-build \
  /p:CollectCoverage=true /p:MergeWith="$coverage_root/coverage.json" \
  /p:CoverletOutput="$coverage_root/coverage" /p:CoverletOutputFormat=json%2ccobertura \
  /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=total
