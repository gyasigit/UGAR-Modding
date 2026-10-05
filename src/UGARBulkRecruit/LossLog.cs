// Diagnostic: why garrison regiments lose men every day. All garrison losses go through
// GarrisonModel.ApplyDamage(a, b, ERegimentDamageKind) (CASUALTIES 0, DESERTERS 1, DECEASE 2), called from
// WorldUnitModel.CalculateWeatherEffect / CalculateMoraleEffect / ApplyDamage and others.
// We measure the garrison's HP before/after each call and print one summary per town per day.
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace UGARBulkRecruit
{
	internal static class LossLog
	{
		sealed class Day
		{
			public string Town;
			public readonly Dictionary<string, (float lost, int calls, float a, float b)> ByKind = new Dictionary<string, (float, int, float, float)>();
		}

		static readonly Dictionary<IntPtr, Day> _today = new Dictionary<IntPtr, Day>();
		static string _context = "other";

		public static void Record(World.GarrisonModel g, float hpBefore, float a, float b, World.ERegimentDamageKind kind)
		{
			var garrison = g.garrison;
			var loc = garrison?.locality;
			if (loc == null || loc.ownerNation != RuntimeVars.playerNation)
				return;
			float lost = hpBefore - garrison.HP;
			if (!_today.TryGetValue(loc.Pointer, out var day))
				_today[loc.Pointer] = day = new Day { Town = loc.Name };
			string key = $"{kind} via {_context}";
			day.ByKind.TryGetValue(key, out var v);
			day.ByKind[key] = (v.lost + lost, v.calls + 1, a, b);
		}

		public static void SetContext(string c) => _context = c;

		public static void Flush()
		{
			if (_today.Count == 0)
				return;
			var sb = new StringBuilder($"Garrison losses {RuntimeVars.date.ToString("yyyy-MM-dd")}:");
			foreach (var d in _today.Values)
			{
				float total = 0;
				foreach (var v in d.ByKind.Values)
					total += v.lost;
				if (total < 0.5f)
					continue;
				sb.Append($"\n  {d.Town}: {total:0} men");
				foreach (var kv in d.ByKind)
					if (kv.Value.lost >= 0.5f)
						sb.Append($"; {kv.Key} {kv.Value.lost:0} ({kv.Value.calls} calls, last args {kv.Value.a:0.###}, {kv.Value.b:0.###})");
			}
			_today.Clear();
			if (Plugin.VerboseLog.Value)
				Plugin.Logger.LogInfo(sb.ToString());
		}
	}

	internal static class LossLogPatches
	{
		[HarmonyPrefix]
		[HarmonyPatch(typeof(World.GarrisonModel), nameof(World.GarrisonModel.ApplyDamage), new[] { typeof(float), typeof(float), typeof(World.ERegimentDamageKind) })]
		static void ApplyDamagePrefix(World.GarrisonModel __instance, out float __state)
		{
			__state = 0f;
			try { __state = __instance.garrison?.HP ?? 0f; } catch (Exception) { }
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.GarrisonModel), nameof(World.GarrisonModel.ApplyDamage), new[] { typeof(float), typeof(float), typeof(World.ERegimentDamageKind) })]
		static void ApplyDamagePostfix(World.GarrisonModel __instance, float __0, float __1, World.ERegimentDamageKind __2, float __state)
		{
			try { LossLog.Record(__instance, __state, __0, __1, __2); } catch (Exception) { }
		}

		// Tag which daily step caused the damage.
		[HarmonyPrefix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.CalculateWeatherEffect))]
		static void WeatherPrefix() => LossLog.SetContext("weather");

		[HarmonyPrefix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.CalculateMoraleEffect))]
		static void MoralePrefix() => LossLog.SetContext("morale");

		[HarmonyPrefix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.CheckMaxMorale))]
		static void MaxMoralePrefix() => LossLog.SetContext("max morale");

		[HarmonyPrefix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.UpdateSupplyRequest))]
		static void SupplyPrefix() => LossLog.SetContext("supply");

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.CalculateWeatherEffect))]
		static void WeatherPostfix() => LossLog.SetContext("other");

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.CalculateMoraleEffect))]
		static void MoralePostfix() => LossLog.SetContext("other");

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.CheckMaxMorale))]
		static void MaxMoralePostfix() => LossLog.SetContext("other");

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.WorldUnitModel), nameof(World.WorldUnitModel.UpdateSupplyRequest))]
		static void SupplyPostfix() => LossLog.SetContext("other");

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.ArmyManager), nameof(World.ArmyManager.UnitGlobalMapLogicDaily))]
		static void DailyPostfix()
		{
			try { LossLog.Flush(); } catch (Exception e) { Plugin.Logger.LogWarning($"Loss log failed: {e.Message}"); }
		}
	}
}
