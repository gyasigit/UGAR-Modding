// Which events have fired (and which were paid for) is stored in the campaign save itself, as marker entries in the
// country's ModifiersManager.staticList (saved by Country.Write/Load with every field).
//
// A marker is ModifierParameterData(type 0, effect 0, durationDays 0, nation = base + event code, visible false):
// - ModifiersManager.Calculate only sums entries whose nation equals the manager's nation or -1
//   (ENationUtil.Contains), so a marker never changes a modifier, and its effect is 0 anyway;
// - durationDays 0 = permanent (ManualUpdate only counts down entries > 0);
// - GetTemporaryModifiers (tooltips) only reads dynamicList.
// So a save played without this mod keeps the markers and nothing happens; with the mod they come back on load.
//
// FiredBase + code: the event was shown (effect = game date; it fires again only if "repeatable"). PaidBase + code: its
// effects apply (paid, or no cost); repeatable events count every paid marker.
using System;
using System.Collections.Generic;
using World;
using World.Modifiers;
using World.SceneObject;

namespace UGARBritishEvents
{
	internal static class SaveMarkers
	{
		public const int FiredBase = 0x40000000;    // far outside ENation (None..MaxSize) and never -1
		public const int PaidBase = 0x41000000;
		const int Span = 0x1000000;                 // event codes are 24-bit

		static readonly DateTime Epoch = new DateTime(1700, 1, 1);

		public static HashSet<int> FiredCodes(Country country) => new HashSet<int>(Scan(country, FiredBase).Keys);

		public static HashSet<int> PaidCodes(Country country) => new HashSet<int>(Scan(country, PaidBase).Keys);

		/// <summary>How many times each event was paid for (repeatable events stack).</summary>
		public static Dictionary<int, int> PaidCounts(Country country)
		{
			var d = new Dictionary<int, int>();
			foreach (var kv in Scan(country, PaidBase))
				d[kv.Key] = kv.Value.Count;
			return d;
		}

		/// <summary>Game date each event last fired (markers from 1.0-1.2.0 have no date: 1 Jan 1700).</summary>
		public static Dictionary<int, DateTime> LastFired(Country country)
		{
			var d = new Dictionary<int, DateTime>();
			foreach (var kv in Scan(country, FiredBase))
			{
				float max = 0f;
				foreach (var v in kv.Value) max = Math.Max(max, v);
				d[kv.Key] = Epoch.AddDays(max);
			}
			return d;
		}

		/// <summary>Event code -> the effect values of its markers in this range (the fired date, as days since 1700).</summary>
		static Dictionary<int, List<float>> Scan(Country country, int baseValue)
		{
			var d = new Dictionary<int, List<float>>();
			var list = country?.modifiersManager?.staticList;
			if (list == null)
				return d;
			for (int i = 0; i < list.Count; i++)
			{
				var m = list[i];
				if (m == null) continue;
				int n = (int)m.nation;
				if (n < baseValue || n >= baseValue + Span) continue;
				if (!d.TryGetValue(n - baseValue, out var vals)) d[n - baseValue] = vals = new List<float>();
				vals.Add(m.effect);
			}
			return d;
		}

		public static int StaticCount(Country country) => country?.modifiersManager?.staticList?.Count ?? -1;

		// The marker's effect is ignored by the game (nation never matches), so it carries the date (days since 1700,
		// exact in a float).
		public static void MarkFired(Country country, EventDefinition def, DateTime date) =>
			Add(country, FiredBase + def.Code, (float)(date - Epoch).TotalDays);

		public static void MarkPaid(Country country, EventDefinition def) => Add(country, PaidBase + def.Code, 0f);

		static void Add(Country country, int nation, float value)
		{
			var marker = new ModifierParameterData((EModifier)0, value, 0, (ENation)nation, false);
			country.modifiersManager.staticList.Add(marker);
		}
	}
}