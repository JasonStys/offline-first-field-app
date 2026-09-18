# File: coverage.ps1
# Purpose: Merge coverage from all test assemblies and enforce the platform-neutral line threshold.
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$coverageRoot = Join-Path $repositoryRoot "artifacts\coverage"
New-Item -ItemType Directory -Force -Path $coverageRoot | Out-Null
Remove-Item -LiteralPath (Join-Path $coverageRoot "coverage.json") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $coverageRoot "coverage.cobertura.xml") -Force -ErrorAction SilentlyContinue
$coverageBase = (Join-Path $coverageRoot "coverage").Replace("\", "/")
$coverageJson = (Join-Path $coverageRoot "coverage.json").Replace("\", "/")

dotnet test "$repositoryRoot\tests\FieldOps.Core.Tests\FieldOps.Core.Tests.csproj" -c Release --no-build `
  /p:CollectCoverage=true /p:CoverletOutput=$coverageBase /p:CoverletOutputFormat=json
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test "$repositoryRoot\tests\FieldOps.Infrastructure.Tests\FieldOps.Infrastructure.Tests.csproj" -c Release --no-build `
  /p:CollectCoverage=true /p:MergeWith=$coverageJson /p:CoverletOutput=$coverageBase /p:CoverletOutputFormat=json
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test "$repositoryRoot\tests\FieldOps.Api.Tests\FieldOps.Api.Tests.csproj" -c Release --no-build `
  /p:CollectCoverage=true /p:MergeWith=$coverageJson /p:CoverletOutput=$coverageBase `
  /p:CoverletOutputFormat=json%2ccobertura /p:Threshold=80 /p:ThresholdType=line /p:ThresholdStat=total
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
