# File: host-package-evidence.ps1
# Purpose: Read bounded notices from the exact separately signature-verified apphost NuGet archive.
# Functions: Read-HostPackageNotices validates metadata and decodes only the two fixed notice entries.
# State: no extraction or project/cache changes; SDK-bundled packs need not exist in .packages.
Set-StrictMode -Version Latest

# Decode only fixed metadata/notices after the caller has independently verified the archive signature.
function Read-HostPackageNotices([string]$Archive) {
    $file = Get-Item -LiteralPath $Archive
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint -or $file.Length -gt 33554432) {
        throw 'Apphost evidence must be a bounded regular NuGet archive.'
    }
    $package = [IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        if ($package.Entries.Count -gt 64) { throw 'Apphost evidence has too many members.' }
        $text = [ordered]@{}
        foreach ($name in @('Microsoft.NETCore.App.Host.win-x64.nuspec','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
            $entries = @($package.Entries | Where-Object { $_.FullName -ceq $name })
            if ($entries.Count -ne 1 -or $entries[0].Length -gt 2097152) { throw 'Required apphost evidence is missing, ambiguous or oversized.' }
            $stream = $entries[0].Open()
            try {
                $buffer = [byte[]]::new(2097153)
                $count = 0
                while ($count -lt $buffer.Length) {
                    $read = $stream.Read($buffer,$count,$buffer.Length-$count)
                    if ($read -eq 0) { break }
                    $count += $read
                }
                if ($count -gt 2097152 -or $count -ne $entries[0].Length) { throw 'Apphost evidence expanded beyond its declared bound.' }
                $text[$name] = [Text.UTF8Encoding]::new($false,$true).GetString($buffer,0,$count)
            }
            finally { $stream.Dispose() }
        }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create([IO.StringReader]::new($text['Microsoft.NETCore.App.Host.win-x64.nuspec'].TrimStart([char]0xfeff)),$settings)
        try {
            $metadata = [Xml.XmlDocument]::new()
            $metadata.XmlResolver = $null
            $metadata.Load($reader)
            $id = $metadata.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="id"]')
            $version = $metadata.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]/*[local-name()="version"]')
            if ($null -eq $id -or $null -eq $version -or $id.InnerText -cne 'Microsoft.NETCore.App.Host.win-x64' -or $version.InnerText -cne '10.0.15') {
                throw 'Apphost evidence does not identify the exact reviewed runtime pack.'
            }
        }
        finally { $reader.Dispose() }
        return $text
    }
    finally { $package.Dispose() }
}
