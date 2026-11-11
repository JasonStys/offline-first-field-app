# File: native-archive.tests.ps1
# Purpose: Test actual ZIP traversal/device/collision/byte budgets and no-overwrite on real files.
# State: only fresh repository .runtime fixtures; no executable, account or normal profile is opened.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/portable-archive.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $repository ('.runtime/archive-guards-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$checks = 0
function Require([bool]$Condition,[string]$Message) { if(-not $Condition){throw $Message} }
function Write-Fixture([string]$Archive,[string[]]$Names,[int]$Bytes=3) {
    $file=[IO.FileStream]::new($Archive,[IO.FileMode]::CreateNew)
    $zip=[IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($name in $Names){$member=$zip.CreateEntry($name);$stream=$member.Open();try{$stream.Write([byte[]]::new($Bytes))}finally{$stream.Dispose()}}
    }finally{$zip.Dispose();$file.Dispose()}
}
function Refuses-BeforeCreation([string]$Archive,[string]$Destination) {
    $rejected=$false
    try { Expand-PortableArchive -Archive $Archive -Destination $Destination }
    catch { $rejected=$true }
    Require $rejected 'Unsafe actual ZIP was accepted.'
    Require (-not (Test-Path -LiteralPath $Destination)) 'Unsafe ZIP created output before admission.'
}
$bundle=Join-Path $testRoot 'bundle'
[void][IO.Directory]::CreateDirectory((Join-Path $bundle 'nested'))
[IO.File]::WriteAllBytes((Join-Path $bundle 'nested/data.bin'),[byte[]]@(0,13,10,255))
$archive=Join-Path $testRoot 'valid.zip'
$digest=Write-PortableArchive -Bundle $bundle -Archive $archive
Require ($digest -match '^[a-f0-9]{64}$') 'Real archive hash was absent.'
Expand-PortableArchive -Archive $archive -Destination (Join-Path $testRoot 'valid')
Require ([Linq.Enumerable]::SequenceEqual([byte[]]@(0,13,10,255),[IO.File]::ReadAllBytes((Join-Path $testRoot 'valid/nested/data.bin')))) 'Real relocated bytes changed.'
$checks++
foreach($names in @(@('../escape'),@('/absolute'),@('C:/drive'),@('a\b'),@('NUL.txt'),@('trailing.'),@('space '),@('a?b'),@('file','FILE'))){
    $id=[Guid]::NewGuid().ToString('N');$bad=Join-Path $testRoot "$id.zip"
    Write-Fixture -Archive $bad -Names $names
    Refuses-BeforeCreation -Archive $bad -Destination (Join-Path $testRoot $id)
    $checks++
}
$bad=Join-Path $testRoot 'expanded.zip'
Write-Fixture -Archive $bad -Names @('data') -Bytes 4096
$old=$PortableMaximumMember
$PortableMaximumMember=1024
try {Refuses-BeforeCreation -Archive $bad -Destination (Join-Path $testRoot 'expanded');$checks++}
finally {$PortableMaximumMember=$old}
$rejected=$false
try {Write-PortableArchive -Bundle $bundle -Archive $archive}catch{$rejected=$true}
Require $rejected 'Existing archive was overwritten.'
$rejected=$false
try {Expand-PortableArchive -Archive $archive -Destination (Join-Path $testRoot 'valid')}catch{$rejected=$true}
Require $rejected 'Existing extraction was reused.'
$checks+=2
@{passed=$true;actualZipChecks=$checks;directory=$testRoot} | ConvertTo-Json -Compress
