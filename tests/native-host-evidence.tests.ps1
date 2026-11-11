# File: native-host-evidence.tests.ps1
# Purpose: Test actual ZIP notice/schema boundaries independently of NuGet signature verification.
# Functions: Fixture creates fresh archives; Refuse requires failure; no SDK/cache/user data is changed.
# State: only fresh ignored fixture directories; test archives are not signed or installed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/host-package-evidence.ps1')
$directory = Join-Path (Split-Path $PSScriptRoot -Parent) ('.runtime/host-guards-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
$metadata = '<package><metadata><id>Microsoft.NETCore.App.Host.win-x64</id><version>10.0.15</version></metadata></package>'
# Write a real fresh ZIP from literal synthetic members; earlier evidence is never replaced.
function Fixture([string]$Name,[object[]]$Members) {
    $path = Join-Path $directory "$Name.zip"
    $package = [IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($member in $Members) {
            $stream = $package.CreateEntry($member[0]).Open()
            try {
                $raw = [Text.UTF8Encoding]::new($false).GetBytes($member[1])
                $stream.Write($raw)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $package.Dispose() }
    return $path
}
# Require the actual archive reader to reject malformed evidence; no assertion can be optimized away.
function Refuse([string]$Path) {
    $rejected = $false
    try { [void](Read-HostPackageNotices -Archive $Path) } catch { $rejected = $true }
    if (-not $rejected) { throw 'Malformed evidence was accepted.' }
}
$valid = @(@('Microsoft.NETCore.App.Host.win-x64.nuspec',$metadata),@('LICENSE.TXT','Example license'),@('THIRD-PARTY-NOTICES.TXT','Example notices'))
$path = Fixture 'valid' $valid
$observed = Read-HostPackageNotices -Archive $path
if ($observed.Count -ne 3 -or $observed['LICENSE.TXT'] -cne 'Example license') { throw 'Fixed notice contents differ.' }
Refuse (Fixture 'missing' @($valid[0],$valid[1]))
Refuse (Fixture 'duplicate' @($valid[0],$valid[1],$valid[2],$valid[1]))
Refuse (Fixture 'wrong-version' @(@($valid[0][0],$metadata.Replace('10.0.15','10.0.14')),$valid[1],$valid[2]))
Refuse (Fixture 'wrong-product' @(@($valid[0][0],$metadata.Replace('Host.win-x64','Host.linux-x64')),$valid[1],$valid[2]))
Refuse (Fixture 'dtd' @(@($valid[0][0],'<!DOCTYPE package [<!ENTITY x "wrong">]><package>&x;</package>'),$valid[1],$valid[2]))
Refuse (Fixture 'expanded-budget' @($valid[0],@('LICENSE.TXT',('a' * 2097153)),$valid[2]))
$many = @($valid)
foreach ($number in 1..62) { $many += ,@("extra-$number",'x') }
Refuse (Fixture 'members-budget' $many)
[ordered]@{passed=$true;actualZipChecks=8;directory=$directory;signatureVerificationTested=$false} | ConvertTo-Json -Compress
