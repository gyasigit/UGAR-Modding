# Mining Gold

In the base game, building Mining Infrastructure in a region with gold raises iron and other ore output a lot, but
adds only a little gold income. This mod makes each Mining Infrastructure level raise a gold region's gold income by
the same rule it raises ore, and shows the gain on the building card.

## How to use it

Nothing to do: it's on by default.

- Gold regions are the ones with the gold icon among their resources.
- Open the region panel and look at the **Mining Infrastructure** card. Under the usual ore lines there is now a gold
  line, for example **+1100** in green: the extra money per week your settlements in that region get from the next
  level.
- Build the level as usual. From then on the extra gold is part of your settlements' weekly income.

### How much gold?

Each settlement's gold grows by the same multiplier its ore output gets from the mining level. That multiplier
depends on how many settlements share the region, the region's loyalty to you and prisoners, just like ore.

Example: a region with one settlement at full loyalty and mining level 1. Ore output roughly doubles, so the gold term
goes from the game's 1200 to about 2100 per week. Your normal income bonuses (policies, Income Collection department,
difficulty) then apply on top, as they do for all income.

The mod never gives less than the game's own gold bonus. Everything else in the income calculation is unchanged.

## Settings (F8)

Open the [mod manager](mod-manager.md) with **F8** and find **Mining Gold** in the **Fixes** category.

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | on | Mining Infrastructure also boosts gold income, and the card shows the gold line. Off = the game's normal rule. |
| `General.AllNations` | on | *(Advanced)* Apply the rule to every nation's settlements, as the game's own rules do. Off = only yours. |
| `Debug.WeeklyLog` | on | *(Advanced)* Each week, write every gold region's ore and gold multipliers and the extra gold to the log. |

Settings are saved in `BepInEx\config\ugar.mininggold.cfg` and apply straight away.

## Compatibility and saves

- Nothing is stored in your save. The extra gold is recalculated each week, so removing or switching off the mod
  simply returns to the game's normal gold income.
- With `AllNations` on (the default), AI nations get the same boost in their gold regions, which keeps things fair.
- The game's income previews include the extra gold. A few other money displays may still show the game's own
  smaller number.
- [Resource Breakdown](resource-breakdown.md) isn't affected: it covers goods such as ore and wood, not money.
- After installing, restart the game once so the mod gets loaded.

## Troubleshooting

Open `BepInEx\LogOutput.log` in the game folder and look for lines starting with `[Info   :UGAR Mining Gold]`.

- At start-up: `UGAR Mining Gold 1.0.0 loaded (gold boost on)`.
- With `WeeklyLog` on, each week you get a block starting `Mining gold <date> <state>:` with one line per gold
  settlement: the iron multiplier, the gold value vs the game's, and the extra income.
- If that block ends with `income check: N of M settlements differ`, the mod's numbers didn't match the game's for
  some settlements. Please report it with the log.
- `No free line on the Mining Infrastructure card for gold` means the card had no room for the gold line. The extra
  gold is still paid; only the display is missing.
