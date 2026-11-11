# File: test-native.ps1
# Purpose: Verify actual relocated MAUI EXE controls, current runtime and completion-based shutdown.
# State: only fresh explicitly byte-marked synthetic roots and this script's owned child processes.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Bundle, [Parameter(Mandatory)][string]$Proof)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bundleDirectory = [IO.Path]::GetFullPath($Bundle)
$proofDirectory = [IO.Path]::GetFullPath($Proof)
$prefix = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts/native')) + [IO.Path]::DirectorySeparatorChar
if (-not $IsWindows -or -not $proofDirectory.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $proofDirectory)) { throw 'Use a fresh owned Windows proof directory.' }
$identity = Get-Content -LiteralPath (Join-Path $bundleDirectory 'SOURCE.json') -Raw | ConvertFrom-Json
if ($identity.product -ne 'offline-first-field-app' -or $identity.version -ne '1.1.0' -or $identity.dotnetRuntime -ne '10.0.15' -or $identity.source -notmatch '^[a-f0-9]{40}$') { throw 'Actual candidate identity mismatch.' }
foreach ($property in $identity.applicationBytes.PSObject.Properties) {
    if ($property.Name -notin @('FieldOps.App.dll','FieldOps.Api.dll','FieldOps.Core.dll','FieldOps.Infrastructure.dll')) { throw 'Unexpected application payload name.' }
    if ((Get-FileHash -LiteralPath (Join-Path $bundleDirectory $property.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $property.Value) { throw 'Actual compiled payload changed.' }
}
if (@($identity.applicationBytes.PSObject.Properties).Count -ne 4) { throw 'Incomplete compiled payload identity.' }
[void][IO.Directory]::CreateDirectory($proofDirectory)
[IO.File]::WriteAllBytes((Join-Path $proofDirectory 'acceptance.marker'),[Text.Encoding]::UTF8.GetBytes("fieldops-owned-native-acceptance-v1`n"))
$records = @()
foreach ($phase in @('first','reopen')) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $bundleDirectory 'FieldOps.App.exe'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $proofDirectory
    $start.ArgumentList.Add('--native-acceptance-root')
    $start.ArgumentList.Add($proofDirectory)
    if ($phase -eq 'reopen') { $start.ArgumentList.Add('--acceptance-reopen') }
    $start.Environment.Clear()
    foreach ($name in @('SYSTEMROOT','WINDIR','TEMP','TMP')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { $start.Environment[$name] = $value }
    }
    $start.Environment['PATH'] = Join-Path $env:SYSTEMROOT 'System32'
    $childProcess = [Diagnostics.Process]::Start($start)
    try {
        if (-not $childProcess.WaitForExit(120000)) {
            # Timeout is a FAILED isolated test, never an accepted normal app shutdown.
            $childProcess.Kill($true)
            $childProcess.WaitForExit()
            throw 'The owned synthetic native test exceeded its120second process bound.'
        }
        $receipt = Get-Content -LiteralPath (Join-Path $proofDirectory "$phase/acceptance.json") -Raw | ConvertFrom-Json
        if ($childProcess.ExitCode -ne 0 -or $receipt.passed -ne $true -or $receipt.processId -ne $childProcess.Id -or
            $receipt.runtime -ne '10.0.15' -or $receipt.package.source -ne $identity.source -or $receipt.normalProfileOpened -ne $false) {
            throw "Actual extracted native acceptance failed; phase=$phase; exit=$($childProcess.ExitCode); reason=$($receipt.failure)"
        }
        $records += $receipt
        $completed = Get-Content -LiteralPath (Join-Path $proofDirectory "$phase/close-operation-completed.json") -Raw | ConvertFrom-Json
        if ($completed.completed -ne $true) { throw 'Actual retained local operation did not complete before process exit.' }
        $origin = [Uri]$receipt.ownedOrigin
        if ($origin.Scheme -ne 'http' -or $origin.Host -ne '127.0.0.1' -or $origin.Port -lt 1) { throw 'Unexpected owned listener identity.' }
        # Inspect the actual listener table: a firewall-dropped SYN timeout cannot prove liveness.
        $listeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
        if (@($listeners | Where-Object { $_.Port -eq $origin.Port }).Count -ne 0) {
            throw 'The former owned port is still a listener after actual application exit.'
        }
    }
    finally { $childProcess.Dispose() }
}
if (($records[0].retained | ConvertTo-Json -Depth 6 -Compress) -ne ($records[1].retained | ConvertTo-Json -Depth 6 -Compress)) { throw 'Native restart changed the exact retained records.' }
# The application must have released its client and companion ownership after actual process exit.
foreach ($name in @('profile/native-app.lock','profile/native-companion/owner.lock')) {
    $ownership = [IO.FileStream]::new((Join-Path $proofDirectory $name),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $ownership.Dispose()
}
@{schema=1;passed=$true;launches=2;actualExitCodes=@(0,0);sameRetainedRecords=$true;package=$identity;
    normalProfileOpened=$false;dockerChanged=$false;runtime='10.0.15'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $proofDirectory 'summary.json') -Encoding utf8
Write-Output 'Both actual native launches passed; exact retained records and released ownership verified.'
