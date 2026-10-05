<#
.SYNOPSIS
  Builds the player package dist\UGAR-ModPack-<version>.zip.

  - UGAR Mod Manager and UGAR ModKit are always built from src.
  - The other bundled mods are taken as currently deployed in the game's BepInEx\plugins folder
    (the version that was tested), or built from src with -BuildMods.
  - BepInEx 6 IL2CPP is copied from the game folder (loader files only: no interop/cache/config,
    which are generated from game files on first launch).

.PARAMETER DeployLocal
  Afterwards install the package onto this PC's game with the player installer (-Force, so it
  works while the game is running; changes load on the next launch).
#>
param(
	[string]$Version = '1.0.0',
	[string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Ultimate General American Revolution',
	[switch]$BuildMods,
	[switch]$DeployLocal
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Src = Join-Path $Root 'src'
$Dist = Join-Path $Root 'dist'
$Stage = Join-Path $Dist 'UGAR-ModPack'
$Zip = Join-Path $Dist "UGAR-ModPack-$Version.zip"
$Manifests = Join-Path $Src 'UGARModManager\manifests'

# Plugin folder name -> project, config file name (= plugin GUID + .cfg), optional data folder copied from src.
$Mods = [ordered]@{
	'UGARModManager'      = @{ Project = 'UGARModManager';      Cfg = 'ugar.modmanager.cfg'; AlwaysBuild = $true }
	'UGARModKit'          = @{ Project = 'UGARModKit';          Cfg = 'ugar.modkit.cfg';     AlwaysBuild = $true }
	'UGARBulkRecruit'     = @{ Project = 'UGARBulkRecruit';     Cfg = 'ugar.bulkrecruit.cfg' }
	'UGARFixes'           = @{ Project = 'UGARFixes';           Cfg = 'ugar.community.fixes.cfg' }
	'UGARCustomBuildings' = @{ Project = 'UGARCustomBuildings'; Cfg = 'ugar.custombuildings.cfg'; Data = 'buildings'; AlwaysBuild = $true }
	'UGARBritishEvents'   = @{ Project = 'UGARBritishEvents';   Cfg = 'ugar.britishevents.cfg';   Data = 'events' }
	'UGARWeaponWorkshop'  = @{ Project = 'UGARWeaponWorkshop';  Cfg = 'ugar.weaponworkshop.cfg' }
	'UGARDevConsole'      = @{ Project = 'UGARDevConsole';      Cfg = 'ugar.devconsole.cfg' }
	'UGARInstantBuild'    = @{ Project = 'UGARInstantBuild';    Cfg = 'ugar.instantbuild.cfg' }
	'UGARResourceBreakdown' = @{ Project = 'UGARResourceBreakdown'; Cfg = 'ugar.resourcebreakdown.cfg' }
	'UGARMiningGold'      = @{ Project = 'UGARMiningGold';      Cfg = 'ugar.mininggold.cfg' }
	'UGARTradePartners'   = @{ Project = 'UGARTradePartners';   Cfg = 'ugar.tradepartners.cfg' }
	'UGARRecruitGrowth'   = @{ Project = 'UGARRecruitGrowth';   Cfg = 'ugar.recruitgrowth.cfg' }
}

if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force "$Stage\installer", "$Stage\payload\plugins", "$Stage\payload\BepInEx-IL2CPP\BepInEx" | Out-Null

function Build([string]$project, [string]$out) {
	$tmp = Join-Path $env:TEMP "ugar-build-$project"
	if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
	dotnet build (Join-Path $Src "$project\$project.csproj") -c Release -o $tmp -p:GameDir="$GameDir" | Out-Host
	if ($LASTEXITCODE -ne 0) { throw "Build of $project failed" }
	Copy-Item (Join-Path $tmp "$project.dll") $out
	# API docs for libraries (UGAR ModKit), so modders get them in their editor.
	if (Test-Path (Join-Path $tmp "$project.xml")) { Copy-Item (Join-Path $tmp "$project.xml") $out }
}

foreach ($name in $Mods.Keys) {
	$out = Join-Path $Stage "payload\plugins\$name"
	New-Item -ItemType Directory -Force $out | Out-Null
	if ($Mods[$name].AlwaysBuild -or $BuildMods) {
		Build $Mods[$name].Project $out
	} else {
		$deployed = Join-Path $GameDir "BepInEx\plugins\$name\$name.dll"
		if (-not (Test-Path $deployed)) { $deployed = "$deployed.disabled" }
		if (-not (Test-Path $deployed)) { throw "$name is not deployed in the game; build it or use -BuildMods" }
		Copy-Item $deployed (Join-Path $out "$name.dll")
	}
	$manifest = Join-Path $Manifests "$name\ugar-mod.json"
	if (Test-Path $manifest) { Copy-Item $manifest $out }
	if ($Mods[$name].Data) {
		Copy-Item (Join-Path $Src "$($Mods[$name].Project)\$($Mods[$name].Data)") $out -Recurse
	}
}

# BepInEx loader files only.
$bie = Join-Path $Stage 'payload\BepInEx-IL2CPP'
foreach ($f in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') { Copy-Item (Join-Path $GameDir $f) $bie }
Copy-Item (Join-Path $GameDir 'dotnet') $bie -Recurse
Copy-Item (Join-Path $GameDir 'BepInEx\core') (Join-Path $bie 'BepInEx') -Recurse

# Installer, docs, pack info.
$inst = Join-Path $Src 'Installer'
Copy-Item "$inst\Install-UGARMods.ps1" "$Stage\installer"
Copy-Item "$inst\Install.bat", "$inst\Uninstall.bat", "$inst\THIRD-PARTY.txt" $Stage

$modLines = @()
foreach ($name in $Mods.Keys) {
	if ($name -eq 'UGARModManager') { continue }
	$m = Join-Path $Manifests "$name\ugar-mod.json"
	if (Test-Path $m) {
		$j = Get-Content $m -Raw | ConvertFrom-Json
		$modLines += "- $($j.name): $($j.description)"
	}
}
$readme = (Get-Content "$inst\README.txt" -Raw).Replace('{VERSION}', $Version).Replace('{MODLIST}', ($modLines -join "`r`n"))
Set-Content "$Stage\README.txt" $readme -Encoding UTF8

@{ version = $Version; configFiles = @($Mods.Values | ForEach-Object { $_.Cfg }) } | ConvertTo-Json | Set-Content "$Stage\pack.json" -Encoding UTF8

if (Test-Path $Zip) { Remove-Item $Zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Zip root = package folder, so players get one "UGAR-ModPack" folder when they unzip.
[IO.Compression.ZipFile]::CreateFromDirectory($Stage, $Zip, [IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host ("Package: {0} ({1:N1} MB)" -f $Zip, ((Get-Item $Zip).Length / 1MB)) -ForegroundColor Green
# Same zip under a fixed name: upload it to each GitHub release so .../releases/latest/download/UGAR-ModPack.zip
# (the README's download link) always gets the newest version.
Copy-Item $Zip (Join-Path $Dist 'UGAR-ModPack.zip') -Force

if ($DeployLocal) {
	& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$Stage\installer\Install-UGARMods.ps1" -GameDir $GameDir -Force -Quiet
}
