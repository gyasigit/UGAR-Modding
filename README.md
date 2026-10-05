# UGAR Mod Pack

Mods made for **Ultimate General: American Revolution** (Steam, Windows), built on BepInEx 6 (IL2CPP).
Not affiliated with or endorsed by Game-Labs. Contains no game files.

| Folder | What |
|---|---|
| `src/` | One folder per mod (BepInEx plugins), the in-game mod manager, ModKit (library for other modders) and the player installer |
| `docs/` | How each mod works and the modder guide ([docs/modkit.md](docs/modkit.md)) |
| `tools/` | `build-dist.ps1` builds the player zip, `deploy-mod.ps1` installs one mod into a running game |

Build the player package: `powershell -ExecutionPolicy Bypass -File tools\build-dist.ps1 -Version x.y.z`
(needs the .NET SDK and the game with BepInEx run once, for the interop assemblies).

Player instructions: [src/Installer/README.txt](src/Installer/README.txt).
