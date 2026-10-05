using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using World;
using World.SceneObject;

namespace UGARInstantBuild;

// Finishes the player's settlement buildings, upgrades and repairs as soon as they are ordered.
//
// How the game builds: RegionLocality.StartConstruction(slot, construction) puts the slot
// in constructionQuerry; once a day RegionLocality.UpdateConstruction(points) adds points to the first queued
// construction (its upgrade, when the slot holds one) and, when constructionProgress reaches
// settings.pointsToConstruct, completes it (state, effects, "construction finished" notification, quest report) and
// removes it from the queue. Only one queued item advances per day.
//
// We fill the first item's progress up to exactly pointsToConstruct before the game adds its points (the same thing
// the developers' PlayerPreferences.instantlyDevelopmentConstruct flag does, without that flag's skip of the cost
// check), and then run UpdateConstruction(0) on the next frame until the settlement's queue is empty, so everything
// completes through the game's own code.
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "ugar.instantbuild";
    public const string Name = "UGAR Instant Construction";
    public const string Version = "1.0.0";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> LogFinished;

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("General", "Enabled", false,
            "Your settlement buildings, upgrades and repairs finish as soon as you order them (you still pay their cost). " +
            "Buildings already in a queue finish on the next in-game day. AI nations build normally.");
        LogFinished = Config.Bind("General", "LogFinished", true,
            new ConfigDescription("Write each instantly finished construction to the BepInEx log.", null, "Advanced"));

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.FullName}.{m.Name}");

        ClassInjector.RegisterTypeInIl2Cpp<Finisher>();
        AddComponent<Finisher>();
        Log.LogInfo($"{Name} {Version} loaded (instant construction {(Enabled.Value ? "on" : "off")}).");
    }

    internal static bool Applies(RegionLocality locality) =>
        Enabled.Value && locality != null && !locality.destroyed && locality.IsPlayerNation;

    // The construction that UpdateConstruction will advance next, or null when nothing is queued.
    internal static Construction NextInQueue(RegionLocality locality)
    {
        var queue = locality.constructionQuerry;
        var slots = locality.constructionSlots;
        if (queue == null || slots == null || queue.Count == 0) return null;
        int slot = queue[0];
        if (slot < 0 || slot >= slots.Length) return null;
        var c = slots[slot];
        if (c == null) return null;
        return c.upgrade ?? c;
    }

    internal static int QueueCount(RegionLocality locality) => locality.constructionQuerry?.Count ?? 0;
}

[HarmonyPatch(typeof(RegionLocality), nameof(RegionLocality.UpdateConstruction))]
internal static class UpdateConstructionPatch
{
    static void Prefix(RegionLocality __instance)
    {
        try
        {
            if (!Plugin.Applies(__instance)) return;
            var c = Plugin.NextInQueue(__instance);
            var settings = c?.settings;
            if (settings == null) return;
            float needed = settings.pointsToConstruct;
            if (c.constructionProgress >= needed) return;
            c.constructionProgress = needed;
            if (Plugin.LogFinished.Value)
                Plugin.Log.LogInfo($"Finishing {settings.Header} in {__instance.Name}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"UpdateConstruction prefix failed: {e}");
        }
    }

    // More items queued: finish them on the next frame (the game only advances one per call).
    static void Postfix(RegionLocality __instance)
    {
        if (!Finisher.Running && Plugin.Applies(__instance) && Plugin.QueueCount(__instance) > 0)
            Finisher.Queue(__instance);
    }
}

// The build menu's path (LocalityInfoPanel.StartConstruction is its only caller): finish what was just ordered.
// Deferred to Finisher.Update so the panel finishes its own refresh first.
[HarmonyPatch(typeof(RegionLocality), nameof(RegionLocality.StartConstruction))]
internal static class StartConstructionPatch
{
    static void Postfix(RegionLocality __instance)
    {
        if (Plugin.Applies(__instance) && Plugin.QueueCount(__instance) > 0)
            Finisher.Queue(__instance);
    }
}

public class Finisher : MonoBehaviour
{
    public Finisher(IntPtr ptr) : base(ptr) { }

    static readonly List<RegionLocality> Pending = new();
    internal static bool Running;

    internal static void Queue(RegionLocality locality)
    {
        foreach (var l in Pending)
            if (l.Pointer == locality.Pointer) return;
        Pending.Add(locality);
    }

    void Update()
    {
        if (Pending.Count == 0) return;
        var work = Pending.ToArray();
        Pending.Clear();
        Running = true;
        try
        {
            foreach (var locality in work)
            {
                // Each call completes the first queued item (our prefix fills its progress). Stop when the queue is
                // empty or a call doesn't shrink it (something the game refuses to finish).
                for (int i = 0; i < 64 && Plugin.Applies(locality); i++)
                {
                    int before = Plugin.QueueCount(locality);
                    if (before == 0) break;
                    locality.UpdateConstruction(0f);
                    if (Plugin.QueueCount(locality) >= before) break;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Finishing constructions failed: {e}");
        }
        finally
        {
            Running = false;
        }
    }

    void OnDestroy() => Pending.Clear();
}
