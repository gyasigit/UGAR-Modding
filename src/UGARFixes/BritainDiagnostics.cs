using System;
using System.Text;
using HarmonyLib;
using UnityEngine;
using World;
using World.SceneObject;
using World.UI.Britain;
using MapModeRoot = World.UI.GeneralPage.MapMode.RootElement;

namespace UGARFixes;

// Step 1 of the British campaign work: instrument the England (Britain) screen so a
// reproduction on a real save tells us exactly where it breaks. Everything here only logs.
internal static class BritainState
{
    public static string Describe()
    {
        var sb = new StringBuilder();
        try
        {
            var scene = MonoBehaviourSingleton<SceneManager>.instance;
            if (scene == null) return "SceneManager not available (not on campaign map?)";
            sb.Append($"MapMode={scene.MapMode} GameSpeed={scene.GameSpeed}");
            var country = scene.PlayerCountry;
            if (country == null) return sb.Append(" PlayerCountry=null").ToString();
            sb.Append($" Factory={country.Factory} Shipyard={country.Shipyard}");

            var eu = country.europeanManager;
            if (eu == null) sb.Append(" europeanManager=null");
            else
            {
                sb.Append($" recruits={eu.recruits}/{eu.maxRecruits} rentedFactories={eu.rentedFactories} rentedShipyards={eu.rentedShipyards}");
                var inv = eu.inventory;
                sb.Append(inv == null ? " inventory=null" : $" euProductionPoints={inv.productionPoints}");
                var rr = eu.remoteRegion;
                sb.Append(rr == null ? " euRemoteRegion=null" : $" euRemoteRegion={rr.name}");
            }

            var remote = country.RemoteRegion;
            if (remote == null) sb.Append(" RemoteRegion=null");
            else
            {
                var locs = remote.localities;
                sb.Append($" RemoteRegion={remote.name} localities={(locs == null ? "null" : locs.Count.ToString())}");
                sb.Append($" state={(remote.state == null ? "null" : remote.state.name)}");
            }

            var mods = country.modifiersManager;
            if (mods != null)
                sb.Append($" {(World.EModifier)0x70}={mods.GetModifierValue((World.EModifier)0x70, false)}");
        }
        catch (Exception e)
        {
            sb.Append($" <state dump failed: {e.GetType().Name}: {e.Message}>");
        }
        return sb.ToString();
    }
}

[HarmonyPatch(typeof(BritainPanel), nameof(BritainPanel.Show))]
internal static class BritainPanel_Show_Diag
{
    static void Prefix(BritainPanel __instance)
    {
        Plugin.Log.LogInfo($"[Britain] Show() begin. huds={__instance.huds?.Length} | {BritainState.Describe()}");
    }

    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null)
            Plugin.Log.LogError($"[Britain] Show() threw {__exception}\n  state: {BritainState.Describe()}");
        else
            Plugin.Log.LogInfo("[Britain] Show() completed");
        return __exception; // diagnostics only: keep vanilla behaviour
    }
}

[HarmonyPatch(typeof(BritainPanel), nameof(BritainPanel.Hide))]
internal static class BritainPanel_Hide_Diag
{
    static void Prefix() => Plugin.Log.LogInfo($"[Britain] Hide() begin | {BritainState.Describe()}");

    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null) Plugin.Log.LogError($"[Britain] Hide() threw {__exception}");
        return __exception;
    }
}

[HarmonyPatch(typeof(MapModeRoot), nameof(MapModeRoot.OnBritClick))]
internal static class BritButton_Diag
{
    static void Prefix() => Plugin.Log.LogInfo("[Britain] England/USA button clicked");

    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null) Plugin.Log.LogError($"[Britain] OnBritClick threw {__exception}");
        return __exception;
    }
}

[HarmonyPatch(typeof(World.UI.Britain.Rented.RentedShipyardsSwitcher), nameof(World.UI.Britain.Rented.RentedShipyardsSwitcher.UpdateContent))]
internal static class RentedShipyards_Diag
{
    static void Postfix(World.UI.Britain.Rented.RentedShipyardsSwitcher __instance)
    {
        Plugin.Log.LogInfo($"[Britain] Rented shipyards: current={__instance.current} target={__instance.target} available={__instance.availableFactories} price={__instance.price} percent={__instance.percent}");
    }
}

[HarmonyPatch(typeof(World.UI.Britain.Rented.RentedFactoriesSwitcher), nameof(World.UI.Britain.Rented.RentedFactoriesSwitcher.UpdateContent))]
internal static class RentedFactories_Diag
{
    static void Postfix(World.UI.Britain.Rented.RentedFactoriesSwitcher __instance)
    {
        Plugin.Log.LogInfo($"[Britain] Rented factories: current={__instance.current} target={__instance.target} available={__instance.availableFactories} price={__instance.price} percent={__instance.percent}");
    }
}

public class DiagnosticsHotkeys : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9))
            Plugin.Log.LogInfo($"[Britain] F9 state dump | {BritainState.Describe()}");
    }
}
