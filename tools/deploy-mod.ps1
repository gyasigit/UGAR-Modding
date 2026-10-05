<#
.SYNOPSIS
  Builds one mod from src and copies it into the game's BepInEx\plugins folder, even while the game is running.

  With the UGAR Mod Manager's live reload on (F8 window, "Live reload"), the running game picks the new version up
  about a second after the copy: no restart needed.

  A dll that the running game still locks (the version it loaded at start-up) can't be overwritten, so it is renamed
  to <name>.dll.old-<ticks> first; the mod manager deletes those leftovers on the next launch.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\deploy-mod.ps1 UGARBulkRecruit
  powershell -ExecutionPolicy Bypass -File tools\deploy-mod.ps1 UGARCustomBuildings -Data buildings
#>
param(
	[Parameter(Mandatory = $true, Position = 0)][string]$Project,
	# Plugin folder under BepInEx\plugins (default: the project name).
	[string]$Folder,
	# Data folder under the project to copy next to the dll (for example 'buildings' or 'events').
	[string]$Data,
	[string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Ultimate General American Revolution'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Folder) { $Folder = $Project }
$csproj = Join-Path $Root "src\$Project\$Project.csproj"
$tmp = Join-Path $env:TEMP "ugar-deploy-$Project"
$dest = Join-Path $GameDir "BepInEx\plugins\$Folder"

if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
dotnet build $csproj -c Release -o $tmp -p:GameDir="$GameDir" -nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Build of $Project failed" }

New-Item -ItemType Directory -Force $dest | Out-Null

function Copy-Locked([string]$src, [string]$dst) {
	try {
		Copy-Item -LiteralPath $src -Destination $dst -Force
	} catch {
		$old = "$dst.old-" + [DateTime]::Now.Ticks
		Move-Item -LiteralPath $dst -Destination $old -Force
		Copy-Item -LiteralPath $src -Destination $dst -Force
		Write-Host "  (the game had $([IO.Path]::GetFileName($dst)) locked; the old copy is now $([IO.Path]::GetFileName($old)))"
	}
}

$dll = Join-Path $dest "$Project.dll"
if ((Test-Path "$dll.disabled") -and -not (Test-Path $dll)) {
	# Switched off in the mod manager: update the disabled copy and leave it off.
	Copy-Locked (Join-Path $tmp "$Project.dll") "$dll.disabled"
	Write-Host "$Project is switched off in the mod manager; updated $Project.dll.disabled."
} else {
	Copy-Locked (Join-Path $tmp "$Project.dll") $dll
	Write-Host "Deployed $Project.dll to $dest"
}

foreach ($m in @((Join-Path $Root "src\$Project\ugar-mod.json"), (Join-Path $Root "src\UGARModManager\manifests\$Folder\ugar-mod.json"))) {
	if (Test-Path $m) {
		# Only when the source is newer: some deployed manifests were updated in place.
		$target = Join-Path $dest 'ugar-mod.json'
		if (-not (Test-Path $target) -or (Get-Item $m).LastWriteTime -gt (Get-Item $target).LastWriteTime) { Copy-Item $m $target -Force }
		break
	}
}
if ($Data) {
	Copy-Item (Join-Path $Root "src\$Project\$Data") $dest -Recurse -Force
	Write-Host "Copied $Data\"
}
