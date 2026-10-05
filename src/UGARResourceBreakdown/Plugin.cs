// UGAR Resource Breakdown: click a resource (or the factory/shipyard numbers) on the Production screen to see where it
// comes from and where it goes. Read-only: it reads the game's numbers and changes nothing.
// See Breakdown.cs for the game's rules and docs/resource-breakdown.md for the player guide.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using World;
using World.SceneObject;

namespace UGARResourceBreakdown
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.resourcebreakdown";
		public const string Name = "UGAR Resource Breakdown";
		public const string Version = "1.0.2";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<float> UiScale;
		internal static ConfigEntry<bool> VerifyLog;

		Harmony _harmony;
		Popover _popover;

		public override void Load()
		{
			Logger = Log;
			Enabled = Config.Bind("General", "Enabled", true,
				"Click a resource, or the factory/shipyard numbers, on the Production screen to see where it comes from.");
			UiScale = Config.Bind("Window", "Scale", 1f, new ConfigDescription("Width multiplier for the popover.",
				new AcceptableValueRange<float>(0.6f, 1.6f), "Advanced"));
			VerifyLog = Config.Bind("Debug", "VerifyLog", true, new ConfigDescription(
				"After each weekly update, write the predicted and actual resource deliveries to BepInEx\\LogOutput.log.", null, "Advanced"));

			ClassInjector.RegisterTypeInIl2Cpp<Popover>();
			_popover = AddComponent<Popover>();
			_harmony = new Harmony(Guid);
			_harmony.PatchAll(typeof(Patches));
			Log.LogInfo($"{Name} {Version} loaded. Click a resource on the Production screen.");
		}

		// The mod manager's live reload calls this before loading the new copy: remove our UI and patches.
		public override bool Unload()
		{
			try { _harmony?.UnpatchSelf(); } catch (Exception e) { Log.LogWarning($"Unpatch failed: {e.Message}"); }
			try { if (_popover != null) UnityEngine.Object.Destroy(_popover); } catch (Exception) { }
			_popover = null;
			return true;
		}
	}

	internal static class Patches
	{
		// Country.ReportResources is where the weekly mining/farming output enters the player's storage
		// (State.TransferResources and TransferResourcesInactiveState call it).
		[HarmonyPrefix]
		[HarmonyPatch(typeof(Country), nameof(Country.ReportResources))]
		static void ReportResourcesPrefix(Country __instance, CountItem __0)
		{
			if (!Plugin.Enabled.Value)
				return;
			try
			{
				var player = Breakdown.PlayerCountry;
				if (player == null || __instance.Pointer != player.Pointer || __0?.asset == null)
					return;
				Received.Add(__0.asset, __0.count);
			}
			catch (Exception) { }
		}

		// Self-check: on the frame after a weekly update, recompute the regions' output with the values that update
		// just used and compare it with what Country.ReportResources actually delivered.
		[HarmonyPostfix]
		[HarmonyPatch(typeof(State), nameof(State.WeeklyUpdate))]
		static void WeeklyPostfix(bool __0)
		{
			// SceneManager.UpdateWeeklyStates(true) is a recalculation pass (e.g. after loading a save): every state then
			// runs WeeklyUpdate(true), which applies the control penalty instead of TransferResources, so nothing is
			// delivered. Only check real weekly updates.
			if (__0)
				return;
			if (Plugin.Enabled.Value && Plugin.VerifyLog.Value)
				Popover.CheckPending = true;
		}

		/// <summary>Called by the popover component on the frame after the weekly update.</summary>
		internal static void LogCheck()
		{
			var sb = new System.Text.StringBuilder($"Resource breakdown check {Breakdown.Today:yyyy-MM-dd}:");
			var types = Breakdown.PanelTypes;
			bool mismatch = false;
			var excluded = new System.Collections.Generic.List<string>();
			for (int i = 0; i < types.Length; i++)
			{
				var asset = Breakdown.Asset(types[i]);
				Breakdown.Excluded = i == 0 ? excluded : null;
				float predicted;
				try { predicted = Breakdown.Resource(types[i]).WeeklyRegions; }
				finally { Breakdown.Excluded = null; }
				float actual = asset != null && Received.TryGet(asset, out float got, out _) ? got : 0f;
				sb.Append($" {types[i]} predicted +{predicted:0.##}, game +{actual:0.##};");
				if (Math.Abs(actual - predicted) > 0.01f + 0.001f * Math.Abs(actual))
					mismatch = true;
			}
			if (mismatch)
			{
				try
				{
					sb.Append(Breakdown.StateDiff(EInventoryType.Coal));
					sb.Append(Breakdown.StateDiff(EInventoryType.Copper));
				}
				catch (Exception e) { sb.Append($" (state diagnostics failed: {e.Message})"); }
			}
			if (excluded.Count > 0)
				sb.Append($" Left out (listed under another state, never delivered): {string.Join("; ", excluded)}.");
			if (mismatch)
				Plugin.Logger.LogWarning(sb + " (mismatch: see docs/resource-breakdown.md)");
			else
				Plugin.Logger.LogInfo(sb.ToString());
		}
	}
}
