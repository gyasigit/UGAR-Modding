// Nation-wide effects ("countryModifiers"): added to the owner country's modifier array.
//
// ModifiersManager.Calculate clears modifiers[] and re-sums staticList + dynamicList. A country's lists
// are filled by events/research/doctrines and saved with the campaign, so we don't add list entries (they would be
// saved and duplicated). Instead a Calculate postfix adds our totals to the array after every recalculation, and
// Sync() recalculates a country when its totals change. Everything the game reads goes through that array
// (GetModifierValue), e.g. the militia limit: LocalityCreatePanel.CheckLimitsAndOtherBlockers and the army tooltip
// use ToInt(country.modifiersManager.GetModifierValue(BLOCK_MILITA)) when NationSettings.armyCreation.hasMilitiaLimit.
using System;
using System.Collections.Generic;
using UnityEngine;
using World;
using World.Modifiers;
using World.SceneObject;

namespace UGARCustomBuildings
{
	internal static class CountryEffects
	{
		sealed class Applied
		{
			public ModifiersManager Manager;
			public ENation Nation;
			public Dictionary<int, float> Totals;
			public int Version = -1;    // dataVersion right after our postfix added the totals
		}

		static readonly Dictionary<IntPtr, Applied> _applied = new Dictionary<IntPtr, Applied>();
		static float _nextSync;
		static bool _dirty = true;

		public static bool Any
		{
			get
			{
				foreach (var e in Registry.Entries)
					if (e.Def.CountryModifiers.Count > 0) return true;
				return false;
			}
		}

		public static void MarkDirty() => _dirty = true;

		/// <summary>Called every frame; syncs when buildings changed, and every few seconds anyway (captures, demolitions).</summary>
		public static void Tick()
		{
			float now = Time.realtimeSinceStartup;
			if (!_dirty && now < _nextSync)
				return;
			_dirty = false;
			_nextSync = now + 5f;
			try
			{
				Sync();
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Custom building nation effects not updated: {e.Message}");
			}
		}

		static void Sync()
		{
			var desired = new Dictionary<IntPtr, Applied>();
			if (Plugin.Enabled.Value && Any && MonoBehaviourSingleton<SceneManager>.instance != null)
			{
				var byNation = new Dictionary<ENation, Dictionary<int, float>>();
				foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
				{
					if (l == null) continue;
					foreach (var (e, strength) in Growth.Built(l))
					{
						foreach (var m in e.Def.CountryModifiers)
						{
							if (!Enum.TryParse<EModifier>(m.Modifier, true, out var mod)) continue;
							if (!byNation.TryGetValue(l.ownerNation, out var t)) byNation[l.ownerNation] = t = new Dictionary<int, float>();
							t.TryGetValue((int)mod, out float v);
							t[(int)mod] = v + m.Value * strength;
						}
					}
				}
				foreach (var kv in byNation)
				{
					var mm = ENationUtil.GetCountry(kv.Key)?.modifiersManager;
					if (mm != null)
						desired[mm.Pointer] = new Applied { Manager = mm, Nation = kv.Key, Totals = kv.Value };
				}
			}

			// Recalculate every country whose totals changed (including ones that lost all their buildings).
			var changed = new List<Applied>();
			foreach (var kv in desired)
				if (!_applied.TryGetValue(kv.Key, out var old) || !Same(old.Totals, kv.Value.Totals))
					changed.Add(kv.Value);
			var removed = new List<Applied>();
			foreach (var kv in _applied)
				if (!desired.ContainsKey(kv.Key))
					removed.Add(kv.Value);
			if (changed.Count == 0 && removed.Count == 0)
			{
				// Same totals. If the array was re-summed without our postfix (an inlined Calculate), add them again.
				foreach (var a in _applied.Values)
					if (a.Manager.dataVersion != a.Version)
						a.Manager.Calculate();
				return;
			}

			_applied.Clear();
			foreach (var kv in desired)
				_applied[kv.Key] = kv.Value;
			foreach (var a in removed)
			{
				try { a.Manager.Calculate(); } catch { /* manager of a campaign that was unloaded */ }
			}
			foreach (var a in changed)
			{
				a.Manager.Calculate();
				foreach (var t in a.Totals)
				{
					var mod = (EModifier)t.Key;
					Plugin.Logger.LogInfo($"Custom buildings: {a.Nation} {mod} is now {a.Manager.GetModifierValue(mod, false):0.##} ({t.Value:+0.##;-0.##} from custom buildings).");
				}
				WarnIfNoMilitiaLimit(a);
			}
		}

		static bool Same(Dictionary<int, float> a, Dictionary<int, float> b)
		{
			if (a.Count != b.Count) return false;
			foreach (var kv in a)
				if (!b.TryGetValue(kv.Key, out var v) || Math.Abs(v - kv.Value) > 1e-4f) return false;
			return true;
		}

		static readonly HashSet<ENation> _warned = new HashSet<ENation>();

		static void WarnIfNoMilitiaLimit(Applied a)
		{
			if (!a.Totals.ContainsKey((int)EModifier.BLOCK_MILITA) || _warned.Contains(a.Nation))
				return;
			_warned.Add(a.Nation);
			var settings = ENationUtil.GetNationSettings(a.Nation);
			var ac = settings?.armyCreation;
			if (ac != null && !ac.hasMilitiaLimit)
				Plugin.Logger.LogWarning($"Custom buildings: {a.Nation} has no militia limit in this game, so a higher BLOCK_MILITA changes nothing.");
		}

		/// <summary>Postfix of ModifiersManager.Calculate: add our totals after the game re-summed the array.</summary>
		public static void OnCalculated(ModifiersManager mm)
		{
			if (_applied.Count == 0 || mm == null || !_applied.TryGetValue(mm.Pointer, out var a))
				return;
			var arr = mm.modifiers;
			if (arr == null)
				return;
			foreach (var t in a.Totals)
				if (t.Key >= 0 && t.Key < arr.Length)
					arr[t.Key] = arr[t.Key] + t.Value;
			a.Version = mm.dataVersion;
		}
	}
}
