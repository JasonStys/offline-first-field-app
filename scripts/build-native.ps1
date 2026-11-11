# File: build-native.ps1
# Purpose: Build the genuine self-contained MAUI/WinUI app with source/runtime provenance and notices.
# Functions: Invoke-Checked runs fixed tools; Add-Notice bounds public dependency terms.
# State: fresh release output only; no installer, normal data, global SDK/workload or Docker changes.
[CmdletBinding()]
param([string]$Output = 'release/windows', [switch]$AllowDirty)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath((Join-Path $repository $Output))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repository 'release')) + [IO.Path]::DirectorySeparatorChar
if (-not $IsWindows -or -not $destination.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $destination)) {
    throw 'Choose a fresh Windows output below this repository release directory.'
}
$dotnetTool = @(Get-Command dotnet -CommandType Application)[0].Source
$gitTool = @(Get-Command git -CommandType Application)[0].Source
function Invoke-Checked([string[]]$Arguments) {
    & $dotnetTool @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'The checked .NET build command failed.' }
}
$gitArguments = @('-c', "safe.directory=$($repository.Replace('\','/'))", '-C', $repository)
$source = (& $gitTool @gitArguments rev-parse HEAD).Trim()
$before = (& $gitTool @gitArguments status --porcelain --untracked-files=all | Out-String).Trim()
$sdk = (& $dotnetTool --version).Trim()
if ($source -notmatch '^[a-f0-9]{40}$' -or ($before -and (-not $AllowDirty -or $env:GITHUB_ACTIONS -eq 'true'))) {
    throw 'A clean exact source checkout is required; dirty local prototypes need explicit labeling.'
}
if ($env:GITHUB_ACTIONS -eq 'true' -and ($env:GITHUB_SHA -ne $source -or $sdk -ne '10.0.404')) { throw 'CI source/SDK mismatch.' }
if ($sdk -notin @('10.0.403','10.0.404')) { throw 'Review a new SDK before building this native candidate.' }
$project = Join-Path $repository 'src/FieldOps.App/FieldOps.App.csproj'
[xml]$metadata = [IO.File]::ReadAllText($project)
if ($metadata.Project.PropertyGroup.Version -ne '1.1.0' -or $metadata.Project.PropertyGroup.ApplicationDisplayVersion -ne '1.1.0') { throw 'Native product versions differ.' }
Push-Location $repository
try {
    $properties = @('-p:NativeWindowsOnly=true','-r','win-x64','-p:SelfContained=true',
        '-p:WindowsAppSDKSelfContained=true','-p:FieldOpsNativeRuntimeVersion=10.0.15','-p:PublishTrimmed=false')
    Invoke-Checked -Arguments (@('restore',$project,'--locked-mode') + $properties)
    Invoke-Checked -Arguments (@('publish',$project,'-c','Release','-f','net10.0-windows10.0.19041.0','--no-restore','-o',$destination) + $properties)
    # Preserve the real API DLL; omit its separate CLI apphost/config from the single native application.
    foreach ($name in @('FieldOps.Api.exe','FieldOps.Api.runtimeconfig.json','FieldOps.Api.deps.json')) {
        $path = Join-Path $destination $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    $assets = Get-Content -LiteralPath (Join-Path $repository 'src/FieldOps.App/obj/project.assets.json') -Raw | ConvertFrom-Json
    $notices = [Text.StringBuilder]::new()
    function Add-Notice([string]$Title, [string]$Path) {
        $file = Get-Item -LiteralPath $Path
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint -or $file.Length -gt 2097152) { throw 'Notice must be a bounded regular public resource.' }
        [void]$notices.AppendLine("`n=== $Title ===")
        [void]$notices.AppendLine([IO.File]::ReadAllText($file.FullName))
    }
    Add-Notice 'Authored application by Jason Stys' (Join-Path $repository 'LICENSE')
    foreach ($library in $assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' }) {
        $directory = Join-Path (Join-Path $repository '.packages') $library.Value.path
        $specification = @(Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' -File)
        if ($specification.Count -ne 1) { throw 'Dependency package metadata is ambiguous.' }
        Add-Notice "$($library.Name) package metadata/license declaration" $specification[0].FullName
        foreach ($notice in Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD-PARTY)' }) {
            Add-Notice "$($library.Name) / $($notice.Name)" $notice.FullName
        }
    }
    $runtimeBytes = [ordered]@{}
    $runtimeFiles = @(
        @('microsoft.netcore.app.runtime.win-x64','runtimes/win-x64/native/coreclr.dll','coreclr.dll'),
        @('microsoft.netcore.app.runtime.win-x64','runtimes/win-x64/native/hostfxr.dll','hostfxr.dll'),
        @('microsoft.netcore.app.runtime.win-x64','runtimes/win-x64/lib/net10.0/System.Private.CoreLib.dll','System.Private.CoreLib.dll'),
        @('microsoft.aspnetcore.app.runtime.win-x64','runtimes/win-x64/lib/net10.0/Microsoft.AspNetCore.Server.Kestrel.Core.dll','Microsoft.AspNetCore.Server.Kestrel.Core.dll'))
    foreach ($entry in $runtimeFiles) {
        $cached = Join-Path $repository ('.packages/' + $entry[0] + '/10.0.15/' + $entry[1])
        $shipped = Join-Path $destination $entry[2]
        $expected = (Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash.ToLowerInvariant()
        if ((Get-FileHash -LiteralPath $shipped -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Bundled runtime differs from the current pinned NuGet runtime bytes.' }
        $runtimeBytes[$entry[2]] = $expected
    }
    foreach ($packageName in @('microsoft.netcore.app.runtime.win-x64','microsoft.aspnetcore.app.runtime.win-x64','microsoft.netcore.app.host.win-x64')) {
        $directory = Join-Path $repository ('.packages/' + $packageName + '/10.0.15')
        Invoke-Checked -Arguments @('nuget','verify',(Join-Path $directory "$packageName.10.0.15.nupkg"),'--all','--verbosity','quiet')
        foreach ($notice in Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY)' }) {
            Add-Notice "$packageName 10.0.15 / $($notice.Name)" $notice.FullName
        }
    }
    [IO.File]::WriteAllText((Join-Path $destination 'THIRD-PARTY-NOTICES.txt'),$notices.ToString(),[Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $destination 'LICENSE.txt')
    $applicationBytes = [ordered]@{}
    foreach ($name in @('FieldOps.App.dll','FieldOps.Api.dll','FieldOps.Core.dll','FieldOps.Infrastructure.dll')) {
        $applicationBytes[$name] = (Get-FileHash -LiteralPath (Join-Path $destination $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $identity = [ordered]@{schema=1;product='offline-first-field-app';version='1.1.0';source=$source;sourceDirty=[bool]$before;
        buildSdk=$sdk;dotnetRuntime='10.0.15';runtimeBytes=$runtimeBytes;
        applicationBytes=$applicationBytes;
        windowsAppSdk=($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Microsoft.WindowsAppSDK/' })}
    $identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $destination 'SOURCE.json') -Encoding utf8
    $after = (& $gitTool @gitArguments status --porcelain --untracked-files=all | Out-String).Trim()
    if ((& $gitTool @gitArguments rev-parse HEAD).Trim() -ne $source -or $before -ne $after) { throw 'Tracked source changed while building the candidate.' }
    Write-Output ($identity | ConvertTo-Json -Depth 6 -Compress)
}
finally { Pop-Location }
