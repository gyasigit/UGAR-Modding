# UGAR Mod Manager

One in-game window that lists every installed BepInEx mod, lets you switch mods on or off, and edits their settings
while you play. Mods don't need to know about the manager: any BepInEx plugin's settings show up automatically.

## For players

### Opening it

Press **F8**, or click the small **Mods** button in the bottom-left corner of the screen.

### What you can do

- **Switch mods on or off.** Every mod is listed by category with an **Enabled** box. Switching a mod off renames its
  `.dll` to `.dll.disabled`, so it takes effect on the next launch (the running copy keeps working until then).
  Switched-off mods stay listed so you can turn them back on. The manager itself can't be switched off.
- **Change settings.** Each mod's settings are edited live and saved straight to its `.cfg` file in `BepInEx\config`:
  - on/off settings get a tick box, keys get a "press a key" button;
  - numbers with a range get a slider;
  - lists of choices get `<` / `>` buttons;
  - anything else gets a text box and **Apply**.
- **Show advanced settings** reveals extra settings (debug logs, window sizes and so on).
- Settings marked **\*** need a restart (or a **Reload** of that mod). A banner lists what's waiting for a restart.
- While you type in a text box or bind a key, the game's own hotkeys are blocked, so typing can't trigger quick-load
  and the like.

### Live reload

Most mods update in the running game, with no restart:

- When a mod's files are updated (a new `.dll`, or one of its data files such as `*.json`), the manager reloads that
  mod about a second later.
- When you edit a mod's `.cfg` file in a text editor while the game runs, the new values are picked up straight away.
- Each mod has a **Reload** button that does the same on demand. It also applies settings marked **\***.

A mod that wasn't loaded when the game started (for example one you just installed) needs one restart first.

### Manager settings

In the window under **UGAR Mod Manager**, or in `BepInEx\config\ugar.modmanager.cfg`:

| Setting | Default | What it does |
|---|---|---|
| `General.ToggleKey` | F8 | Key that opens and closes the window. |
| `General.ShowButton` | on | Show the small **Mods** button. |
| `General.ButtonCorner` | BottomLeft | Corner for the button. |
| `General.Scale` | 1 | Size of the window and button (0.6 to 2.5). |
| `General.BlockGameClicks` | on | *(Advanced)* Stop clicks on the window reaching the game behind it. |
| `LiveReload.ReloadChangedMods` | on | Reload a mod when its dll or data files change. |
| `LiveReload.ReloadEditedSettings` | on | Re-read a `.cfg` file edited outside the game. |
| `LiveReload.Delay` | 1 | *(Advanced)* Seconds to wait after the last file change before reloading. |

The camera may still react to the scroll wheel and keys while the cursor is over the window. Other mods use F7 and F9,
so avoid those keys.

### Troubleshooting

Look in `BepInEx\LogOutput.log` for lines starting with `[Info   :UGAR Mod Manager]`. At start-up you should see
`UGAR Mod Manager 1.1.2 loaded. Press F8 in game to open it.` Each live reload is logged there too (for example
`Reloaded Custom Buildings`). If the window hits an error it switches itself off and logs
`Mod manager window disabled after an error`; the game and your other mods keep running.

## For mod authors

### Registering a mod

There's nothing to reference. Write a normal BepInEx 6 IL2CPP plugin and bind its settings with `Config.Bind`; they
appear in the window automatically. Read `entry.Value` when you use it (not once in `Load`) so changes apply live.

### ConfigDescription tags

Add plain strings as tags in a setting's `ConfigDescription`:

| Tag | Effect |
|---|---|
| `"RestartRequired"` | Marks the setting with **\*** and adds it to the restart banner. |
| `"Advanced"` | Only shown when "Show advanced settings" is ticked. |
| `"Hidden"` | Never shown. |

```csharp
Config.Bind("General", "PatchX", true, new ConfigDescription("Fixes X.", null, "RestartRequired"));
```

Use `AcceptableValueRange` for a slider and `AcceptableValueList` or an enum for `<` / `>` buttons.

### The ugar-mod.json manifest

Optional, but needed for live reload and for a player-friendly name, description and category. Put `ugar-mod.json`
in the mod's own folder under `BepInEx\plugins`, or name it `<DllName>.ugar-mod.json` next to the dll.

```json
{
  "id": "ugar.myfix",
  "name": "My Fix",
  "description": "One or two sentences for players.",
  "author": "...",
  "category": "Fixes",
  "restartRequired": ["General.PatchX"],
  "advanced": ["Window.X"],
  "hidden": [],
  "canDisable": true,
  "hotReload": true,
  "watch": ["*.myfix.json"],
  "reloadWith": []
}
```

| Field | Meaning |
|---|---|
| `id` | Your plugin's BepInEx GUID. |
| `name`, `description`, `author` | Shown in the window. |
| `category` | Group in the window (for example Fixes, Cheats, Information). Default "Other mods". |
| `restartRequired`, `advanced`, `hidden` | Settings named `"Section.Key"`; same effect as the tags above, for mods without tags. |
| `canDisable` | `false` means players can't switch the mod off from the manager. Default `true`. |
| `hotReload` | `false` opts out of live reload. Default `true`. |
| `watch` | File name patterns anywhere under `BepInEx\plugins` that reload your mod when they change. Files in your own folder always do. |
| `reloadWith` | GUIDs of mods you depend on: when one of them is reloaded, yours is reloaded too. |

Field names are case-insensitive; comments and trailing commas are allowed.

### Reloading cleanly

- Mods without a manifest can't be live-reloaded; they need a restart.
- Anything a component creates outside itself (a canvas, a `DontDestroyOnLoad` object) must be destroyed in that
  component's `OnDestroy`.
- Anything else that outlives your components (event handlers on game objects, objects added to game lists, Harmony
  patches you applied by hand) belongs in a `BasePlugin.Unload()` override, which is called before the new copy loads.
- Objects your mod already added to a loaded campaign stay in the game. The new copy should look them up again rather
  than assume a fresh start.
