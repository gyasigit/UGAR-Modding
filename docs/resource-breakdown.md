# UGAR Resource Breakdown

Explains the resource summary on the **Production** screen: where each resource comes from, what uses it, and whether
your stock is growing or shrinking. It only reads the game's numbers; it changes nothing in the game or your save.

## What the Production screen numbers mean

The summary block on the Production screen shows six resources (coal, copper, iron, saltpeter, wood, cloth) as
**`N (M)`**, plus factories and shipyards as **`used / total`**:

- **N** is what's in storage right now (your colony storage; on Britain's England screen, the England storage).
- **(M)**, the number in parentheses, is what your **running production orders use per day** at full speed. It's
  hidden when nothing uses the resource. It is *not* income: income arrives once a week.
- **Factories `35/35`** and **shipyards (anchor) `0/30`**: plants in use / plants available. Orders take free plants from
  the top of the order list down, each up to its own maximum.

## How to use it

Click a resource (its icon or number) in that block to pin a popover beside the panel, in the game's own tooltip style:

- **Top line**: in storage, used per day, and the net change per week.
- **Coming in, each week**: every region that produces it, with its raw output and the multiplier the game applies,
  and every settlement conversion building that makes it (the Weaver's makes cloth from cotton, ...) or uses it as an
  input. The last line is what the game actually delivered in the latest weekly update.
- **Used by production, each day**: every running order that consumes it (plants, items per day, amount per item), the
  Resource Consumption department effect if you have one, and a warning when orders are short of it.
- **Ships and storage** (when relevant): the other storage (England / colony), cargo at sea in each direction, and cargo
  waiting in ports.
- **Net**: produced per week, used per week, net per week, and how many days until storage runs out at this rate.

Click the factory or shipyard numbers for the order list with the plants each order holds, items per day and progress.

Click anywhere else, press **Esc**, or close the Production screen to close the popover. The popover blocks clicks so
they don't reach the map, and refreshes every second.

## How resources move (in short)

1. **Weekly income.** Each region you hold produces ore and goods. Mined resources are multiplied by the state's
   resource extraction rate and your Resource Extraction department effect, and cost money (mining expenses).
   A state with an extraction rate of 0 delivers nothing.
2. **Weekly conversion.** Settlement buildings with a conversion (Weaver's, Rum Distiller's, Tobacconist's) turn their
   inputs into goods once a week, if your storage has the inputs.
3. **Daily use.** Each day every production order makes as many items as its plants, resources and money allow, and
   uses its components (adjusted by the Resource Consumption department effect).
4. **Everything else** (market purchases, trade deliveries, loot, events, cargo ships between England and America) adds
   to storage when it happens. The popover shows cargo at sea and in ports, but doesn't forecast purchases or trade.

## Settings (F8)

Also in `BepInEx\config\ugar.resourcebreakdown.cfg`.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | true | Turn the mod on or off. |
| `Window.Scale` | 1 | Width multiplier for the popover (advanced). |
| `Debug.VerifyLog` | true | After each weekly update, log the predicted vs actual deliveries (advanced). |

## Compatibility

- Read-only, so it's safe to add or remove at any time; saves are not affected.
- To remove it, switch it off in the mod manager (F8) or delete `BepInEx\plugins\UGARResourceBreakdown`.

## Limits

- The weekly figures use this week's region values. Changes since the last weekly update (captures, loyalty,
  buildings) show up after the next one.
- Small differences for coal and copper between the predicted weekly figures and the "last delivery" line are possible;
  the "last delivery" line is always what the game actually gave you.

## Troubleshooting

Look in `BepInEx\LogOutput.log` for lines from `UGAR Resource Breakdown`. With `Debug.VerifyLog` on, each weekly
update writes a line starting `Resource breakdown check`, comparing predicted and actual deliveries; mismatches are
logged as warnings. If you report a problem, include those lines.
