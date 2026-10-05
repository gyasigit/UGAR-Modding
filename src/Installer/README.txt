UGAR Mod Pack {VERSION}
for Ultimate General: American Revolution (Steam, Windows)
=====================================================================

What's in it
------------
- UGAR Mod Manager: an in-game window to switch mods on or off and change
  their settings. Press F8 in game, or click the small "Mods" button in the
  bottom-left corner.
{MODLIST}
- BepInEx 6 (IL2CPP), the mod loader. It's only installed if you don't
  already have it.

No game files are included or changed. Mods sit in the game's BepInEx folder.


Install
-------
1. Close the game.
2. Unzip this whole package anywhere (for example your Desktop).
3. Double-click Install.bat.
   It finds the game through Steam. If it can't, it asks you for the game
   folder (the folder that contains ugar.exe).
4. Start the game from Steam. If BepInEx was just installed, the FIRST start
   takes a few extra minutes with a black screen or console window while
   BepInEx prepares the game. That's normal. Later starts are fast.
5. In game press F8 to open the mod manager.

Windows SmartScreen or your antivirus may ask about Install.bat because it
was downloaded. Choose "More info" > "Run anyway" if you trust the source.


Using the mod manager
---------------------
- Each mod has an "Enabled" box. Switching a mod on or off takes effect the
  next time you start the game (the window tells you when a restart is due).
- Click a mod's name to see its settings. Most settings apply immediately.
  Settings marked with * need a restart.
- "Show advanced settings" reveals extra options such as window positions.
- Settings are saved in the game's BepInEx\config folder (*.cfg files), so
  they survive updates of the mod pack.


Update
------
Run Install.bat from the newer package. Your settings are kept, and mods you
switched off stay off.


Uninstall
---------
Close the game and double-click Uninstall.bat. It removes the mods and their
settings. If this pack installed BepInEx and no other BepInEx mods are left,
it offers to remove BepInEx too, which returns the game to unmodded.

If the game misbehaves after a Steam update, run Uninstall.bat (or switch mods
off in the manager) and check for a newer mod pack.


Command-line options (for Install.bat / Uninstall.bat)
-----------------------------------------------------
  -GameDir "<folder>"  use this game folder instead of searching Steam
  -KeepBepInEx         uninstall: keep BepInEx
  -KeepSettings        uninstall: keep the mods' .cfg files
  -Quiet               no questions, no "press Enter" at the end


Problems?
---------
The log is in <game folder>\BepInEx\LogOutput.log. Please include it when
reporting a problem.

Third-party software included: see THIRD-PARTY.txt.
