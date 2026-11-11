# File: archive-native.ps1
# Purpose: Produce/checksum a complete Windows payload and test the actually extracted application.
# State: fresh release sibling/owned proof outputs only; no installation, publication or normal profiles.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Bundle, [Parameter(Mandatory)][string]$Proof)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'portable-archive.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bundleDirectory = [IO.Path]::GetFullPath($Bundle)
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repository 'release')) + [IO.Path]::DirectorySeparatorChar
if (-not $bundleDirectory.StartsWith($releaseRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Bundle must be a fresh owned release candidate.' }
$archive = Join-Path ([IO.Path]::GetDirectoryName($bundleDirectory)) 'FieldOpsOffline-1.1.0-windows-x64.zip'
$digest = Write-PortableArchive -Bundle $bundleDirectory -Archive $archive
$sidecar = [IO.FileStream]::new($archive + '.sha256',[IO.FileMode]::CreateNew)
try {
    $bytes = [Text.Encoding]::ASCII.GetBytes("$digest  $([IO.Path]::GetFileName($archive))`n")
    $sidecar.Write($bytes)
}
finally { $sidecar.Dispose() }
$extracted = Join-Path ([IO.Path]::GetDirectoryName($bundleDirectory)) 'extracted-portable'
Expand-PortableArchive -Archive $archive -Destination $extracted
& (Join-Path $PSScriptRoot 'test-native.ps1') -Bundle $extracted -Proof $Proof
Write-Output "Actual relocated archive SHA256: $digest"
