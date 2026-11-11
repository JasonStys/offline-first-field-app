# File: portable-archive.ps1
# Purpose: Create complete Windows payload ZIPs and safely relocate bounded actual member bytes.
# Functions: Write-PortableArchive and Expand-PortableArchive use fresh caller-owned destinations.
# State: fixed member/byte budgets; no executable, installation or profile operation occurs here.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$PortableMaximumMembers = 10000
$PortableMaximumBytes = 2147483648L
$PortableMaximumMember = 268435456L

function Write-PortableArchive([string]$Bundle, [string]$Archive) {
    if (Test-Path -LiteralPath $Archive) { throw 'Archive output must be new.' }
    $root = Get-Item -LiteralPath $Bundle
    if (-not $root.PSIsContainer -or $root.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Portable bundle must be a real directory.' }
    $members = @(Get-ChildItem -LiteralPath $Bundle -Recurse -Force)
    if ($members.Count -eq 0 -or $members.Count -gt $PortableMaximumMembers) { throw 'Archive member inventory exceeds its bound.' }
    $total = 0L
    foreach ($member in $members) {
        if ($member.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked portable inputs are unsupported.' }
        if (-not $member.PSIsContainer) {
            $total += $member.Length
            if ($member.Length -gt $PortableMaximumMember -or $total -gt $PortableMaximumBytes) { throw 'Portable input exceeds byte budgets.' }
        }
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($Bundle,$Archive,[IO.Compression.CompressionLevel]::Fastest,$false)
    return (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Expand-PortableArchive([string]$Archive, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { throw 'Extraction destination must be new.' }
    $inputArchive = Get-Item -LiteralPath $Archive
    if ($inputArchive.PSIsContainer -or $inputArchive.Attributes -band [IO.FileAttributes]::ReparsePoint -or $inputArchive.Length -gt 536870912) { throw 'Expected a bounded regular portable ZIP.' }
    $package = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        if ($package.Entries.Count -eq 0 -or $package.Entries.Count -gt $PortableMaximumMembers) { throw 'Portable member budget exceeded.' }
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $total = 0L
        foreach ($entry in $package.Entries) {
            $name = $entry.FullName.TrimEnd('/')
            $parts = $name.Split('/')
            $mode = ([int64]$entry.ExternalAttributes -shr 16) -band 0xF000
            if (-not $name -or $name.Contains('\') -or $name.Contains(':') -or [IO.Path]::IsPathRooted($name) -or
                -not $names.Add($name) -or $mode -notin @(0,0x8000,0x4000) -or
                $entry.Length -lt 0 -or $entry.Length -gt $PortableMaximumMember) { throw 'Unsafe portable member.' }
            foreach ($part in $parts) {
                if ($part -in @('','.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or
                    $part.IndexOfAny([char[]]'<>:"\|?*') -ge 0 -or @($part.ToCharArray() | Where-Object { [int]$_ -lt 32 }).Count -gt 0 -or
                    $part.Split('.')[0].ToUpperInvariant() -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') { throw 'Ambiguous Windows portable member.' }
            }
            $total += $entry.Length
            if ($total -gt $PortableMaximumBytes) { throw 'Portable expanded-byte budget exceeded.' }
        }
        [void][IO.Directory]::CreateDirectory($Destination)
        $actual = 0L
        foreach ($entry in $package.Entries) {
            $path = [IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            $prefix = [IO.Path]::GetFullPath($Destination) + [IO.Path]::DirectorySeparatorChar
            if (-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Portable output escaped its fresh destination.' }
            if ($entry.FullName.EndsWith('/')) { [void][IO.Directory]::CreateDirectory($path); continue }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            $incoming = $entry.Open()
            $outgoing = [IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try {
                $buffer = [byte[]]::new(65536)
                $count = 0L
                while ($true) {
                    $read = $incoming.Read($buffer,0,[int][Math]::Min($buffer.Length,$PortableMaximumMember-$count+1))
                    if ($read -eq 0) { break }
                    $count += $read
                    $actual += $read
                    if ($count -gt $entry.Length -or $count -gt $PortableMaximumMember -or $actual -gt $PortableMaximumBytes) { throw 'Actual portable bytes exceed their budget.' }
                    $outgoing.Write($buffer,0,$read)
                }
                if ($count -ne $entry.Length) { throw 'Actual portable member length differs.' }
            }
            finally { $outgoing.Dispose(); $incoming.Dispose() }
        }
    }
    finally { $package.Dispose() }
}
