// Britain's factory and shipyard caps (England screen, World.UI.Britain.Rented.*Switcher):
//
//   cap = floatExt.ToInt(Config.game.britainCampaignConfig.rentCountFromLocalityCount.Evaluate(
//             PlayerCountry.modifiersManager.GetModifierValue(MAX_REMOTE_LOCALITY /*112*/, false)))
//
// The same cap applies to factories and to shipyards, each rented separately into EuropeanManager.rentedFactories /
// rentedShipyards. Only the switchers' Increase / Decrease / UpdateContent evaluate it (Increase clamps to it); the
// weekly rent (Country.UpdateIndependentFactories, pricePerOneRentFromRentsCount) and production just use the rented
// count, nothing re-clamps it. MAX_REMOTE_LOCALITY is also the number of home settlements, so we don't raise that.
//
// Instead, while one of those switcher methods runs we swap the config's curve for a copy shifted up by the event
// bonus (shifting every key's value by N shifts Evaluate by exactly N), and put the original back afterwards.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using World;
using World.UI.Britain.Rented;

namespace UGARBritishEvents
{
	internal static class RentCaps
	{
		static int _depth;
		static AnimationCurve _original;
		static readonly Dictionary<int, AnimationCurve> _shifted = new Dictionary<int, AnimationCurve>();

		public static AnimationCurve Curve => Config.game?.britainCampaignConfig?.rentCountFromLocalityCount;

		/// <summary>The game's own cap (without our bonus).</summary>
		public static int BaseCap()
		{
			var curve = _original ?? Curve;
			var country = Events.PlayerCountry;
			if (curve == null || country?.modifiersManager == null)
				return -1;
			return floatExt.ToInt(curve.Evaluate(country.modifiersManager.GetModifierValue(EModifier.MAX_REMOTE_LOCALITY, false)));
		}

		public static string DescribeCurve()
		{
			var curve = _original ?? Curve;
			if (curve == null)
				return "(no curve)";
			var keys = curve.keys;
			var parts = new List<string>();
			for (int i = 0; i < keys.Length; i++)
				parts.Add($"{keys[i].time:0.##}->{keys[i].value:0.##}");
			return string.Join(", ", parts);
		}

		static void Enter(int bonus)
		{
			if (_depth++ > 0 || bonus == 0)
				return;
			var cfg = Config.game?.britainCampaignConfig;
			var curve = cfg?.rentCountFromLocalityCount;
			if (curve == null)
				return;
			_original = curve;
			cfg.rentCountFromLocalityCount = Shifted(curve, bonus);
		}

		static void Exit()
		{
			if (--_depth > 0)
				return;
			_depth = 0;
			if (_original == null)
				return;
			var cfg = Config.game?.britainCampaignConfig;
			if (cfg != null)
				cfg.rentCountFromLocalityCount = _original;
			_original = null;
		}

		static AnimationCurve Shifted(AnimationCurve curve, int bonus)
		{
			if (_shifted.TryGetValue(bonus, out var c) && c != null)
				return c;
			var keys = curve.keys;
			var nk = new Il2CppStructArray<Keyframe>(keys.Length);
			for (int i = 0; i < keys.Length; i++)
			{
				var k = keys[i];
				k.value += bonus;
				nk[i] = k;
			}
			c = new AnimationCurve(nk) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
			_shifted[bonus] = c;
			return c;
		}

		/// <summary>Drops cached curves (a different campaign config may be loaded).</summary>
		public static void Reset() => _shifted.Clear();

		static IEnumerable<System.Reflection.MethodBase> Targets(Type t) => new System.Reflection.MethodBase[]
		{
			AccessTools.Method(t, "UpdateContent"),
			AccessTools.Method(t, "Increase"),
			AccessTools.Method(t, "Decrease"),
		};

		[HarmonyPatch]
		internal static class FactoriesPatch
		{
			static IEnumerable<System.Reflection.MethodBase> TargetMethods() => Targets(typeof(RentedFactoriesSwitcher));

			static void Prefix()
			{
				try { Enter(Events.Bonus.Factories); }
				catch (Exception e) { Plugin.Logger.LogWarning($"British events: factory cap not raised: {e.Message}"); }
			}

			static Exception Finalizer(Exception __exception)
			{
				try { Exit(); } catch { }
				return __exception;
			}
		}

		[HarmonyPatch]
		internal static class ShipyardsPatch
		{
			static IEnumerable<System.Reflection.MethodBase> TargetMethods() => Targets(typeof(RentedShipyardsSwitcher));

			static void Prefix()
			{
				try { Enter(Events.Bonus.Shipyards); }
				catch (Exception e) { Plugin.Logger.LogWarning($"British events: shipyard cap not raised: {e.Message}"); }
			}

			static Exception Finalizer(Exception __exception)
			{
				try { Exit(); } catch { }
				return __exception;
			}
		}
	}
}