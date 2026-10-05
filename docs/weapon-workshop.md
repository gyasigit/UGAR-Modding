# UGAR Weapon Workshop

Design your own muskets and rifles from the guns you have unlocked, then improve them as **Mk II, Mk III...** next to
the old version. Every design is a real game weapon: factories make it, it sits in storage, companies carry it, and
battles use its numbers.

## Opening the workshop

- Open the **production screen** and click **WORKSHOP** among the filter tabs (MUSKETS, CANNONS, SHIPS, SUPPLY).
  Click it again to close.
- Or press **F10** anywhere on the campaign map (setting `General.Hotkey`). F10, **Esc** or the **X** closes it.

The window sits to the right of the order list (or in the middle of the screen when the production screen is closed).
Clicks on it never reach the map.

## Designing a weapon

Click **NEW DESIGN**. The designer starts from your best unlocked gun. Pick:

| Part | Options | What it does |
|---|---|---|
| **Base** `<` `>` | Any musket, carbine or rifle you have **researched**, or have at least one of **in storage** (bought or captured) | Starting numbers, and which unit types can carry it |
| **Barrel** | Short / Standard / Long | Short: reloads faster (×0.88), shorter range (×0.9), weaker at long range, cheaper; cavalry can carry it. Long: range ×1.08, stronger at long range, reload ×1.06, more iron |
| **Rifling** | Smoothbore / Rifled | Rifling a musket: range ×1.2, accuracy +0.15, reload ×1.7, melee ×0.85, 2.2× factory work. Needs a rifle unlocked. Rifles are always rifled |
| **Bayonet** | None / Socket / Sword | None: melee ×0.55, reload ×0.97, cheaper. Socket: as the base. Sword: melee ×1.3, reload ×1.03, a little more iron |
| **Quality** | Rough / Standard / Fine / Masterwork | Better quality: more accurate and faster to reload, but costs more (×0.8, 1, 1.3, 1.75) |

The comparison shows the base (grey bar) and the design (green = better, red = worse) for efficiency, range,
reloading, accuracy and melee, then price, factory work, money and iron per gun. **RENAME** opens a text field: type
the name, Enter or OK keeps it, Esc or CANCEL discards it. While you type, the game's map hotkeys are off.

**CREATE DESIGN** pays a one-time **development cost** (400 + 30 × the gun's price, rounded to 50; see
`Balance.DevelopmentCostMultiplier`) and adds the design to your nation's research. Then:

- order it under MUSKETS in the production screen like any musket (it uses your factories, money and iron);
- once it's in storage, pick it in a company's weapon list when recruiting or re-arming. Bulk Recruit's "best weapon"
  option picks it automatically when it's the best you have enough of.

**Caps:** you can't jump ahead of your technology. Each number is capped at the best unlocked weapon's value plus a
small margin (range +5%, reload 5% faster, accuracy +0.03, melee +10%). The designer says when a cap applied. Better
guns also cost more to make: price, factory work and money per gun scale with the design's performance.

## Improving a design (Mk II)

Click **MK II** on a design card. The designer opens with its parts and the name "... Mk II". Change what you like and
create it. **The old design stays as it is.** Guns already made keep their stats; re-arm regiments as the new guns are
produced or bought. A Mk costs 75% of the normal development cost.

## Archiving, deleting and renaming

| Action | Where | What happens |
|---|---|---|
| **ARCHIVE** | design card | Moved to ARCHIVED and taken off the MUSKETS order list; its factory orders are cancelled (orders pay per finished gun, so nothing is lost). Guns already made keep working. |
| **RESTORE** | archived card | Back in the DESIGNS list and orderable again. |
| **DELETE** | archived card | Only when **nothing uses it**: no guns in storage or at the market, no factory orders, no company carrying it. Otherwise the card says what still uses it, e.g. "In use: carried by 3 companies in 1 regiment, 120 in storage". Asks for confirmation. **No refund** of the development cost. |
| **RENAME** | design card | Changes the name shown everywhere. |

All of these are saved with your campaign. If guns of a deleted design turn up again (an older save, loot), they still
work as long as the Weapon Workshop is installed.

## Settings (F8)

Also in `BepInEx\config\ugar.weaponworkshop.cfg`.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | true | Off: the WORKSHOP tab and hotkey are gone. Designs already in a save keep working. |
| `General.Hotkey` | F10 | Key that opens and closes the workshop (a Unity KeyCode name, e.g. F10, W). |
| `Balance.DevelopmentCostMultiplier` | 1 | 0 = free designs, up to 5. |
| `Window.Scale` | 1 | Window size, 0.5 to 3 (advanced). |

## Saves: important

**A save that contains a design needs this mod to load.** Without it the game can't find the weapon and the load
fails. To stop using the mod: sell or use up the designed guns, re-arm companies with normal weapons, and keep the
mod installed for any save made while designs existed.

If a design can't be rebuilt (for example its base weapon no longer exists), it loads as a plain trade musket named
"Unknown workshop weapon" instead of breaking the save.

## Limits

- Designs are per nation and only for the player; the AI never designs weapons.
- Artillery can't be designed.
- Market buy lists don't offer designs (selling works through the normal market screen).
- Other mods' F-key hotkeys still work while you type a name.

## Troubleshooting

Look in `BepInEx\LogOutput.log` for lines from `UGAR Weapon Workshop`:

- `Workshop tab added under the production filters.` If you see `Workshop tab not added` instead, use the hotkey.
- `Weapon design <name> (...)`: one line per design when a campaign loads, with its stats.
- `Weapon design "..." can't be rebuilt`: that design loaded as "Unknown workshop weapon".
- `Weapon Workshop window disabled after repeated errors`: include the log when reporting the problem.
