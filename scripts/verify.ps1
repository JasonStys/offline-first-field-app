# File: verify.ps1
# Purpose: Run the reproducible Windows release gate plus both declared MAUI platform builds.
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
  dotnet restore FieldOps.CI.slnx --locked-mode
  dotnet format FieldOps.CI.slnx --verify-no-changes --no-restore
  dotnet build FieldOps.CI.slnx -c Release --no-restore
  & "$PSScriptRoot\coverage.ps1"
  dotnet run --project src\FieldOps.Demo -c Release --no-build
  dotnet run --project src\FieldOps.Demo -c Release --no-build -- --benchmark
  dotnet publish src\FieldOps.Api -c Release --no-build --no-self-contained -o artifacts\api
  dotnet run --project tools\FieldOps.RepositoryChecks -c Release --no-build -- index-check
  dotnet run --project tools\FieldOps.RepositoryChecks -c Release --no-build -- validate
  dotnet list FieldOps.CI.slnx package --vulnerable --include-transitive
  # The original multi-platform restore is OS/workload-dependent; native RID graphs use separate locks.
  dotnet restore src\FieldOps.App\FieldOps.App.csproj
  dotnet build src\FieldOps.App\FieldOps.App.csproj -c Release -f net10.0-android --no-restore
  dotnet restore src\FieldOps.App\FieldOps.App.csproj --locked-mode -p:NativeWindowsOnly=true `
    -r win-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:FieldOpsNativeRuntimeVersion=10.0.15
  dotnet build src\FieldOps.App\FieldOps.App.csproj -c Release `
    -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained --no-restore `
    -p:NativeWindowsOnly=true -p:WindowsAppSDKSelfContained=true -p:FieldOpsNativeRuntimeVersion=10.0.15
} finally {
  Pop-Location
}
