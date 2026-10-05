using System;
using System.Collections.Generic;
using HarmonyLib;
using World.ColonialStorage;
using World.SceneObject;
using World.UI.GeneralPage.LocalityInfo;

namespace UGARRecruitGrowth
{
	internal static class Patches
	{
		internal static LocalityInfoPanel OpenPanel;

		[HarmonyPostfix]
		[HarmonyPatch(typeof(LocalityInfoPanel), nameof(LocalityInfoPanel.Init))]
		static void InitPostfix(LocalityInfoPanel __instance)
		{
			OpenPanel = __instance;
		}

		// ---- Extra lines in the game's own tooltips ----

		static IntPtr _hoverLocality;
		static string _hoverText;
		static float _hoverUntil;
		static string _nationalText;
		static float _nationalUntil;

		static void Append(Common.UI.IElement hint, string extra)
		{
			if (hint == null || string.IsNullOrEmpty(extra))
				return;
			var th = hint.TryCast<Common.UI.Hint.TextHint>();
			if (th == null)
				return;
			string current = th.Text ?? "";
			if (current.Contains("Recruit growth</color>"))
				return; // already added (the hint was reused without being rebuilt)
			th.Text = current.TrimEnd('\n') + "\n" + extra;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(RecruitsResourceHint), nameof(RecruitsResourceHint.Hint), MethodType.Getter)]
		static void RecruitsHintPostfix(RecruitsResourceHint __instance, ref Common.UI.IElement __result)
		{
			if (!Plugin.Enabled.Value || !Plugin.ShowInTooltips.Value)
				return;
			try
			{
				var l = __instance.locality;
				if (l == null)
					return;
				if (Overlay.PinnedLocality != IntPtr.Zero && l.Pointer == Overlay.PinnedLocality)
				{
					__result = null; // the pinned popover already shows this; don't stack the game tooltip on it
					return;
				}
				float now = UnityEngine.Time.realtimeSinceStartup;
				if (l.Pointer != _hoverLocality || now >= _hoverUntil)
				{
					_hoverLocality = l.Pointer;
					_hoverText = Texts.HoverSummary(l);
					_hoverUntil = now + 1f;
				}
				Append(__result, _hoverText);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Recruit growth tooltip skipped: {e.Message}");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.UI.GeneralPage.ResourcePanel.PopulationResourceElement), nameof(World.UI.GeneralPage.ResourcePanel.PopulationResourceElement.Hint), MethodType.Getter)]
		static void PopulationHintPostfix(ref Common.UI.IElement __result)
		{
			if (!Plugin.Enabled.Value || !Plugin.ShowInTooltips.Value)
				return;
			try
			{
				if (RecruitMath.PlayerCountry == null)
					return;
				if (Overlay.PinnedOverview)
				{
					__result = null;
					return;
				}
				float now = UnityEngine.Time.realtimeSinceStartup;
				if (_nationalText == null || now >= _nationalUntil)
				{
					_nationalText = Texts.NationalHover();
					_nationalUntil = now + 2f;
				}
				Append(__result, _nationalText);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Recruit growth tooltip skipped: {e.Message}");
			}
		}

		// ---- Self-check: compare what the plugin predicts with what the game actually adds each day. ----

		internal sealed class DayCheck
		{
			public int Before;
			public LocalityGrowth Predicted;
			public bool ShareMismatch;
			public float GameShare;
		}

		static DateTime _day;
		static int _checked, _predictedTotal, _actualTotal, _shareMismatches, _amountMismatches;
		static int _detailsLogged;
		const int MaxDetails = 40;

		static bool Tracked(RegionLocality l)
		{
			return l.ownerNation == RuntimeVars.playerNation || RecruitMath.IsBritishHome(l);
		}

		static void RollDay()
		{
			var today = RecruitMath.Today;
			if (today == _day)
				return;
			if (_checked > 0)
			{
				Plugin.Logger.LogInfo($"Recruit growth check {_day:yyyy-MM-dd}: {_checked} settlements, predicted +{_predictedTotal}, game added +{_actualTotal}, " +
					$"share mismatches {_shareMismatches}, amount mismatches {_amountMismatches}.");
			}
			_day = today;
			_checked = _predictedTotal = _actualTotal = _shareMismatches = _amountMismatches = 0;
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(RegionLocality), nameof(RegionLocality.CalculateRecruits))]
		static void CalculateRecruitsPrefix(RegionLocality __instance, float __0, out DayCheck __state)
		{
			__state = null;
			if (!Plugin.Enabled.Value || !Plugin.VerifyLog.Value)
				return;
			try
			{
				if (!Tracked(__instance))
					return;
				RollDay();
				var g = RecruitMath.Compute(__instance);
				__state = new DayCheck
				{
					Before = __instance.recruits,
					Predicted = g,
					GameShare = __0,
					ShareMismatch = Math.Abs(g.Share - __0) > 1e-5f * Math.Max(1f, Math.Abs(__0)),
				};
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Recruit growth check skipped: {e.Message}");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(RegionLocality), nameof(RegionLocality.CalculateRecruits))]
		static void CalculateRecruitsPostfix(RegionLocality __instance, DayCheck __state)
		{
			if (__state == null)
				return;
			try
			{
				int actual = __instance.recruits - __state.Before;
				int predicted = __state.Predicted.GainTomorrow;
				_checked++;
				_actualTotal += actual;
				_predictedTotal += predicted;
				if (__state.ShareMismatch)
					_shareMismatches++;
				if (actual != predicted)
					_amountMismatches++;
				if ((__state.ShareMismatch || actual != predicted) && _detailsLogged < MaxDetails)
				{
					_detailsLogged++;
					Plugin.Logger.LogWarning($"Recruit growth mismatch at {__state.Predicted.Name}: stock {__state.Before}, predicted +{predicted} " +
						$"(intake {__state.Predicted.Intake}, policy +{__state.Predicted.PolicyBonus}, share {__state.Predicted.Share:0.######}), " +
						$"game added +{actual} (game share {__state.GameShare:0.######}){(__state.Predicted.Error != null ? ", error: " + __state.Predicted.Error : "")}");
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Recruit growth check failed: {e.Message}");
			}
		}

		internal sealed class ConvoyCheck
		{
			public int FleetsBefore;
			public BritishPipeline Predicted;
			public bool PredictDepart;
		}

		static bool IsPlayers(ColonialStorageManager csm)
		{
			var mine = RecruitMath.PlayerCountry?.colonialStorageManager;
			return mine != null && mine.Pointer == csm.Pointer;
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(ColonialStorageManager), nameof(ColonialStorageManager.CheckRecruitsDelivery))]
		static void CheckRecruitsDeliveryPrefix(ColonialStorageManager __instance, out ConvoyCheck __state)
		{
			__state = null;
			if (!Plugin.Enabled.Value || !Plugin.VerifyLog.Value)
				return;
			try
			{
				if (!IsPlayers(__instance))
					return;
				var p = RecruitMath.Britain();
				if (p == null)
					return;
				__state = new ConvoyCheck
				{
					FleetsBefore = __instance.fleets?.Count ?? 0,
					Predicted = p,
					PredictDepart = p.Cap - (p.Pool + p.OnWay) > 0f && p.ShipsFree != 0 && p.HomeReady >= p.Threshold,
				};
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Britain convoy check skipped: {e.Message}");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ColonialStorageManager), nameof(ColonialStorageManager.CheckRecruitsDelivery))]
		static void CheckRecruitsDeliveryPostfix(ColonialStorageManager __instance, ConvoyCheck __state)
		{
			if (__state == null)
				return;
			try
			{
				var p = __state.Predicted;
				int after = __instance.fleets?.Count ?? 0;
				bool departed = after > __state.FleetsBefore;
				if (!departed && !__state.PredictDepart)
					return;
				string actual = "no ship";
				if (departed)
				{
					var now = RecruitMath.Britain();
					var newest = now?.Convoys.Count > 0 ? now.Convoys[now.Convoys.Count - 1] : null;
					var tc = __instance.fleets[after - 1];
					actual = $"ship with {(newest != null ? newest.Recruits.ToString() : "?")} recruits, {tc.travellingDays} days";
				}
				string predicted = __state.PredictDepart
					? $"ship with {Math.Min(p.ShipLoad, p.HomeReady)} recruits, {p.TravelDays} days"
					: "no ship";
				var log = departed == __state.PredictDepart ? (Action<object>)Plugin.Logger.LogInfo : Plugin.Logger.LogWarning;
				log($"Britain recruit convoy {RecruitMath.Today:yyyy-MM-dd}: predicted {predicted}; game sent {actual}. " +
					$"(home ready {p.HomeReady}, threshold {p.Threshold}, pool {p.Pool}, at sea {p.OnWay:0}, cap {p.Cap}, free ships {p.ShipsFree})");
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Britain convoy check failed: {e.Message}");
			}
		}
	}
}
