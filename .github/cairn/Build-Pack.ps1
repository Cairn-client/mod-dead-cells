# Copyright (C) 2026 the Cairn contributors. SPDX-License-Identifier: MIT (as the project it is part of)
<#
.SYNOPSIS
    Builds the Cairn mod pack for Dead Cells (Dead Cells Multiplayer Mod): release\Cairn-Pack-dead-cells-<version>.zip and
    release\pack-index.json (what the Cairn launcher reads to see a newer pack exists).

.DESCRIPTION
    An *external* pack that goes into the game's own folder (docs in the launcher repository: docs/PACKS.md, installDir ".").
    The mod runs on the Dead Cells Core Modding API (DCCM, MIT), which lives in a "coremod" folder of the game and starts the game through its
    own program, so the pack carries both and names that program as its "launch":

      mod\coremod\core, plugins, ModCoreVersion.txt   DCCM (the win-x64 zip of its release, without core\mdk, the kit for building mods)
      mod\coremod\mods\DeadCellsMultiplayerMod\       this repository's mod as its build lays it out (modinfo.json directly inside)
      pack.json      the contract with the launcher
      manifest.txt   MOD|<path>|<size>|<sha256>, one line per file

    The pack's version is the VERSION file (strict X.Y.Z, this project's own); the mod's version (BuildInfo.cs) is the pack's "upstream".
    A file of the game's with the same name and other bytes is never overwritten by the launcher.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DccmZip,
    [Parameter(Mandatory)][string] $ModDir,          # bin\Release\net10.0\output\DeadCellsMultiplayerMod
    [Parameter(Mandatory)][string] $UpstreamVersion
)

$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$version = (Get-Content (Join-Path $root 'VERSION') -TotalCount 1).Trim()
if ($version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw "VERSION must be strict X.Y.Z, got '$version'" }
if ($UpstreamVersion -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw "upstream version must be X.Y.Z, got '$UpstreamVersion'" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$release = Join-Path $root 'release'
$stage = Join-Path $release 'pack-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$mod = Join-Path $stage 'mod'
$coremod = Join-Path $mod 'coremod'
New-Item -ItemType Directory -Force -Path $coremod | Out-Null

$destFull = [IO.Path]::GetFullPath($coremod).TrimEnd('\') + '\'
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $DccmZip))
try {
    foreach ($e in $zip.Entries) {
        $name = $e.FullName.Replace('\', '/')
        if ($name.EndsWith('/') -or $name.StartsWith('core/mdk/')) { continue }   # the kit for building mods is not needed to run them
        $target = [IO.Path]::GetFullPath((Join-Path $coremod ($name -replace '/', '\')))
        if (-not $target.StartsWith($destFull, [StringComparison]::OrdinalIgnoreCase)) { throw "the archive has a path outside its folder: $($e.FullName)" }
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
    }
} finally { $zip.Dispose() }

$modDest = Join-Path $coremod 'mods\DeadCellsMultiplayerMod'
New-Item -ItemType Directory -Force -Path $modDest | Out-Null
Copy-Item (Join-Path $ModDir '*') $modDest -Recurse -Force

foreach ($n in @('coremod\core\host\startup\DeadCellsModding.exe', 'coremod\ModCoreVersion.txt', 'coremod\mods\DeadCellsMultiplayerMod\modinfo.json')) {
    if (-not (Test-Path (Join-Path $mod $n))) { throw "the pack is missing $n" }
}

function Get-Sha256([string] $Path) { (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

# a launcher before 0.3.24 would start the game without the mod: it is refused there
$launcherMin = '0.3.24'
$packJson = [ordered]@{
    schema      = 2
    id          = 'dead-cells'
    game        = 'dead-cells'
    version     = $version
    upstream    = $UpstreamVersion
    kind        = 'external'
    installDir  = '.'
    launch      = 'coremod/core/host/startup/DeadCellsModding.exe'
    launcherMin = $launcherMin
}
[IO.File]::WriteAllText((Join-Path $stage 'pack.json'), (($packJson | ConvertTo-Json) + "`n"), (New-Object System.Text.UTF8Encoding($false)))

$lines = New-Object System.Collections.Generic.List[string]
foreach ($f in Get-ChildItem $mod -Recurse -File | Sort-Object FullName) {
    $rel = $f.FullName.Substring($mod.Length + 1)
    $lines.Add(('MOD|{0}|{1}|{2}' -f $rel, $f.Length, (Get-Sha256 $f.FullName)))
}
[IO.File]::WriteAllText((Join-Path $stage 'manifest.txt'), (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

$zipName = "Cairn-Pack-dead-cells-$version.zip"
$zipPath = Join-Path $release $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$fs = [IO.File]::Open($zipPath, 'Create')
try {
    $z = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in Get-ChildItem $stage -Recurse -File) {
            $rel = $f.FullName.Substring($stage.Length + 1) -replace '\\', '/'
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $z.Dispose() }
} finally { $fs.Dispose() }

$index = [ordered]@{
    schema      = 2
    id          = 'dead-cells'
    game        = 'dead-cells'
    version     = $version
    upstream    = $UpstreamVersion
    kind        = 'external'
    file        = $zipName
    sha256      = Get-Sha256 $zipPath
    size        = (Get-Item $zipPath).Length
    launcherMin = $launcherMin
}
[IO.File]::WriteAllText((Join-Path $release 'pack-index.json'), (($index | ConvertTo-Json) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "pack: $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB), version $version, mod $UpstreamVersion, $($lines.Count) files"
