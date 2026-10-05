// Diagnostic: logs, once per game day, how the game shares out reinforcements in towns where Bulk Recruit was used
// (or whose recruit screen was opened). Reinforcement runs through GarrisonModel.RestoreHealth(budget) /
// Garrison.RestoreHealth(budget): the budget is handed to each unit in priority order via
// WorldUnitInitData.RestoreHealth(budget, locality), and a company is skipped while its restoreRequirements show
// something missing. The log line per unit shows HP before/after, supplies, priorities and those missing items,
// so "why isn't this regiment reinforcing" can be answered from BepInEx\LogOutput.log.
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace UGARBulkRecruit
{
	internal static class ReinforceLog
	{
		static readonly HashSet<IntPtr> _watched = new HashSet<IntPtr>();
		static readonly Dictionary<IntPtr, int> _loggedDay = new Dictionary<IntPtr, int>();

		public static bool IsWatched(World.SceneObject.RegionLocality loc) => loc != null && _watched.Contains(loc.Pointer);

		public static void Watch(World.SceneObject.RegionLocality loc)
		{
			if (loc != null)
				_watched.Add(loc.Pointer);
		}

		internal sealed class Snapshot
		{
			public World.SceneObject.Garrison Garrison;
			public float Budget;
			public readonly List<(World.WorldUnitInitData unit, int hp)> Units = new List<(World.WorldUnitInitData, int)>();
		}

		public static Snapshot Before(World.SceneObject.Garrison g, float budget)
		{
			if (!Plugin.VerboseLog.Value || g == null)
				return null;
			var loc = g.locality;
			if (loc == null)
				return null;
			if (!_watched.Contains(loc.Pointer))
			{
				// Also log any of the player's towns with an understrength regiment.
				if (loc.ownerNation != RuntimeVars.playerNation)
					return null;
				bool weak = false;
				var us = g.units;
				for (int i = 0; us != null && i < us.Count && !weak; i++)
					weak = us[i] != null && us[i].IntHP < us[i].MaxHP - 0.5f;
				if (!weak)
					return null;
			}
			int day = RuntimeVars.date.DayOfYear + RuntimeVars.date.Year * 400;
			if (_loggedDay.TryGetValue(loc.Pointer, out var d) && d == day)
				return null;
			_loggedDay[loc.Pointer] = day;
			var s = new Snapshot { Garrison = g, Budget = budget };
			var units = g.units;
			for (int i = 0; units != null && i < units.Count; i++)
				if (units[i] != null)
					s.Units.Add((units[i], units[i].IntHP));
			return s;
		}

		public static void After(Snapshot s, float left)
		{
			if (s == null)
				return;
			var loc = s.Garrison.locality;
			var sb = new StringBuilder();
			sb.Append($"Reinforcement {RuntimeVars.date:yyyy-MM-dd} {loc?.Name}: budget {s.Budget:0.#} -> {left:0.#} left, town recruits {loc?.Recruits}, ammo {loc?.AmmunitionStored:0}, provision {s.Garrison.Provision:0}");
			foreach (var (unit, hp) in s.Units)
			{
				sb.Append($"\n  {unit.Name}: hp {hp} -> {unit.IntHP} / {unit.MaxHP:0}, provision {unit.Provision:0.#}, ammo {unit.Ammunition:0.#}, priority {unit.DeliveryPriority}, recruit priority {unit.RecruitsDeliveryPriority}");
				var reg = unit.TryCast<World.RegimentInitData>();
				var comps = reg?.companies;
				var missing = new HashSet<string>();
				var weapons = new Dictionary<string, (WeaponTemplate w, Fight.BrigadeModelInitData c)>();
				for (int i = 0; comps != null && i < comps.Length; i++)
				{
					// restoreRequirements is indexed by EUnitNotifications (length MAX = 20), not EInventoryType.
					var req = comps[i]?.restoreRequirements;
					for (int j = 0; req != null && j < req.Length; j++)
						if (req[j])
						{
							missing.Add(((World.EUnitNotifications)j).ToString());
							if (j == (int)World.EUnitNotifications.WEAPONS || j == (int)World.EUnitNotifications.GUNS)
							{
								var w = comps[i].Weapon;
								if (w != null)
									weapons[w.Name] = (w, comps[i]);
							}
						}
				}
				if (missing.Count > 0)
					sb.Append($", companies waiting on: {string.Join(", ", missing)}");
				foreach (var kv in weapons)
				{
					var country = Game.PlayerCountry();
					float home = country?.inventory?.itemStorage?.GetItemCount(kv.Value.w) ?? -1;
					float eu = country?.europeanManager?.inventory?.itemStorage?.GetItemCount(kv.Value.w) ?? -1;
					sb.Append($"\n    weapon {kv.Key}: treasury stock {home:0}, England stock {eu:0}");
				}
				weapons.Clear();
			}
			Plugin.Logger.LogInfo(sb.ToString());
		}
	}

	internal static class ReinforceLogPatches
	{
		// Watch whichever town the player opens.
		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.UI.GeneralPage.LocalityInfo.LocalityInfoPanel), nameof(World.UI.GeneralPage.LocalityInfo.LocalityInfoPanel.Init), new[] { typeof(World.SceneObject.RegionLocality) })]
		static void LocalityInitPostfix(World.SceneObject.RegionLocality __0)
		{
			try { ReinforceLog.Watch(__0); } catch (Exception) { }
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(World.SceneObject.Garrison), nameof(World.SceneObject.Garrison.RestoreHealth))]
		static void GarrisonPrefix(World.SceneObject.Garrison __instance, float __0, out ReinforceLog.Snapshot __state)
		{
			__state = null;
			try { __state = ReinforceLog.Before(__instance, __0); } catch (Exception) { }
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.SceneObject.Garrison), nameof(World.SceneObject.Garrison.RestoreHealth))]
		static void GarrisonPostfix(float __result, ReinforceLog.Snapshot __state)
		{
			try { ReinforceLog.After(__state, __result); } catch (Exception e) { Plugin.Logger.LogWarning($"Reinforcement log failed: {e.Message}"); }
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(World.GarrisonModel), nameof(World.GarrisonModel.RestoreHealth))]
		static void ModelPrefix(World.GarrisonModel __instance, float __0, out ReinforceLog.Snapshot __state)
		{
			__state = null;
			try { __state = ReinforceLog.Before(__instance.garrison, __0); } catch (Exception) { }
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.GarrisonModel), nameof(World.GarrisonModel.RestoreHealth))]
		static void ModelPostfix(float __result, ReinforceLog.Snapshot __state)
		{
			try { ReinforceLog.After(__state, __result); } catch (Exception e) { Plugin.Logger.LogWarning($"Reinforcement log failed: {e.Message}"); }
		}
	}
}
