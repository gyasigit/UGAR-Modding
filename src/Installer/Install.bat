@echo off
rem Installs the UGAR Mod Pack. Extra options: see README.txt
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Install-UGARMods.ps1" %*
