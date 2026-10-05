// Local reinforcement: lets an understrength town garrison draw replacements from the town's own recruits.
//
// How the game reinforces a garrison:
//   RegionLocality.UpdateSupplyRequest (every 1-3 days, colonies only, only if SupplyManager.IsDestinationSafe)
//   asks up to 9 towns within range (SceneManager.GetLocalitiesByDistance) for provisions, ammunition and recruits,
//   and sends a supply wagon. RegionLocality.GetRecruitsDelivery only hands out recruits from a town whose OWN
//   garrison is at full strength. So a town with understrength regiments can never use its own recruit pool,
//   and if its neighbours' garrisons are also understrength (or out of range, or the route is unsafe), nothing
//   ever arrives: that's what happened to New Haven. The wagon's arrival is what calls GarrisonModel.RestoreHealth.
//   Remote garrisons (Country.RemoteRegion, e.g. England) are instead refilled daily by
//   Garrison.UpdateRemoteSupplyRequest, which does use the town's own recruits: GetRecruits(n, flow 3) then
//   Garrison.RestoreHealth(n). This file applies that same remote-garrison step to stuck colonial towns.
//
// Only runs when no supply wagon is already heading to the town, so normal deliveries are untouched.
// Companies still need weapons/horses in stock to take on men (RestoreHealth checks that itself).
using System;
using HarmonyLib;
using UnityEngine;
using World.SceneObject;

namespace UGARBulkRecruit
{
	internal static class LocalReinforce
	{
		public static void Daily(World.ArmyManager army)
		{
			if (!Plugin.Enabled.Value || !Plugin.LocalReinforcement.Value)
				return;
			var player = Game.PlayerCountry();
			if (player == null || army?.country == null || army.country.Pointer != player.Pointer)
				return;
			var towns = UnityEngine.Object.FindObjectsOfType<RegionLocality>();
			if (towns == null)
				return;
			for (int i = 0; i < towns.Length; i++)
			{
				var loc = towns[i];
				try
				{
					Reinforce(loc);
				}
				catch (Exception e)
				{
					Plugin.Logger.LogWarning($"Local reinforcement skipped {loc?.Name}: {e.Message}");
				}
			}
		}

		static void Reinforce(RegionLocality loc)
		{
			if (loc == null || !loc.colonyLocality || loc.ownerNation != RuntimeVars.playerNation)
				return;
			var g = loc.garrison;
			if (g == null || g.units == null || g.units.Count == 0)
				return;
			int deficit = Mathf.FloorToInt(g.MaxHP - g.HP);
			int recruits = loc.Recruits;
			if (deficit < 1 || recruits < Plugin.LocalReinforcementMinRecruits.Value)
				return;
			var onTheWay = loc.supplyOnTheMove;
			if (onTheWay != null && onTheWay.Count > 0)
				return; // the game's own delivery is already coming

			int offer = Math.Min(Math.Min(deficit, recruits), Plugin.LocalReinforcementPerDay.Value);
			float left = g.RestoreHealth(offer);
			int used = Mathf.Clamp(offer - Mathf.CeilToInt(left), 0, offer);
			if (used > 0)
				loc.GetRecruits(used, (World.EHumanResourceFlow)3, null);
			if (Plugin.VerboseLog.Value && (used > 0 || ReinforceLogIsWatched(loc)))
				Plugin.Logger.LogInfo((string)$"Local reinforcement {RuntimeVars.date.ToString("yyyy-MM-dd")} {loc.Name}: short {deficit}, town recruits {recruits}, offered {offer}, used {used}");
		}

		static bool ReinforceLogIsWatched(RegionLocality loc) => ReinforceLog.IsWatched(loc);
	}

	internal static class LocalReinforcePatches
	{
		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.ArmyManager), nameof(World.ArmyManager.UnitGlobalMapLogicDaily))]
		static void DailyPostfix(World.ArmyManager __instance)
		{
			try
			{
				LocalReinforce.Daily(__instance);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Local reinforcement failed: {e}");
			}
		}
	}
}
