<#
.SYNOPSIS
  Installs or uninstalls the UGAR mod pack (BepInEx 6 IL2CPP + UGAR Mod Manager + bundled mods)
  for Ultimate General: American Revolution.

.PARAMETER GameDir
  Game folder (the one containing ugar.exe). Found automatically from Steam when left out.
.PARAMETER Uninstall
  Remove the mods this pack installed (and BepInEx, if this pack installed it).
.PARAMETER KeepBepInEx
  With -Uninstall: leave BepInEx in place even if this pack installed it.
.PARAMETER KeepSettings
  With -Uninstall: leave the mods' .cfg settings files.
.PARAMETER Force
  Install even while the game is running. Changes take effect the next time the game starts.
.PARAMETER Quiet
  No questions and no "press Enter" at the end.
#>
[CmdletBinding()]
param(
	[string]$GameDir,
	[switch]$Uninstall,
	[switch]$KeepBepInEx,
	[switch]$KeepSettings,
	[switch]$Force,
	[switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$PackRoot = Split-Path -Parent $PSScriptRoot
$Payload = Join-Path $PackRoot 'payload'
$RecordName = 'ugar-modpack-install.json'
$SteamAppId = '1901910'
$GameFolderName = 'Ultimate General American Revolution'

# Files BepInEx 6 IL2CPP adds to the game folder. Only these are removed on uninstall.
$BepInExRootItems = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'dotnet', 'BepInEx')

function Say([string]$msg, [string]$color = 'Gray') { Write-Host $msg -ForegroundColor $color }

function Finish([int]$code) {
	if (-not $Quiet) { Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
	exit $code
}

function Test-GameDir([string]$dir) {
	return $dir -and (Test-Path -LiteralPath (Join-Path $dir 'ugar.exe'))
}

function Get-SteamLibraries {
	$roots = @()
	foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
		try {
			$p = Get-ItemProperty -Path $key -ErrorAction Stop
			foreach ($name in 'SteamPath', 'InstallPath') {
				if ($p.$name) { $roots += ($p.$name -replace '/', '\') }
			}
		} catch { }
	}
	$libs = @()
	foreach ($root in ($roots | Select-Object -Unique)) {
		$libs += $root
		$vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
		if (Test-Path -LiteralPath $vdf) {
			foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
				$libs += ($m.Groups[1].Value -replace '\\\\', '\')
			}
		}
	}
	return $libs | Select-Object -Unique
}

function Find-GameDir {
	# 1. The pack was unzipped straight into the game folder.
	if (Test-GameDir $PackRoot) { return $PackRoot }
	$parent = Split-Path -Parent $PackRoot
	if (Test-GameDir $parent) { return $parent }
	# 2. Steam libraries.
	foreach ($lib in Get-SteamLibraries) {
		$manifest = Join-Path $lib "steamapps\appmanifest_$SteamAppId.acf"
		$folder = $GameFolderName
		if (Test-Path -LiteralPath $manifest) {
			$m = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"installdir"\s+"([^"]+)"')
			if ($m.Success) { $folder = $m.Groups[1].Value }
		}
		$dir = Join-Path $lib "steamapps\common\$folder"
		if (Test-GameDir $dir) { return $dir }
	}
	return $null
}

function Ask-GameDir {
	if ($Quiet) { return $null }
	try {
		Add-Type -AssemblyName System.Windows.Forms
		$dlg = New-Object System.Windows.Forms.FolderBrowserDialog
		$dlg.Description = 'Select the Ultimate General: American Revolution folder (the one with ugar.exe)'
		if ($dlg.ShowDialog() -eq 'OK') { return $dlg.SelectedPath }
	} catch {
		return Read-Host 'Paste the game folder path (the one with ugar.exe)'
	}
	return $null
}

function Test-Writable([string]$dir) {
	$probe = Join-Path $dir ('.ugar-write-test-' + [guid]::NewGuid().ToString('N'))
	try { [IO.File]::WriteAllText($probe, 'x'); Remove-Item -LiteralPath $probe -Force; return $true } catch { return $false }
}

function Restart-Elevated {
	$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
	if ($isAdmin) { return $false }
	Say 'The game folder needs administrator rights to change. Asking Windows for permission...' Yellow
	$argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-GameDir', "`"$GameDir`"")
	if ($Uninstall) { $argList += '-Uninstall' }
	if ($KeepBepInEx) { $argList += '-KeepBepInEx' }
	if ($KeepSettings) { $argList += '-KeepSettings' }
	if ($Force) { $argList += '-Force' }
	if ($Quiet) { $argList += '-Quiet' }
	$p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait -PassThru
	exit $p.ExitCode
}

function Get-FileHashOrNull([string]$path) {
	if (Test-Path -LiteralPath $path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
	return $null
}

# Copies one file, coping with a dll the running game still has open (rename it out of the way first).
function Copy-PackFile([string]$src, [string]$dst) {
	if ((Get-FileHashOrNull $src) -eq (Get-FileHashOrNull $dst)) { return }
	$dir = Split-Path -Parent $dst
	if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
	try {
		Copy-Item -LiteralPath $src -Destination $dst -Force
	} catch {
		$old = "$dst.old-" + [DateTime]::Now.Ticks
		Move-Item -LiteralPath $dst -Destination $old -Force
		Copy-Item -LiteralPath $src -Destination $dst -Force
	}
}

function Read-Record([string]$path) {
	if (Test-Path -LiteralPath $path) {
		try { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { }
	}
	return $null
}

# ---------------------------------------------------------------------------------------------

Say ''
Say '=== UGAR Mod Pack ===' Cyan

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not (Test-GameDir $GameDir)) {
	if ($GameDir) { Say "No ugar.exe in $GameDir" Yellow }
	else { Say 'Could not find the game through Steam.' Yellow }
	$GameDir = Ask-GameDir
}
if (-not (Test-GameDir $GameDir)) {
	Say 'Game folder not found. Run again with -GameDir "<path to the folder with ugar.exe>".' Red
	Finish 1
}
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
Say "Game folder: $GameDir"

$running = Get-Process -Name 'ugar' -ErrorAction SilentlyContinue | Where-Object {
	try { $_.Path -and ((Split-Path -Parent $_.Path) -eq $GameDir) } catch { $true }
}
if ($running -and -not $Force) {
	Say 'The game is running. Close it first, then run this again.' Red
	Finish 1
}
if ($running) { Say 'The game is running: changes take effect the next time it starts.' Yellow }

if (-not (Test-Writable $GameDir)) { Restart-Elevated | Out-Null }

$bepinex = Join-Path $GameDir 'BepInEx'
$pluginsDir = Join-Path $bepinex 'plugins'
$configDir = Join-Path $bepinex 'config'
$recordPath = Join-Path $bepinex $RecordName
$record = Read-Record $recordPath

# ---------------------------------------------------------------------------------------------
if ($Uninstall) {
	if (-not $record) {
		Say 'This mod pack is not installed here (no install record found). Nothing to do.' Yellow
		Finish 0
	}
	foreach ($folder in $record.pluginFolders) {
		$p = Join-Path $pluginsDir $folder
		if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force; Say "Removed plugin $folder" }
	}
	if (-not $KeepSettings) {
		foreach ($cfg in $record.configFiles) {
			$p = Join-Path $configDir $cfg
			if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force; Say "Removed settings $cfg" }
		}
	}

	$removeBepInEx = $record.installedBepInEx -and -not $KeepBepInEx
	if ($removeBepInEx) {
		$others = @()
		if (Test-Path -LiteralPath $pluginsDir) { $others = @(Get-ChildItem -LiteralPath $pluginsDir -Recurse -File -Filter '*.dll') }
		if ($others.Count -gt 0) {
			Say "Other BepInEx mods are still installed ($($others.Count) dll file(s)), so BepInEx is kept." Yellow
			$removeBepInEx = $false
		} elseif (-not $Quiet) {
			$answer = Read-Host 'Also remove BepInEx (the mod loader this pack installed)? [Y/n]'
			if ($answer -match '^[nN]') { $removeBepInEx = $false }
		}
	}
	if ($removeBepInEx) {
		foreach ($item in $BepInExRootItems) {
			$p = Join-Path $GameDir $item
			if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force; Say "Removed $item" }
		}
		Say 'BepInEx removed. The game is back to unmodded.' Green
	} else {
		if (Test-Path -LiteralPath $recordPath) { Remove-Item -LiteralPath $recordPath -Force }
		Say 'Mod pack removed. BepInEx was left in place.' Green
	}
	Finish 0
}

# ---------------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $Payload)) {
	Say "Payload folder missing: $Payload. Unzip the whole package and run Install.bat from it." Red
	Finish 1
}

$installedBepInEx = [bool]($record -and $record.installedBepInEx)
$loader = Join-Path $bepinex 'core\BepInEx.Unity.IL2CPP.dll'
if (Test-Path -LiteralPath $loader) {
	$v = (Get-Item -LiteralPath (Join-Path $bepinex 'core\BepInEx.Core.dll')).VersionInfo.ProductVersion
	Say "BepInEx already installed ($v), keeping it."
} else {
	if (Test-Path -LiteralPath (Join-Path $bepinex 'core\BepInEx.dll')) {
		Say 'Found BepInEx 5, which cannot load mods in this game. Installing BepInEx 6 IL2CPP over it.' Yellow
	}
	Say 'Installing BepInEx 6 (IL2CPP) mod loader...'
	$src = Join-Path $Payload 'BepInEx-IL2CPP'
	Get-ChildItem -LiteralPath $src -Recurse -File -Force | ForEach-Object {
		$rel = $_.FullName.Substring($src.Length + 1)
		Copy-PackFile $_.FullName (Join-Path $GameDir $rel)
	}
	foreach ($d in 'plugins', 'patchers', 'config') { New-Item -ItemType Directory -Path (Join-Path $bepinex $d) -Force | Out-Null }
	$installedBepInEx = $true
	Say 'BepInEx installed.' Green
}

New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
$pluginFolders = @()
foreach ($mod in Get-ChildItem -LiteralPath (Join-Path $Payload 'plugins') -Directory) {
	$dest = Join-Path $pluginsDir $mod.Name
	foreach ($f in Get-ChildItem -LiteralPath $mod.FullName -Recurse -File) {
		$rel = $f.FullName.Substring($mod.FullName.Length + 1)
		$target = Join-Path $dest $rel
		# Keep a mod switched off if the player switched it off in the mod manager.
		if ($f.Extension -eq '.dll' -and (Test-Path -LiteralPath "$target.disabled")) {
			$target = "$target.disabled"
		}
		Copy-PackFile $f.FullName $target
	}
	# Leftovers from earlier updates that were locked by a running game.
	Get-ChildItem -LiteralPath $dest -Recurse -File -Filter '*.old-*' -ErrorAction SilentlyContinue | ForEach-Object {
		try { Remove-Item -LiteralPath $_.FullName -Force } catch { }
	}
	$pluginFolders += $mod.Name
	Say "Installed $($mod.Name)"
}

# Update: remove mods an earlier version of the pack installed that this version no longer ships.
if ($record -and $record.pluginFolders) {
	foreach ($folder in $record.pluginFolders) {
		if ($pluginFolders -contains $folder) { continue }
		$p = Join-Path $pluginsDir $folder
		if (Test-Path -LiteralPath $p) {
			try {
				Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop
				Say "Removed $folder (no longer part of the pack)"
			} catch {
				Say "Could not remove $folder (the game may be running); remove BepInEx\plugins\$folder by hand." Yellow
			}
		}
	}
}

$packInfo = Read-Record (Join-Path $PackRoot 'pack.json')
$newRecord = [ordered]@{
	pack             = 'UGAR Mod Pack'
	version          = if ($packInfo) { $packInfo.version } else { 'unknown' }
	installedAt      = (Get-Date).ToString('s')
	installedBepInEx = $installedBepInEx
	pluginFolders    = $pluginFolders
	configFiles      = if ($packInfo) { @($packInfo.configFiles) } else { @() }
}
$newRecord | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding UTF8

Say ''
Say 'Done.' Green
if ($installedBepInEx -and -not $record) {
	Say 'The FIRST launch after installing BepInEx takes a few extra minutes while it prepares the game. That is normal.' Yellow
}
Say 'In game, press F8 (or click the small "Mods" button in the bottom-left corner) to open the mod manager.'
Finish 0
