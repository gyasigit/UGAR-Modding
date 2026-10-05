# Bulk Recruit

Create several identical regiments from one design in a single click, reload the last design you built for a unit
type, and give new companies the best weapon you have in storage. It can also let understrength towns reinforce from
their own recruits.

## Using it in game

1. Open a settlement's recruit screen and pick a unit type (infantry, cavalry, supply, militia ...) as usual.
2. A **Bulk Recruit** card appears beside the screen: **[ - ]  N  [ + ]  [ Max M ]  [ 1 ]**. **Ctrl+=** and
   **Ctrl+-** also change N.
3. The card shows the **total cost of N regiments** (money, officers, renown, each weapon and item, soldiers) next to
   what you have, in red when you're short.
4. N can't go above what you can afford (**Max M**; the card says what limits it).
5. Customize the regiment once and press the game's **Create** button. The game makes the first regiment and the mod
   makes the rest the same way, then reports the result on the card.

Every copy costs the same as the first and is paid through the game's normal rules.

### Other features

- **Replacement weapons**: if your chosen weapon runs out partway, later regiments get the best other weapon the unit
  can carry. The card lists each swap before you press Create (e.g. "Swap: Brown Bess 69 instead of Brown Bess 76 in
  regiments #4-5"). The first regiment is always built exactly as designed.
- **Commanders**: each regiment needs its own officer. Extra copies get your best free officers from the reserve.
  The card shows how many you have.
- **Names**: copies use the game's automatic names. A custom name gets " 2", " 3" and so on.
- **Use last design**: after creating a regiment of a type, reopening that type's recruit screen shows a
  **Use last design (X companies)** button that loads the same companies and weapons. Designs are remembered until
  you close the game. It doesn't re-check which company types the current settlement allows; costs are still checked.
- **Best weapon by default**: a newly added company gets the best weapon (highest price) you have enough of for a
  full company, instead of the template default. You can still change it, and a weapon you picked by hand is kept.
- **Local reinforcement**: normally a colonial town's regiments only get new men by supply wagon from towns whose own
  garrisons are full. With this on, a town refills its own garrison from its recruits each day when no supply wagon is
  on the way. Companies still need weapons and horses in stock.

Only the Create screen is affected. Mercenary hiring, converting and editing existing regiments are unchanged.

## F8 settings

Open the mod manager with **F8** and pick **Bulk Recruit** (see [mod-manager.md](mod-manager.md)).

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | on | Turn the card and all features on or off. |
| `General.MaxCopies` | 20 | Most regiments one Create click can make (1-100). |
| `General.AutoCommanders` | on | Give extra copies the best free reserve officers. Off: copies stop once your chosen officer is used. |
| `General.AutoBestWeapon` | on | Equip new companies with the best weapon you have enough of. |
| `General.RememberDesigns` | on | Offer the "Use last design" button. |
| `General.VerboseLog` | on | Write extra detail (costs, stock, weapon choices) to the log. |
| `General.ResetCopiesOnOpen` (advanced) | on | Reset the count to 1 every time the recruit screen opens. |
| `Reinforcement.LocalReinforcement` | on | Let towns reinforce their garrison from their own recruits. |
| `Reinforcement.MaxPerDay` | 50 | Most recruits one town turns into reinforcements per day. |
| `Reinforcement.MinTownRecruits` | 10 | A town needs at least this many recruits before it reinforces locally. |
| `Window.X` / `Window.Y` (advanced) | -1 / 90 | Position of the card in pixels at 1080p (-1 centres it). |
| `Window.Scale` | 1 | Size of the card (0.5-3). |

## Saves and compatibility

The mod adds nothing to your save: the regiments it creates are ordinary game regiments. You can remove it at any
time by deleting `BepInEx\plugins\UGARBulkRecruit`. If a game update breaks the card, a simpler box is shown instead.

## Troubleshooting

Look in `BepInEx\LogOutput.log` for lines tagged `UGAR Bulk Recruit`. With `General.VerboseLog` on they include
the costs, stock and weapon choices for each Create click.
