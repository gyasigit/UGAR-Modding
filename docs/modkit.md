# UGAR ModKit: writing your own UGAR mod

UGAR ModKit (`ugar.modkit`) is a library mod. On its own it does nothing: other mods reference `UGARModKit.dll` and
get ready-made helpers for the parts of Ultimate General: American Revolution that are hard to work out yourself.

> **Preview:** ModKit 0.x (API level 1) is a preview. Its API may still change between minor versions. From 1.0 on,
> only a major version will remove or change public members.

| Class | What it does |
|---|---|
| `Game` | Player country, nation, today's date, settlements; events `CampaignLoaded` and `NewDay` |
| `SaveData` | Flags and numbers stored **inside the campaign save**, per country |
| `Treasury`, `Price` | Read/add money and supplies; check and pay a price (money, supplies, specialists) |
| `Popups`, `Option` | The game's own event window (message) and question window (several answers, greyed out when blocked) |
| `Modifiers` | Add country-wide modifiers (`EModifier`), saved with the campaign, permanent or timed |
| `Images` | Load a png/jpg as a sprite, or borrow a game event picture |
| `Content` | Which UGAR mods are installed; read custom buildings in a settlement |

The full API with comments ships as `UGARModKit.xml` next to the dll, so your editor shows it.

## Adding content without code

You don't need ModKit (or C#) to add buildings or events. UGAR Custom Buildings reads every `*.building.json` and
UGAR British Events every `*.event.json` **anywhere under `BepInEx\plugins`**. Put your files in your own folder,
for example `BepInEx\plugins\MyBuildings\granary.building.json`. Formats: [custom-buildings.md](custom-buildings.md),
[british-events.md](british-events.md).

## Your first code mod

What you need: the game with the UGAR Mod Pack installed (it installs BepInEx and ModKit) and started once, and the
.NET SDK (6 or newer).

1. Copy the example project `src/UGARModKitExample` and rename the folder, `.csproj` and namespace.
2. In `Plugin.cs`, give it your own GUID and name: `[BepInPlugin("yourname.mymod", "My Mod", "0.1.0")]`.
   Keep `[BepInDependency(ModKit.Guid)]` so BepInEx loads ModKit first (and skips your mod, with a log line, if ModKit
   is missing).
3. If the game isn't in the default Steam folder, build with `-p:GameDir="D:\...\Ultimate General American Revolution"`.
4. `dotnet build -c Release`, then copy your dll to `BepInEx\plugins\MyMod\`. The F8 mod manager lists it with its
   settings. Add a `ugar-mod.json` for a description and live reload (see [mod-manager.md](mod-manager.md)).

The example: once per campaign, a few days after loading, a merchant offers 10 supplies for 500 money.

```csharp
[BepInPlugin("ugar.example.merchant", "UGAR ModKit Example", "0.1.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
{
    public override void Load() => Game.NewDay += OnNewDay;

    static void OnNewDay(Country country, DateTime today, int days)
    {
        if (SaveData.HasFlag(country, "example.merchant.offered")) return;
        if (SaveData.AddNumber(country, "example.merchant.days", days) < 3) return;
        SaveData.SetFlag(country, "example.merchant.offered");

        Popups.Ask("A Travelling Merchant", "A merchant offers to sell his whole load to the army.",
            Option.Paid("Buy the load", new Price(money: 500), () => Treasury.AddSupplies(country, 10)),
            new Option("Send him away"));
    }
}
```

The full example in the project also has two settings (`General.Enabled`, `General.AfterDays`) and shows a thank-you
message after buying.

## API notes

### Game

- `Game.CampaignLoaded(country)`: once per campaign load, including after returning from a battle (the campaign is
  rebuilt). Use it to (re)apply state, not to show a popup every time.
- `Game.NewDay(country, today, days)`: when the date moves forward. `days` can be more than 1 at high speed. Jumps over
  60 days (loading a save) are not reported.
- `Game.PlayerCountry`, `Game.PlayerNation`, `Game.Today`, `Game.InCampaign`, `Game.Settlements(owner)`,
  `Game.FindSettlement(name)`.
- Handlers run on the main thread. An exception in your handler is logged with your assembly name and doesn't stop
  other mods.

### SaveData

- `HasFlag` / `SetFlag` / `ClearFlag`, `GetNumber` / `SetNumber` / `AddNumber` / `HasNumber` / `RemoveNumber`.
- Keys are strings; **prefix them with your mod** (`"mymod.thing"`). Keys are hashed, and the log warns if two keys
  collide in the same session.
- Numbers are 32-bit floats (whole numbers exact up to 16,777,216).
- Values are stored per country in the campaign save. A save loaded without your mod keeps the values and nothing
  happens. Don't store large amounts: each value is one save entry.

### Treasury and Price

- `Treasury.Money/Supplies/Specialists(country)`, `Treasury.AddMoney/AddSupplies(country, amount)`.
- `new Price(money, supplies, specialists)`: `CanAfford(country, out missing)` and `TryPay(country)`.

### Popups

- `Popups.Show(title, text, button, image)`: the campaign event window.
- `Popups.Ask(title, text, [image], options...)`: the question window. `Option.Blocked` returns a reason to grey a
  button out (shown by the game) or null. **Keep one option that is never blocked**: the game refuses to show a
  question whose answers are all greyed out. `Option.Paid(text, price, chosen)` greys itself out when the player can't
  afford it and pays before `chosen` runs.
- Popups need a loaded campaign (`Game.InCampaign`). They queue behind the game's own events.
- Questions are not saved: if the player quits before answering, the answer callbacks never run. Set your "shown" flag
  before asking (as the example does) or after answering, depending on whether you want it asked again.

### Modifiers

- `Modifiers.AddToCountry(country, "BLOCK_MILITA", 2, percent: false)` adds a saved, permanent (or `days:`-timed) entry.
  `Modifiers.Get(country, modifier)` reads the current value.
- Not every `EModifier` does something at country level; many only apply per settlement or region. Known country-level
  ones include `BLOCK_MILITA` (militia limit) and `MAX_REMOTE_LOCALITY`. Percent modifiers are fractions: 0.1 = +10%.
- Entries are not removed when your mod is uninstalled. For effects that should stop with the mod, store a SaveData
  flag and apply the effect from your own code instead.

### Images

- `Images.Load(file)` loads a png/jpg as a sprite; `Images.GameEventPicture(name)` borrows one of the game's event
  pictures.

### Content

- `Content.IsLoaded("ugar.custombuildings")`, `Content.VersionOf(guid)`; GUID constants `Content.CustomBuildings`,
  `Content.BritishEvents`, `Content.WeaponWorkshop`.
- `Content.CustomBuildingsIn(settlement)` and `Content.CustomBuildingWorkforce(settlement)` read from UGAR Custom
  Buildings when it's installed (empty / 0 otherwise).

## Rules for mods that use the kit

- Never bypass the Morgan's Rifles DLC check, and never read or copy credentials found in the game's files. Mods doing
  either won't be listed alongside the pack.
- Everything the kit does runs on the game's main thread; don't call it from other threads.
- Live reload: ModKit itself can't be live-reloaded; mods that use it can. Restart the game after updating ModKit.

## Not in 0.1 yet

- Registering buildings, events or weapons from code (today: JSON files for buildings and events; weapons only through
  the Weapon Workshop window).
- Settlement-level save data and modifiers, battle hooks, UI panels and tooltips in the game's style.

## Troubleshooting

Look in `BepInEx\LogOutput.log` for lines from `UGAR ModKit` (and your own plugin's name). If your mod doesn't load,
check that ModKit is installed: BepInEx logs a missing-dependency line and skips your mod.
