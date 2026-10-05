# British Events

New campaign events for Great Britain. When an event's conditions are met it appears in the game's own event window
and asks for a price (money, supplies, specialists and/or goods from the home storage). Pay to get its effect, or
pass for nothing. Effects include renting more factories and shipyards on the England screen, a population boost for
the home settlements, and permanent country modifiers.

## Using it in game

Just play a British campaign. Events are checked once per in-game day; when one fires you'll see a window with two
buttons:

- **Pay** shows the price in brackets. It is greyed out (with what you're short of) when Britain can't afford it.
- **Pass** costs nothing and gives nothing.

Either way, the event has been used up (unless you make it repeatable, see below). The price is rolled at random
within a range each time. The effects are listed at the end of the event text.

### Included events

| Event | When | Price | Effect if paid |
|---|---|---|---|
| Army Extraordinaries | from 1 Feb 1776 | 3,000-6,000 money, 15-30 supplies, 1-2 specialists | +5 factories you can rent |
| Navy Board merchant yards | from 15 Sep 1776, if you hold New York, Halifax, Newport, Philadelphia, Charleston or Savannah | 4,000-8,000 money, 20-40 supplies, 1-3 specialists | +5 shipyards you can rent |
| Carron carronades | from 1 Jan 1779, after paying for Army Extraordinaries | 2,500-5,000 money, 10-25 supplies, 0-1 specialists | +3 factories, +3 shipyards |
| The Emigration Ships Are Stayed | from 1 Sep 1775, if the home storage has trade goods | 50-100 Provisions, 25-50% of one trade good in the home storage, 1,000-2,500 money | +30% population in every home settlement |

The game normally lets Britain rent at most 30 factories and 30 shipyards; paid events raise those limits.

## F8 settings

Open the mod manager with **F8** and pick **British Events** (see [mod-manager.md](mod-manager.md)).

| Setting | What it does |
|---|---|
| `General.Enabled` | Fire the mod's events and apply the extra factory/shipyard limits. Off: nothing fires and the limits are the game's own (rentals already above the limit stay until you lower them). |
| `Repeatable events` → `<event id>` | On: the event can fire again after the interval below, and each payment stacks its effects. Off: once per campaign. |
| `Repeatable events` → `<event id> days` | In-game days (7-3,650) after an event last fired before it can fire again. |
| `Debug.LogChecks` (advanced) | Log each day why an event hasn't fired yet. |
| `Debug.WriteInfo` (advanced) | When a campaign loads, write the rent limits, settlement names and game event picture names to `BepInEx\config\ugar.britishevents.info.txt`. Useful when writing events. |
| `Debug.ForceEvent` (advanced) | Type an event id to fire it on the next in-game day, ignoring its conditions and whether it already fired. Clears itself after use. |

Repeatable settings take effect straight away; no restart needed.

## Writing your own events

Put a `*.event.json` file anywhere under `BepInEx\plugins` (the mod's own are in `UGARBritishEvents\events`). A file
can hold one event or a list `[ {...}, {...} ]`. `//` comments are allowed. With the F8 mod manager installed,
changed files are picked up without restarting.

```json
{
  "id": "army-extraordinaries",              // required, unique, never change it later
  "title": "Parliament Votes the Army Extraordinaries",
  "text": "Lord North has carried the House ...",
  "nations": ["Britain"],                    // default Britain
  "repeatable": false,                       // default for the F8 setting
  "repeatAfterDays": 365,                    // default for the F8 setting
  "cost": {                                  // optional: each a number or a [min, max] range
    "money": [3000, 6000],
    "supplies": [15, 30],                    // construction materials
    "specialists": [1, 2],                   // officers from the reserve pool
    "homeGoods": { "Provision": [50, 100] }, // goods from the home (England) storage
    "homeStock": { "kinds": 1, "share": [0.25, 0.5] }  // N trade goods picked from what is stored at home, a share of each
  },
  "payButton": "Fund the contracts",         // default "Pay"
  "passButton": "The Treasury cannot spare it",  // default "Pass (no cost, no effect)"
  "button": "Very well",                     // only for events without a cost
  "image": "my-picture.png",                 // optional: png/jpg next to the json file
  "gameImage": "SomeParliamentEvent",        // optional: borrow a game event's picture by name
  "trigger": {
    "from": "1776-02-01",                    // yyyy-MM-dd, inclusive
    "until": "1781-10-19",
    "chancePerWeek": 0.35,                   // 0..1, default 1
    "minSettlements": 5,                     // owned settlements in America
    "ownsAll": ["New York"],
    "ownsAny": ["Boston", "Philadelphia"],
    "requires": ["navy-board-merchant-yards"],   // other events that must have been paid for
    "minRentedFactories": 30,
    "minRentedShipyards": 0
  },
  "effects": {
    "maxFactories": 5,                       // more factories Britain can rent (can be negative)
    "maxShipyards": 5,
    "homePopulationPercent": 0.3,            // each home settlement +30% population
    "homePopulation": 12000,                 // flat population, split over the home settlements
    "modifiers": [                           // optional permanent country modifiers
      { "modifier": "RECRUITS_PERCENT", "value": 0.1, "percent": true }
    ]
  }
}
```

Tips:

- An event without a `cost` applies its effects at once and shows a plain window with one button.
- Effects apply only when the player pays. The price and effect lines are added to the text automatically.
- Home goods names: `Provision`, `Horse`, `Wood`, `Iron`, `Coal`, `Saltpeter`, `Cotton`, `Sugar`, `Tobbaco` (sic),
  `Rum`, `Cigares` (sic), `Furs`, `Tea`, `Spices`, `Luxury`, `Musket`, ...
- The info file (`ugar.britishevents.info.txt`) lists settlement names for `ownsAll`/`ownsAny`, picture names for
  `gameImage`, and what the home storage holds.
- Avoid `{` and `}` in `title` and `text`.
- Without an image, the picture of Britain's first parliament event is used.

## Saves and compatibility

- Fired and paid events are stored in your save. A save opened without the mod still loads; the extra rent limits
  just stop applying.
- **Don't rename an event `id`** once it's in use, or it will fire again.
- Limit bonuses are worked out from the current json files, so editing an event's numbers also changes campaigns
  where it already fired. Removing a file removes its bonus.
- Population boosts and `modifiers` become part of the save and stay even if the mod is removed.
- If you save while an event's question is still open, reloading counts it as passed.

## Troubleshooting

Look in `BepInEx\LogOutput.log` for lines from `UGAR British Events` that start with `British events`, for example
`campaign loaded (...) Factory cap 30+0 ...`, `fired "<id>", asking ...`, `"<id>" applied (paid ...)` or
`passed on "<id>"`. Problems reading an `*.event.json` file are logged as warnings. Turn on `Debug.LogChecks` to see
why an event isn't firing.
