# Instant Construction

A cheat for Ultimate General: American Revolution. When it's switched on, your settlement buildings, upgrades and
repairs finish as soon as you order them. You still pay their normal cost, and AI nations keep building at the normal
speed.

## How to use it

1. Press **F8** (or click the small **Mods** button) to open the [mod manager](mod-manager.md).
2. Open the **Cheats** category and find **Instant Construction**.
3. Tick **Enabled**. The change applies straight away, no restart needed.

Now order a building, upgrade or repair in any of your settlements. It shows as built at once, with the game's usual
"construction finished" notification, and the cost is taken as normal.

Things that were already waiting in a settlement's build queue when you switched the cheat on finish on the next
in-game day. Untick **Enabled** to go back to normal build times for new orders.

## Settings (F8)

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | off | Turns instant construction on or off for your settlements. |
| `General.LogFinished` | on | *(Advanced)* Writes each instantly finished construction to the BepInEx log. |

Settings are saved in `BepInEx\config\ugar.instantbuild.cfg`.

## Compatibility and saves

- Only your own settlements are affected. AI nations build normally.
- The mod doesn't add anything to your save. Buildings it finishes are ordinary finished buildings, so your saves keep
  working if you remove the mod later.
- The usual "can you build / can you afford it" checks still apply. The cheat only skips the waiting time.
- If you install the mod while the game is running, restart the game once so it gets loaded.

## Troubleshooting

Open `BepInEx\LogOutput.log` in the game folder and look for lines starting with `[Info   :UGAR Instant Construction]`.

- At start-up you should see `UGAR Instant Construction 1.0.0 loaded (instant construction off)` (or `on`).
- With `LogFinished` on, every finished order is logged as `Finishing <building> in <settlement>`.
- If something goes wrong, an error line such as `Finishing constructions failed` appears under the same prefix.
  Include it when you report a problem.
