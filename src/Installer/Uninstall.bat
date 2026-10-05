@echo off
rem Removes the UGAR Mod Pack (and BepInEx, if this pack installed it).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Install-UGARMods.ps1" -Uninstall %*
