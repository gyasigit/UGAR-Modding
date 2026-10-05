using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UGARDevConsole;

// Command list, descriptions and Tab completion. Names and parameters come from the game's own command class
// (UltimateAdmiral.ConsoleMethods, public static methods, called by name with reflection). Descriptions: the game's
// [Help] text (ConsoleMethods.GetMethodInfo) when it has one, otherwise ours below, inferred from the name and the
// parameters. Keep in sync with docs/dev-console-commands.md.
internal static class Commands
{
    internal enum Area { Campaign, Info, Battle, System }

    internal sealed class Cmd
    {
        public string Name;
        public string Usage;       // "Money <amount:int>"
        public string Description;
        public string GameNote;    // the developers' own [Help] text, if any
        public bool ChangesSave;
        public Area Area;
        public bool TakesNation;   // first argument is a nation name
    }

    // name -> (area, changes save, takes a nation first, description)
    static readonly Dictionary<string, (Area, bool, bool, string)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // Campaign: resources and economy
        ["Money"] = (Area.Campaign, true, false, "Adds money to your treasury (negative removes)."),
        ["ChangeRenown"] = (Area.Campaign, true, false, "Adds renown (negative removes)."),
        ["AddSpecialists"] = (Area.Campaign, true, false, "Adds specialists (needed to construct buildings)."),
        ["AddConstructionMaterials"] = (Area.Campaign, true, false, "Adds construction materials."),
        ["AddPrisoners"] = (Area.Campaign, true, false, "Adds that many prisoners."),
        ["AddPrisonerOfficer"] = (Area.Campaign, true, false, "Adds one captured enemy officer."),
        ["SetTax"] = (Area.Campaign, true, false, "Sets your tax level (the game stores it as a fraction, e.g. 0.2 = 20%)."),
        ["Speed"] = (Area.Campaign, false, false, "Sets the campaign time speed (game-speed step; 0 pauses)."),
        // Campaign: armies and reserves
        ["AddReserveUnits"] = (Area.Campaign, true, true, "Adds land units to a nation's AI army reserve (nation, count)."),
        ["RemoveReserveUnits"] = (Area.Campaign, true, true, "Removes land units from a nation's AI army reserve (nation, count)."),
        ["AddReserveNavies"] = (Area.Campaign, true, true, "Adds ships to a nation's AI navy reserve (nation, count)."),
        ["RemoveReserveNavies"] = (Area.Campaign, true, true, "Removes ships from a nation's AI navy reserve (nation, count)."),
        ["AddReserveMercenary"] = (Area.Campaign, true, true, "AI nations only: adds that many units of the AI's designated mercenary template (ArmyCreationManagerSettings.consoleMercenaryTemplate) to the AI's waiting reserve, which the AI then deploys. Not for your own army."),
        ["RemoveReserveMercenary"] = (Area.Campaign, true, true, "Removes mercenary units from a nation's reserve (nation, count)."),
        ["ChangeOfficerExperince"] = (Area.Campaign, true, false, "Changes the experience of the selected/targeted officer."),
        ["AddOfficerExperince"] = (Area.Campaign, true, false, "Adds experience to the officer with this name."),
        ["PromoteAttribute"] = (Area.Campaign, true, false, "Raises one attribute (by index) of the targeted officer by the amount."),
        ["ChangeUnitMorale"] = (Area.Campaign, true, false, "Changes the morale of the selected unit."),
        // Campaign: territory and diplomacy
        ["Capture"] = (Area.Campaign, true, true, "Hands the selected settlement/region over to the nation."),
        ["DestroyLocality"] = (Area.Campaign, true, false, "Destroys the settlement with this name."),
        ["ChangeTension"] = (Area.Campaign, true, true, "Changes tension between two nations (nation, nation, amount)."),
        ["ChangeRelation"] = (Area.Campaign, true, true, "Sets the relation between two nations (nation, nation, state such as WAR or ALLIES)."),
        ["ChangeVictoryPoints"] = (Area.Campaign, true, true, "Changes victory points between two nations (nation, nation, amount)."),
        ["RelationEvent"] = (Area.Campaign, true, true, "Fires a diplomatic relation event (nation, event name). See RelationEventList."),
        ["RelationEventList"] = (Area.Info, false, false, "Lists the relation events RelationEvent can fire."),
        ["SetCountryModifiers"] = (Area.Campaign, true, true, "Sets a nation-wide modifier (nation, modifier name, value), e.g. RECRUITS_PERCENT."),
        ["SetLocalityModifiers"] = (Area.Campaign, true, false, "Sets a modifier on a settlement (settlement name, modifier name, value)."),
        ["SetRegionModifiers"] = (Area.Campaign, true, false, "Sets a modifier on a region (region name, modifier name, value)."),
        ["SetLandDynamicDifficulty"] = (Area.Campaign, true, false, "Sets the land-battle dynamic difficulty factor."),
        ["SetNavalDynamicDifficulty"] = (Area.Campaign, true, false, "Sets the naval-battle dynamic difficulty factor."),
        ["FinishCampaign"] = (Area.Campaign, true, false, "ENDS the campaign with a result: 0 Death, 1 British victory, 2 British dismissed, 3 British defeat, 4 USA dismissed, 5 USA victory, 6 USA defeat, 7 'In development' ending."),
        ["FinishMission"] = (Area.Campaign, true, false, "Completes the current mission/quest."),
        // Info and debug panels (read-only)
        ["ArmyStatPanel"] = (Area.Info, false, true, "Opens the developers' army statistics panel for a nation (armies, fleets, money and resource charts)."),
        ["ArmyCreationPanel"] = (Area.Info, false, true, "Opens the developers' AI army-creation panel for an AI nation (reserves, production, transfers). Your own nation has no AI army manager, so it answers \"doesn't have army creation manager\"; in a British campaign use USA. Unknown names fall back to Britain."),
        ["ShowRisk"] = (Area.Info, false, true, "Shows the AI's risk map for a nation (map overlay)."),
        ["ShowInterest"] = (Area.Info, false, true, "Shows the AI's interest map for a nation (map overlay)."),
        ["ShowPower"] = (Area.Info, false, true, "Shows the AI's power map for a nation (map overlay)."),
        ["CountryModifiers"] = (Area.Info, false, true, "Prints a nation's current modifiers."),
        ["LocalityModifiers"] = (Area.Info, false, false, "Prints the modifiers of the settlement with this name."),
        ["RegionModifiers"] = (Area.Info, false, false, "Prints the modifiers of the region with this name."),
        ["TerritoryStatistics"] = (Area.Info, false, false, "Writes territory statistics (the game's statistics export)."),
        ["CollectEventsData"] = (Area.Info, false, false, "Collects/dumps campaign event data for the developers."),
        ["LogModifiers"] = (Area.Info, false, false, "Logs the modifiers of the console target."),
        // System
        ["LoadMainMenu"] = (Area.System, false, false, "Returns to the main menu (unsaved progress is lost)."),
        ["CollectGarbage"] = (Area.System, false, false, "Forces a memory clean-up."),
        ["SetTargetFramerate"] = (Area.System, false, false, "Caps the frame rate (frames per second)."),
        ["SetGlobalTime"] = (Area.System, false, false, "Sets the time of day used for lighting."),
        ["SetWeatherPreset"] = (Area.System, false, false, "Switches the weather preset (index)."),
        ["SetWind"] = (Area.Battle, false, false, "Sets the wind direction/strength (naval battles)."),
        // Battle/naval commands from Ultimate Admiral: Age of Sail (need a battle and a middle-mouse target)
        ["ExplodeShips"] = (Area.Battle, false, false, "Blows up the targeted ship(s)."),
        ["IncinerateShips"] = (Area.Battle, false, false, "Sets the targeted ship(s) on fire."),
        ["SetArmorsCondition"] = (Area.Battle, false, false, "Sets the condition of all armor sections of the target ship."),
        ["SetArmorCondition"] = (Area.Battle, false, false, "Sets the condition of one armor section (value, section)."),
        ["DamageCrew"] = (Area.Battle, false, false, "Kills part of the target ship's crew."),
        ["DamageRudder"] = (Area.Battle, false, false, "Damages the target ship's rudder."),
        ["DamagePump"] = (Area.Battle, false, false, "Damages the target ship's pumps."),
        ["DamageHull"] = (Area.Battle, false, false, "Damages the target ship's hull."),
        ["DamageStructure"] = (Area.Battle, false, false, "Damages the target ship's structure."),
        ["DamagePwdMag"] = (Area.Battle, false, false, "Damages the target ship's powder magazine."),
        ["DamageModule"] = (Area.Battle, false, false, "Damages one module of the target ship (amount, module index)."),
        ["DestroyMastSection"] = (Area.Battle, false, false, "Destroys a section of a mast (mast, section)."),
        ["DestroyMast"] = (Area.Battle, false, false, "Destroys a mast (index)."),
        ["DestroyAllMasts"] = (Area.Battle, false, false, "Destroys all masts of the target ship."),
        ["DestroyCannons"] = (Area.Battle, false, false, "Destroys cannons on the target ship (side/deck, count)."),
        ["SetMorale"] = (Area.Battle, false, false, "Sets the target's morale."),
        ["SetStamina"] = (Area.Battle, false, false, "Sets the target's stamina."),
        ["KillOfficer"] = (Area.Battle, false, false, "Kills an officer of the target ship (index)."),
        ["WoundOfficer"] = (Area.Battle, false, false, "Wounds an officer of the target ship (index)."),
        ["SetSurrender"] = (Area.Battle, false, false, "Makes the target ship surrender (or not)."),
        ["SetTactics"] = (Area.Battle, false, false, "Sets the target ship's AI tactics mode."),
        ["TestBulletsDamage"] = (Area.Battle, false, false, "Developer test of bullet damage."),
        ["CheckMasts"] = (Area.Battle, false, false, "Developer check of mast states."),
        ["ResetShocks"] = (Area.Battle, false, false, "Resets the target unit's shock state."),
        ["SurrenderUnit"] = (Area.Battle, false, false, "Makes the targeted land unit surrender."),
        ["KillUnit"] = (Area.Battle, false, false, "Kills the targeted land unit."),
        ["RemoveUnit"] = (Area.Battle, false, false, "Removes the targeted land unit."),
        ["ShatterUnit"] = (Area.Battle, false, false, "Breaks (routs) the targeted land unit."),
        ["DamageUnit"] = (Area.Battle, false, false, "Damages the targeted land unit."),
        ["WoundUnitOfficer"] = (Area.Battle, false, false, "Wounds the targeted unit's officer."),
        ["KillUnitOfficer"] = (Area.Battle, false, false, "Kills the targeted unit's officer."),
    };

    internal static readonly string[] Nations = { "USA", "Britain", "France", "Spain", "Cherokee", "Creeks", "Iroqouis", "Miamis" };
    internal static readonly string[] Builtins = { "help", "list", "clear" };

    static List<Cmd> _all;

    internal static List<Cmd> All(UltimateAdmiral.ConsoleBar bar)
    {
        if (_all != null) return _all;
        var list = new List<Cmd>();
        foreach (var m in typeof(UltimateAdmiral.ConsoleMethods).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.ReturnType != typeof(void) || m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith(".")) continue;
            if (list.Any(c => c.Name == m.Name)) continue;
            var ps = m.GetParameters();
            var usage = m.Name + string.Concat(ps.Select(p => $" <{p.Name}:{TypeName(p.ParameterType)}>"));
            Known.TryGetValue(m.Name, out var k);
            var cmd = new Cmd
            {
                Name = m.Name, Usage = usage, Area = k.Item4 != null ? k.Item1 : Area.Info,
                ChangesSave = k.Item2, TakesNation = k.Item3 || (ps.Length > 0 && ps[0].ParameterType.Name == "ENation"),
                Description = k.Item4 ?? "(no description)",
            };
            cmd.GameNote = GameHelp(bar, m.Name); // mostly argument hints such as "(0):[0...360]"
            list.Add(cmd);
        }
        list.Sort((a, b) => a.Area != b.Area ? a.Area.CompareTo(b.Area) : string.CompareOrdinal(a.Name, b.Name));
        var games = list.Count(c => c.GameNote != null);
        Plugin.Log.LogInfo($"Console commands: {list.Count} found, {games} with the developers' own help text");
        return _all = list;
    }

    static string TypeName(Type t) => t.Name switch
    {
        "Int32" => "int", "Single" => "number", "String" => "text", "Boolean" => "true/false", "ENation" => "nation", _ => t.Name,
    };

    // The developers' [Help] text, if the command has one (the game prints "Description for command ... not found." otherwise).
    static string GameHelp(UltimateAdmiral.ConsoleBar bar, string name)
    {
        try
        {
            var s = bar?.consoleMethods?.GetMethodInfo(name);
            if (string.IsNullOrWhiteSpace(s) || s.StartsWith("Description for command", StringComparison.OrdinalIgnoreCase)) return null;
            return s.Trim();
        }
        catch (Exception) { return null; }
    }

    internal static Cmd Find(UltimateAdmiral.ConsoleBar bar, string name) =>
        All(bar).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    internal static string Line(Cmd c) =>
        $"{c.Usage} - {c.Description}{(c.ChangesSave ? " [changes save]" : "")}" +
        (c.GameNote != null ? $"  | developers' note: {c.GameNote}" : "");

    internal static IEnumerable<string> ListText(UltimateAdmiral.ConsoleBar bar)
    {
        yield return "Commands (names are not case-sensitive; Tab completes; 'help <command>' for details).";
        yield return "Descriptions are worked out from the game code (not written by the developers); 'developers' note' is their own text.";
        Area? last = null;
        foreach (var c in All(bar))
        {
            if (c.Area != last)
            {
                last = c.Area;
                yield return c.Area switch
                {
                    Area.Campaign => "-- Campaign (cheats: these change your save) --",
                    Area.Info => "-- Information and debug panels (read-only) --",
                    Area.System => "-- System --",
                    _ => "-- Battle/naval (from Ultimate Admiral; the console only exists on the campaign map, so most won't do anything) --",
                };
            }
            yield return "  " + Line(c);
        }
    }

    // Tab completion. Returns the new input text; candidates are filled when more than one choice is left.
    internal static string Complete(UltimateAdmiral.ConsoleBar bar, string input, int cycle, List<string> candidates)
    {
        candidates.Clear();
        input ??= "";
        var parts = input.Split(' ');
        int idx = parts.Length - 1;
        var word = parts[idx];
        IEnumerable<string> pool;
        if (idx == 0) pool = All(bar).Select(c => c.Name).Concat(Builtins);
        else if (idx == 1 && parts[0].Equals("help", StringComparison.OrdinalIgnoreCase)) pool = All(bar).Select(c => c.Name);
        else
        {
            var cmd = Find(bar, parts[0]);
            bool nationArg = cmd != null && cmd.TakesNation && (idx == 1 || (idx == 2 && (cmd.Name is "ChangeTension" or "ChangeRelation" or "ChangeVictoryPoints")));
            if (!nationArg) return input;
            pool = Nations;
        }
        var matches = pool.Where(n => n.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(n => n).ToList();
        if (matches.Count == 0) return input;
        string pick;
        if (matches.Count == 1) pick = matches[0] + " ";
        else
        {
            var prefix = CommonPrefix(matches);
            bool extends = prefix.Length > word.Length;
            if (extends && cycle == 0) pick = prefix; // first Tab: extend to the shared part, like a shell
            else pick = matches[(extends ? cycle - 1 : cycle) % matches.Count]; // further Tabs cycle the choices
            candidates.AddRange(matches);
        }
        parts[idx] = pick;
        return string.Join(" ", parts);
    }

    static string CommonPrefix(List<string> xs)
    {
        var p = xs[0];
        foreach (var x in xs)
        {
            int i = 0;
            while (i < p.Length && i < x.Length && char.ToLowerInvariant(p[i]) == char.ToLowerInvariant(x[i])) i++;
            p = p.Substring(0, i);
        }
        return p;
    }
}
