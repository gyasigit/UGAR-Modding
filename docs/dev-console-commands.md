# Developer Console

The **Developer Console** mod turns on the developers' own hidden cheat and debug console on the campaign map. This
page explains how to open it and lists its commands.

## Using it in game

- Press **~** (the `` ` ``/~ key on US keyboards) on the campaign map to open the console. **Escape** or ~ closes it.
- Command names aren't case-sensitive. **Tab** completes command and nation names.
- `list` prints every command in game, and `help <command>` explains one. `clear` empties the window.
- Nation names: USA, Britain, France, Spain, Cherokee, Creeks, Iroqouis (the game's spelling), Miamis. Use these
  exact names; "British" or "England" won't work.

**Warning:** commands marked as cheats change your campaign save. Back up your save, or try them on a copy first.

### About the descriptions

The developers left no descriptions for most commands, so the ones below are best guesses based on each command's
name and parameters, and many haven't been tried. Where the developers did leave a short note (mostly value ranges),
it is quoted as a **developers' note**; in game it appears after `| developers' note:`.

## F8 settings

Open the mod manager with **F8** and pick **Developer Console** (see [mod-manager.md](mod-manager.md)).

| Setting | Default | What it does |
|---|---|---|
| `General.Enabled` | on | Developer console on the campaign map. Off hides it immediately. |
| `General.ToggleKey` | BackQuote | Key that opens/closes the console (a Unity key name; BackQuote is the ~ key). |
| `General.LogCommands` (advanced) | on | Write every console command and its output to the log. |

## Campaign cheats (change the save)

| Command | Description |
|---|---|
| `Money <amount:int>` | Adds money to your treasury (negative removes). |
| `ChangeRenown <amount>` | Adds renown. |
| `AddSpecialists <count>` | Adds specialists (needed for construction). |
| `AddConstructionMaterials <count>` | Adds construction materials. |
| `AddPrisoners <count>` | Adds prisoners. |
| `AddPrisonerOfficer` | Adds one captured enemy officer. |
| `SetTax <value>` | Sets the tax level as a fraction (0.2 = 20%). |
| `Speed <step:int>` | Sets campaign time speed (not a save change). |
| `AddReserveUnits <nation> <count>` / `RemoveReserveUnits` | Adds/removes land units in an AI nation's army reserve. |
| `AddReserveNavies <nation> <count>` / `RemoveReserveNavies` | Same for an AI navy reserve. |
| `AddReserveMercenary <nation> <count>` / `RemoveReserveMercenary` | **AI nations only.** Adds/removes mercenary units in the AI's reserve, the pool of units the AI deploys onto the map. It doesn't give the player anything. |
| `ChangeOfficerExperince <amount>` | Changes the targeted officer's experience. |
| `AddOfficerExperince <name> <amount>` | Adds experience to the officer with this name. |
| `PromoteAttribute <index:int> <amount>` | Raises one attribute of the targeted officer. |
| `ChangeUnitMorale <amount>` | Changes the selected unit's morale. |
| `Capture <nation>` | Hands the selected settlement/region to the nation. |
| `DestroyLocality <name>` | Destroys the settlement with this name. |
| `ChangeTension <nation> <nation> <amount>` | Changes tension between two nations. |
| `ChangeRelation <nation> <nation> <state>` | Sets the relation (e.g. WAR, ALLIES). |
| `ChangeVictoryPoints <nation> <nation> <amount>` | Changes victory points between two nations. |
| `RelationEvent <name> <nation>` | Fires a diplomatic relation event. **Developers' note:** "name - event name (use RelationEventList to get names), nation - another nation". |
| `SetCountryModifiers <nation> <modifier> <value>` | Sets a nation-wide modifier (e.g. RECRUITS_PERCENT). |
| `SetLocalityModifiers <settlement> <modifier> <value>` | Sets a modifier on a settlement. |
| `SetRegionModifiers <region> <modifier> <value>` | Sets a modifier on a region. |
| `SetLandDynamicDifficulty <value>` / `SetNavalDynamicDifficulty <value>` | Sets the dynamic difficulty factor for land or naval battles. |
| `FinishCampaign <result:int>` | **Ends the campaign.** 0 Death, 1 British victory, 2 British dismissed, 3 British defeat, 4 USA dismissed, 5 USA victory, 6 USA defeat, 7 "In development" ending. |
| `FinishMission` | Completes the current mission/quest. |

## Information and debug panels (read-only)

| Command | Description |
|---|---|
| `ArmyStatPanel <nation>` | Opens the developers' army statistics panel (armies, fleets, money/resource charts). |
| `ArmyCreationPanel <nation>` | Opens the AI army-creation panel (reserves, production, transfers). **AI nations only**: for your own nation the game answers "<nation> doesn't have army creation manager", so in a British campaign use `USA` and vice versa. |
| `ShowRisk/ShowInterest/ShowPower <nation>` | Shows the AI's risk / interest / power map overlay for a nation. |
| `CountryModifiers <nation>` | Prints a nation's modifiers. |
| `LocalityModifiers <settlement>` / `RegionModifiers <region>` | Prints a settlement's / region's modifiers. |
| `TerritoryStatistics` | Writes territory statistics. |
| `RelationEventList` | Lists event names for `RelationEvent`. |
| `CollectEventsData` | Dumps campaign event data. |
| `LogModifiers` | Logs the console target's modifiers. |

## System

| Command | Description |
|---|---|
| `LoadMainMenu` | Returns to the main menu (unsaved progress is lost). |
| `CollectGarbage` | Forces a memory clean-up. |
| `SetTargetFramerate <fps>` | Caps the frame rate. |
| `SetGlobalTime <value>` | Sets the time of day used for lighting. |
| `SetWeatherPreset <index>` | Switches the weather preset. |

## Battle and naval commands (left over from Ultimate Admiral: Age of Sail)

These need a battle and a target selected with the middle mouse button. The console only exists on the campaign map,
so most of them do nothing in this game.

| Command | Description | Developers' note |
|---|---|---|
| `ExplodeShips` | Blows up the targeted ship(s). | "Booom!" |
| `IncinerateShips <value>` | Sets the targeted ship(s) on fire. | "(0):[0...1] Fire Rate." |
| `SetArmorsCondition <value>` | Condition of all armor sides. | "Set armor condition of each side. (0):[0...1] New Condition." |
| `SetArmorCondition <value> <side>` | Condition of one armor side. | "(0):[0...1] Condition; (1):[0...3] Side index." |
| `DamageCrew/DamageRudder/DamageHull/DamageStructure/DamagePwdMag <hp>` | Damage to that part of the target ship. | "(0): HP" |
| `DamagePump <points>` | Pump damage / flooding. | "(0): Flooding Points" |
| `DamageModule <hp> <index>` | Damages one module. | "(0): HP" |
| `DestroyMastSection <mast> <section>` / `DestroyMast <mast>` / `DestroyAllMasts` | Mast damage. | "(0): Mast Index (1): Section Index" |
| `DestroyCannons <board> <count>` | Destroys cannons on one side. | "(0): Board Index, (1): Numbee" [sic] |
| `SetWind <degrees>` | Wind direction. | "(0):[0...360]" |
| `SetMorale <value>` / `SetStamina <value>` | Target's morale / stamina. | "(0):[0...1]" |
| `KillOfficer <index>` / `WoundOfficer <index>` | Kills/wounds a ship officer. | "(0):[0...6] Officer Index" |
| `SetSurrender <0/1>` | Surrender mode. | "Enables/Disables Surrender Mode. (0):[0...1] 0 - Disable, 1 - Enable" |
| `SetTactics <0..5>` | Ship AI tactics. | "(0):[0...5] 0 - Ranger, 1 - Rogue, 2 - Tank, 3 - Boarder, 4 - Survival, 5 - Keelwater" |
| `SurrenderUnit/KillUnit/RemoveUnit/ShatterUnit/WoundUnitOfficer/KillUnitOfficer`, `DamageUnit <value>` | Land-unit actions on the middle-mouse target. | – |
| `TestBulletsDamage <int>`, `CheckMasts`, `ResetShocks` | Developer tests. | – |

## Saves and compatibility

The mod itself adds nothing to your save and can be removed at any time. Cheat commands, however, make real changes
to the campaign that stay after you save, even if you later remove the mod.

## Troubleshooting

Look in `BepInEx\LogOutput.log` for lines tagged `UGAR Developer Console`. On start it logs
`UGAR Developer Console ... loaded. Press BackQuote on the campaign map to open the console.`; with
`General.LogCommands` on, each command appears as `Console command: ...`. If the console doesn't open, check that
you are on the campaign map (not a battle or the main menu) and that `General.Enabled` is on.
