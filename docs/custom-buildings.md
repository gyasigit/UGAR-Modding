# Custom Buildings

Adds new settlement buildings to the game's own build menu. They cost resources, take construction time, apply real
effects and are saved with your campaign. Modders can add more with simple `*.building.json` files.

## The buildings

| Building | Who | Where | Effect |
|---|---|---|---|
| **Town Expansion** | everyone | towns, ports | Doubles the settlement's weekly population growth (so its workforce and recruits grow faster) and +10% recruits. Costs 1.5x a Granary. |
| **Loyalist Muster Field** | Britain | towns, ports, forts | +2 to Britain's militia regiment limit for each one you finish (they stack). Costs the same as a Barracks. |
| **Governor's Assembly Rooms** | Britain | towns, ports, max 2 | Every week uses 3 tea + 2 spice + 1 luxury goods for +8 renown. Costs 2,400 money, 15 construction materials, 2 specialists. |
| **Coffeehouse** | USA | towns, ports, max 2 | Every week uses 3 tea + 2 spice for +6 renown. Costs 1,600 money, 10 construction materials, 1 specialist. |

## Using it in game

Open a settlement you own and look in the build menu: the new buildings are listed with their picture, cost, build
time and effect lines. Each settlement can have one of each.

- **Muster Field**: hover the army number in the top bar to see your max militia rise when one finishes. A damaged
  Muster Field counts in proportion to its condition.
- **Assembly Rooms / Coffeehouse**: tea, spice and luxury goods are bought from overseas trade partners. Goods are
  taken from the colony storage first, then England's. If anything is short, nothing is used and no renown is given
  that week. The building's tooltip shows each good as have/need (green or red), how many weeks you can cover, the
  next reception date and the last result. Renown follows the game's usual diminishing returns.

## F8 settings

Open the mod manager with **F8** and pick **Custom Buildings** (see [mod-manager.md](mod-manager.md)).

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | on | Offer custom buildings and apply their effects. Off: saves with them still load, but they do nothing, can't be built, and the game may remove and refund them. Needs a restart. |
| `Notifications.ReceptionSkipped` | on | Notify you when a weekly reception is skipped for missing goods. |
| `Notifications.ShortNextWeek` | on | Notify you right after a reception if the goods left won't cover next week. |
| `Notifications.ReceptionHeld` | off | Notify you every time a reception is held, with the renown gained. |
| `Debug.LogGrowth` (advanced) | on | Log each week's extra population and workforce. |
| `Debug.DumpGameBuildings` (advanced) | on | At game start, write the game's own buildings to `BepInEx\config\ugar.custombuildings.game-buildings.txt`. |

## Adding your own building

Drop a `*.building.json` file anywhere under `BepInEx\plugins` (the mod's own are in
`UGARCustomBuildings\buildings`). One file can hold one building or a JSON array of them; `//` comments are allowed.
Restart the game, or let the F8 mod manager reload it.

```jsonc
{
  "id": "mymod.field_hospital",          // required, unique, stored in saves: never change it later
  "name": "Field Hospital",
  "description": "Text shown in the build menu.",

  "template": ["Church", "Schoolhouse"],  // game building(s) to copy the picture from; first match wins
  "settlementTypes": ["TOWN", "FORT"],    // TOWN, FORT, PORT; empty = where the template can be built
  "nations": ["USA", "Britain"],          // empty = everyone
  "aiCanBuild": false,                    // AI nations also get it
  "requires": ["Barracks"],               // game buildings that must be finished in the settlement first
  "maxPerNation": 2,                      // optional cap across the whole nation (0 = none)

  "cost": { "multiplier": 1.0 },          // x template cost, or an absolute list:
  // "cost": [ { "resource": "Money", "amount": 2000 }, { "resource": "ConstructionMaterial", "amount": 20 } ],
  "buildPoints": { "multiplier": 1.0 },   // x template, or a number: "buildPoints": 300
  "upkeep": [ { "resource": "Money", "amount": 10 } ],  // optional
  "indestructible": false,

  "effects": {
    "populationGrowthBonus": 0.5,         // +50% of the settlement's natural weekly population growth
    "weeklyWorkforce": 20,                // flat workforce every week
    "modifiers": [                        // settlement modifiers, while the building stands
      { "modifier": "RECRUITS_PERCENT", "value": 0.05 },
      { "modifier": "LOYALTY_INCREMENT", "value": 1, "percent": false }
    ],
    "countryModifiers": [                 // nation-wide, summed over all such buildings the owner has
      { "modifier": "BLOCK_MILITA", "value": 2 }   // +2 militia regiment limit
    ],
    "weeklyConversion": {                 // player only: every week, if storage holds all of these, use them for renown
      "consume": [ { "resource": "Tea", "amount": 3 }, { "resource": "Spices", "amount": 2 } ],
      "renown": 6
    }
  }
}
```

Names you can use:

- **Templates / requires**: the first column of `ugar.custombuildings.game-buildings.txt`, which also lists every
  game building's real cost, build points and effects for balancing. Common ones: Armory, Barracks, BlacksmithShop,
  Church, CustomHouse, Docks, FurTraderHouse, Granary, LumberMill, PrintingPress, PrisonersCamp, RumDistillerHouse,
  Schoolhouse, Stable, Stockade, TobacconistShop, TownHall, Warehouse, WeaverHouse.
- **Resources**: Money, ConstructionMaterial, Specialist, Wood, Iron, Copper, Coal, Saltpeter, Horse, Recruit, Wheat,
  Provision, Ammunition, Musket, Cannon, Furs, Textiles, Cigares, Rum, Cotton, Sugar, Tobbaco (sic), Tea, Spices,
  Luxury, ...
- **Settlement modifiers**: RECRUITS_PERCENT, RECRUITS_LOYALTY_PERCENT, LOYALTY_INCREMENT, INCOME_PERCENT,
  TAX_PERCENT, PROVISION_INCREMENT, PRODUCTION_POINTS_PERCENT, CONSTRUCTION_POINTS_PERCENT, FORTIFICATION_LEVEL, ...
  Percent values are fractions (0.1 = +10%).
- **Country modifiers**: BLOCK_MILITA (militia limit), ARMY_LIMIT, GENERAL_LIMIT, NAVY_LIMIT, ...

`percent` only changes how the value is displayed (default true for `modifiers`, false for `countryModifiers`).
Upgrades of custom buildings are not supported.

## Saves and compatibility

- **Saves that contain a custom building need this mod to load.** To uninstall cleanly, demolish your custom
  buildings (or turn `General.Enabled` off and play on), save, then remove the mod.
- If the mod is installed but a building's json file is gone, the save still loads; the building becomes an empty
  "Removed modded building" you can demolish.
- Never change a building's `id` once it's been built in a save.
- Works with [Recruit Growth](recruit-growth.md), which shows the extra workforce from custom buildings.

## Troubleshooting

Look in `BepInEx\LogOutput.log` for lines tagged `UGAR Custom Buildings` and search for `Custom building` /
`Custom buildings`. At start it logs how many building files loaded and each building's final cost
(`Custom building Town Expansion (ugar.town_expansion): template Granary, ...`). Muster Field changes show as
`Custom buildings: Britain BLOCK_MILITA is now X`. Errors in a `*.building.json` file are logged as warnings.
