// UGAR Mining Gold: Mining Infrastructure boosts a gold region's gold income by the same rule it boosts iron and the
// other ores, and the Mining Infrastructure card shows the gold gain under the ore lines.
//
// How the game does it:
// - Mining Infrastructure is a region construction (MineSettings.Execute: Region.mining += 1).
// - Ore, per settlement (Region.CalculateRegion → CalculateOrePoints(locality, share × loyalty, prisoners, …, mining)):
//   orePoints = RegionConfig.oreCurve(1 + mining × share × loyaltyEffect × prisonersBonus) × difficulty
//   resourcesBonusProduction (+ the state doctrine's bonus for the player). share = 1 / non-native settlements in the
//   region, loyaltyEffect = RegionConfig.loyaltyEffectCurve(owner's loyalty) (0.75 .. 1.1), prisonersBonus =
//   LocalityConfig.prisonersBonusCurve(prisoners). oreCurve is the identity (0→0, 10→10), so each level adds about
//   +100 % of the level-0 ore output in a one-settlement region.
// - Gold, per settlement (Region.CalculateIncome, only when Region.Gold = availableResources[EResource.Gold]):
//   income = (INCOME_INCREMENT country + settlement + workforce × State.IncomeFromWorkforce
//             + RegionConfig.goldIncomeFromMining(mining)) × (1 + INCOME_PERCENT) × (1 + IncomeCollection department)
//             × difficulty incomeModifier.
//   goldIncomeFromMining is 1000, 1200, 1393, 1500, 1600, 1700 … 2000 at level 10: the game
//   already gives a little gold for mining (+20 % at level 1, less after), but the card never shows it.
//
// This mod: gold term = max(vanilla goldIncomeFromMining(mining), goldIncomeFromMining(0) × ore multiplier), where the
// ore multiplier is the settlement's own CalculateOrePoints(mining) / CalculateOrePoints(0), so gold grows exactly like
// iron does. Never less than vanilla. The rest of the income formula is unchanged.
using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using World;
using World.SceneObject;
using SelectionElement = World.UI.GeneralPage.RegionInfo.ConstructionSelectionElement;

namespace UGARMiningGold;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "ugar.mininggold";
    public const string Name = "UGAR Mining Gold";
    public const string Version = "1.0.0";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> AllNations;
    internal static ConfigEntry<bool> WeeklyLog;

    Harmony _harmony;

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("General", "Enabled", true,
            "Mining Infrastructure in a gold region also raises the region's gold income, by the same rule it raises iron " +
            "(each level multiplies the gold the same way it multiplies the ore). The Mining Infrastructure card shows the " +
            "extra gold per week. Never gives less than the game's own small gold bonus.");
        AllNations = Config.Bind("General", "AllNations", true, new ConfigDescription(
            "Apply the rule to every nation's settlements, like the game's own rules (off: only yours).", null, "Advanced"));
        WeeklyLog = Config.Bind("Debug", "WeeklyLog", true, new ConfigDescription(
            "Each week, write every gold region's iron and gold multipliers and the extra gold to BepInEx\\LogOutput.log.",
            null, "Advanced"));

        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(Patches));
        foreach (var m in _harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.FullName}.{m.Name}");
        Log.LogInfo($"{Name} {Version} loaded (gold boost {(Enabled.Value ? "on" : "off")}). Gold curve:{Rules.CurveText()}");
    }

    // The mod manager's live reload calls this before loading the new copy.
    public override bool Unload()
    {
        try { _harmony?.UnpatchSelf(); } catch (Exception e) { Log.LogWarning($"Unpatch failed: {e.Message}"); }
        Patches.Week.Clear();
        return true;
    }
}

internal static class Rules
{
    internal static AnimationCurve GoldCurve => Config.game?.region?.goldIncomeFromMining;

    internal static string CurveText()
    {
        try
        {
            var c = GoldCurve;
            if (c == null) return " (not loaded yet)";
            var sb = new StringBuilder();
            for (int i = 0; i <= 5; i++) sb.Append($" {i}:{c.Evaluate(i):0}");
            return sb.ToString();
        }
        catch (Exception) { return " ?"; }
    }

    internal static bool Applies(Region region, RegionLocality loc)
    {
        if (!Plugin.Enabled.Value || region == null || loc == null || loc.destroyed || region.mining <= 0 || !region.Gold)
            return false;
        return Plugin.AllNations.Value || loc.ownerNation == RuntimeVars.playerNation;
    }

    /// <summary>1 / settlements that share the region's output (not destroyed, not native), as Region.CalculateRegion.</summary>
    internal static float Share(Region region)
    {
        var locs = region.localities;
        if (locs == null) return 0f;
        int n = 0;
        for (int i = 0; i < locs.Count; i++)
        {
            var l = locs[i];
            if (l != null && !l.destroyed && !ENationUtil.IsNative(l.ownerNation)) n++;
        }
        return n > 0 ? 1f / n : 0f;
    }

    /// <summary>How much the region's mining level multiplies this settlement's ore: the game's own ore formula at
    /// <paramref name="level"/> divided by the same at level 0 (no doctrine bonus).</summary>
    internal static float OreMultiplier(Region region, RegionLocality loc, float loyaltyEffect, int level)
    {
        float share = Share(region);
        float prisoners = 1f;
        var curve = Config.game?.locality?.prisonersBonusCurve;
        if (curve != null) prisoners = curve.Evaluate(loc.prisoners);
        float at0 = region.CalculateOrePoints(loc, share * loyaltyEffect, prisoners, false, false, 0);
        if (at0 <= 0f) return 1f;
        return region.CalculateOrePoints(loc, share * loyaltyEffect, prisoners, false, false, level) / at0;
    }

    /// <summary>Gold term of one settlement's income at a mining level: vanilla curve vs. the iron rule (the larger).</summary>
    internal static float GoldTerm(Region region, RegionLocality loc, float loyaltyEffect, int level, out float vanilla, out float multiplier)
    {
        var c = GoldCurve;
        vanilla = c != null ? c.Evaluate(level) : 0f;
        multiplier = level > 0 ? OreMultiplier(region, loc, loyaltyEffect, level) : 1f;
        float ironRule = (c != null ? c.Evaluate(0) : 0f) * multiplier;
        return Plugin.Enabled.Value ? Math.Max(vanilla, ironRule) : vanilla;
    }

    /// <summary>What Region.CalculateIncome multiplies its sum by: (1 + INCOME_PERCENT) × (1 + IncomeCollection) × difficulty.</summary>
    internal static float IncomeMultiplier(ENation nation)
    {
        float m = 1f;
        var country = ENationUtil.GetCountry(nation);
        if (country?.modifiersManager != null) m *= country.modifiersManager.GetModifierValue(EModifier.INCOME_PERCENT, true);
        var dm = MonoBehaviourSingleton<DepartmentManager>.instance;
        if (dm != null) m *= 1f + dm.GetEffect(EDepartmentEffect.IncomeCollection);
        var diff = Config.game?.difficultyConfig;
        if (diff != null) m *= diff.GetDifficultySettings(RuntimeVars.difficulty).incomeModifier;
        return m;
    }

    /// <summary>The rest of CalculateIncome's sum (without gold), to check IncomeMultiplier against the game's result.</summary>
    internal static float BaseSum(Region region, RegionLocality loc)
    {
        float s = 0f;
        var country = ENationUtil.GetCountry(loc.ownerNation);
        if (country?.modifiersManager != null) s += country.modifiersManager.GetModifierValue(EModifier.INCOME_INCREMENT, false);
        if (loc.modifierManager != null) s += loc.modifierManager.GetModifierValue(EModifier.INCOME_INCREMENT, false);
        if (region.state != null) s += loc.Workforce * region.state.IncomeFromWorkforce();
        return s;
    }

    internal static float LoyaltyEffect(Region region, ENation nation)
    {
        var curve = Config.game?.region?.loyaltyEffectCurve;
        float loyalty = region.GetLoyalty(nation);
        return curve != null ? curve.Evaluate(loyalty) : 1f;
    }

    /// <summary>Extra gold per week from building one more Mining Infrastructure level, for the player's settlements,
    /// after the income multipliers (what the card shows).</summary>
    internal static float NextLevelGold(Region region)
    {
        var locs = region.localities;
        if (locs == null || !region.Gold) return 0f;
        var player = RuntimeVars.playerNation;
        int m = region.mining;
        float total = 0f;
        for (int i = 0; i < locs.Count; i++)
        {
            var l = locs[i];
            if (l == null || l.destroyed || l.ownerNation != player) continue;
            float loy = LoyaltyEffect(region, player);
            float now = GoldTerm(region, l, loy, m, out _, out _);
            float next = GoldTerm(region, l, loy, m + 1, out _, out _);
            total += (next - now) * IncomeMultiplier(player);
        }
        return total;
    }
}

internal static class Patches
{
    internal static readonly List<string> Week = new();
    static int _weeklyDepth;
    static int _checks, _checkFails;

    // Region.CalculateIncome(locality, loyaltyEffect, 1, 0): one settlement's weekly money (also used by previews).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Region), nameof(Region.CalculateIncome))]
    static void CalculateIncomePostfix(Region __instance, RegionLocality __0, float __1, ref float __result)
    {
        try
        {
            if (!Rules.Applies(__instance, __0)) return;
            int m = __instance.mining;
            float gold = Rules.GoldTerm(__instance, __0, __1, m, out float vanilla, out float mult);
            float extra = gold - vanilla;
            float incomeMult = Rules.IncomeMultiplier(__0.ownerNation);
            if (_weeklyDepth > 0)
            {
                // Self-check: the game's result should be incomeMult × (base + vanilla gold).
                float expected = incomeMult * (Rules.BaseSum(__instance, __0) + vanilla);
                _checks++;
                bool ok = Math.Abs(expected - __result) <= 0.5f + 0.001f * Math.Abs(__result);
                if (!ok) _checkFails++;
                if (Plugin.WeeklyLog.Value)
                    Week.Add($"{__instance.Name} / {__0.Name} ({__0.ownerNation}, mining {m}): iron ×{mult:0.##} " +
                             $"→ gold {gold:0} (game {vanilla:0}), +{extra:0} raw, +{extra * incomeMult:0} income (×{incomeMult:0.##})" +
                             (ok ? "" : $" [check: game income {__result:0}, expected {expected:0}]"));
            }
            if (extra > 0f) __result += extra * incomeMult;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"CalculateIncome postfix failed: {e.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(State), nameof(State.WeeklyUpdate))]
    static void WeeklyPrefix() => _weeklyDepth++;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(State), nameof(State.WeeklyUpdate))]
    static void WeeklyPostfix(State __instance)
    {
        _weeklyDepth = Math.Max(0, _weeklyDepth - 1);
        if (Week.Count == 0) return;
        try
        {
            var d = RuntimeVars.date;
            string name = string.IsNullOrEmpty(__instance.stateName) ? __instance.name : __instance.stateName;
            var sb = new StringBuilder($"Mining gold {d.Year:0000}-{d.Month:00}-{d.Day:00} {name}:");
            foreach (var line in Week) sb.Append("\n  ").Append(line);
            if (_checkFails > 0) sb.Append($"\n  income check: {_checkFails} of {_checks} settlements differ (see docs/mining-gold.md)");
            Plugin.Log.LogInfo(sb.ToString());
        }
        catch (Exception) { }
        Week.Clear();
        _checks = _checkFails = 0;
    }

    // The region panel's construction card (RegionInfo.ConstructionSelectionElement.Show → Region.GetInfoAboutConstruction):
    // for Mining Infrastructure the game lists the ore gained by the next level; add the gold line after them.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SelectionElement), nameof(SelectionElement.Show))]
    static void CardPostfix(SelectionElement __instance, EConstruction __0, Region __1)
    {
        try
        {
            if (!Plugin.Enabled.Value || __0 != EConstruction.Mining || __1 == null || !__1.Gold) return;
            float gain = Rules.NextLevelGold(__1);
            if (gain <= 0.005f) return;
            var slots = __instance.bonusResources;
            if (slots == null) return;
            World.UI.ResourceElement free = null;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s != null && !s.gameObject.activeSelf) { free = s; break; }
            }
            if (free == null)
            {
                if (!_warnedNoSlot) Plugin.Log.LogWarning($"No free line on the Mining Infrastructure card for gold (+{gain:0}).");
                _warnedNoSlot = true;
                return;
            }
            var icon = Config.game?.hud?.GetResourceIcon(EResource.Gold);
            free.gameObject.SetActive(true);
            free.Show(icon, StringUtil.Colored(string.Format("+{0}", MathHelper.Round(gain, 2)), "green"));
            free.hintContent = "Gold: extra money per week from the next Mining Infrastructure level (UGAR Mining Gold).";
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Mining card postfix failed: {e.Message}");
        }
    }

    static bool _warnedNoSlot;
}
