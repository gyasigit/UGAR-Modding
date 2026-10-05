// Effects the game has no modifier for, applied by this plugin.
//
// Region.CalculatePopulationGrows (weekly) does for each settlement:
//   f = Population * RegionConfig.populationGrowthPercent
//   population += max(1, (int)f)
//   w = f * workforcePercent; slaves = (int)(w * slavesPercent); AddWorkforce((int)(w - slaves))
// populationGrowthBonus repeats that growth scaled by the bonus (no minimum of 1, slave share dropped);
// weeklyWorkforce adds a flat amount. AddWorkforce also raises the population, as in the game.
using System;
using System.Collections.Generic;
using World;
using World.SceneObject;

namespace UGARCustomBuildings
{
	internal struct GrowthBonus
	{
		public int Population;  // added straight to the population
		public int Workforce;   // passed to AddWorkforce (also raises the population)
		public List<string> Sources;
	}

	internal static class Growth
	{
		/// <summary>Finished custom buildings in a settlement, with the share of their effect that applies (damage).</summary>
		public static IEnumerable<(Entry entry, float strength)> Built(RegionLocality l)
		{
			var slots = l?.constructionSlots;
			if (slots == null)
				yield break;
			for (int i = 0; i < slots.Length; i++)
			{
				var c = slots[i];
				if (c == null || c.state != EConstructionState.READY)
					continue;
				var settings = c.settings;
				var e = settings == null ? null : Registry.Find(settings.Pointer);
				if (e == null || e.Def.Placeholder)
					continue;
				float strength = c.damage ? Math.Max(0f, Math.Min(1f, c.Progress)) : 1f;
				yield return (e, strength);
			}
		}

		public static GrowthBonus Weekly(RegionLocality l)
		{
			var b = new GrowthBonus();
			if (!Plugin.Enabled.Value || l == null)
				return b;
			var rc = Config.game?.region;
			if (rc == null)
				return b;
			foreach (var (e, strength) in Built(l))
			{
				var d = e.Def;
				if (!d.HasCustomEffects)
					continue;
				int wf = 0, pop = 0;
				if (d.PopulationGrowthBonus != 0f)
				{
					float f = l.Population * rc.populationGrowthPercent * d.PopulationGrowthBonus * strength;
					pop = (int)f;
					float w = f * rc.workforcePercent;
					w -= (int)(w * l.slavesPercent);
					wf += Math.Max(0, (int)w);
				}
				wf += (int)(d.WeeklyWorkforce * strength);
				if (pop == 0 && wf == 0)
					continue;
				b.Population += pop;
				b.Workforce += wf;
				(b.Sources ??= new List<string>()).Add(d.Name);
			}
			return b;
		}

		public static void Apply(RegionLocality l, GrowthBonus b)
		{
			if (b.Population != 0)
				l.population = l.population + b.Population;
			if (b.Workforce > 0)
				l.AddWorkforce(b.Workforce);
		}
	}
}
