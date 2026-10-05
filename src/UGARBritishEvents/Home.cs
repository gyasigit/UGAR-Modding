// Britain's home countries (the England screen).
//
// - Home settlements: the RegionLocality objects whose region is Country.colonialStorageManager.remoteRegion
//   ("Great Britain"; MAX_REMOTE_LOCALITY of them are unlocked, 6 in a test save). Each has raw `population` and
//   `workforce`; recruits for the convoys come from workforce (RegionLocality.CalculateRecruits).
// - Home storage: Country.europeanManager.inventory.itemStorage. Goods bought on the England screen land there
//   (MarketManager.BuyItem with Country.IsEuropeanAction) and cargo ships move goods between it and the colony storage.
//   Money, supplies (construction materials) and specialists live in Country.inventory even in Europe.
// - Population boost: like the weekly growth (Region.CalculatePopulationGrows: population += f;
//   AddWorkforce(f × RegionConfig.workforcePercent)). AddWorkforce raises both
//   population and workforce and fires OnPopulationChanged / UpdateResources, so the UI refreshes.
using System;
using System.Collections.Generic;
using World;
using World.SceneObject;

namespace UGARBritishEvents
{
	internal static class Home
	{
		public static List<RegionLocality> Settlements(Country country)
		{
			var list = new List<RegionLocality>();
			var remote = country?.colonialStorageManager?.remoteRegion;
			if (remote == null)
				return list;
			foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
				if (l != null && l.region != null && l.region.Pointer == remote.Pointer && l.ownerNation == country.Nation)
					list.Add(l);
			list.Sort((a, b) => string.CompareOrdinal(Name(a), Name(b)));
			return list;
		}

		public static ItemStorage Storage(Country country) => country?.europeanManager?.inventory?.itemStorage;

		public static PathAsset ItemAsset(EInventoryType type)
		{
			var items = Config.game?.inventorySettings?.inventoryItems;
			int i = (int)type;
			return items != null && i >= 0 && i < items.Length ? items[i] : null;
		}

		/// <summary>The asset for a type as stored at home: the catalogue asset if it is there, else a stored item of that
		/// type (ResourceItem.inventoryType) or name, else the catalogue asset.</summary>
		public static PathAsset StoredAsset(Country country, EInventoryType type)
		{
			var catalogue = ItemAsset(type);
			var items = Storage(country)?.items;
			if (items == null)
				return catalogue;
			PathAsset byType = null;
			for (int i = 0; i < items.Count; i++)
			{
				var a = items[i]?.asset;
				if (a == null) continue;
				if (catalogue != null && a.Pointer == catalogue.Pointer) return a;
				var res = a.TryCast<ResourceItem>();
				if (byType == null && ((res != null && res.inventoryType == type) || string.Equals(a.name, type.ToString(), StringComparison.OrdinalIgnoreCase)))
					byType = a;
			}
			return byType ?? catalogue;
		}

		/// <summary>Trade goods (ResourceItem assets, not weapons/ships) in home storage with at least `min` units.</summary>
		public static List<(PathAsset asset, string name, float count)> Goods(Country country, float min = 1f)
		{
			var list = new List<(PathAsset, string, float)>();
			var items = Storage(country)?.items;
			if (items == null)
				return list;
			for (int i = 0; i < items.Count; i++)
			{
				var c = items[i];
				if (c == null || c.asset == null || c.count < min) continue;
				var res = c.asset.TryCast<ResourceItem>();
				if (res == null) continue;
				list.Add((c.asset, ItemName(c.asset), c.count));
			}
			return list;
		}

		public static string ItemName(PathAsset asset)
		{
			try
			{
				var res = asset.TryCast<ResourceItem>();
				var n = res?.Name;
				if (!string.IsNullOrEmpty(n)) return n;
			}
			catch { }
			return asset.name;
		}

		public static string Name(RegionLocality l)
		{
			try { return l.Name ?? l.townName ?? "?"; } catch { return "?"; }
		}

		public struct Growth
		{
			public int Settlements;
			public int Population;
			public int Workforce;
		}

		/// <summary>The one-off boost a paid event gives, as it would apply now.</summary>
		public static Growth Plan(Country country, EventDefinition d, List<RegionLocality> homes = null)
		{
			homes ??= Settlements(country);
			var g = new Growth { Settlements = homes.Count };
			foreach (var (_, pop, wf) in PerSettlement(d, homes))
			{
				g.Population += pop;
				g.Workforce += wf;
			}
			return g;
		}

		static IEnumerable<(RegionLocality l, int pop, int wf)> PerSettlement(EventDefinition d, List<RegionLocality> homes)
		{
			if (homes.Count == 0 || (d.HomePopulationPercent == 0f && d.HomePopulation == 0))
				yield break;
			float wfShare = Config.game?.region?.workforcePercent ?? 0f;
			int flatEach = d.HomePopulation / homes.Count;
			foreach (var l in homes)
			{
				int pop = (int)(l.population * d.HomePopulationPercent) + flatEach;
				if (pop <= 0) continue;
				int wf = Math.Clamp((int)(pop * wfShare), 0, pop);
				yield return (l, pop, wf);
			}
		}

		public static Growth Apply(Country country, EventDefinition d)
		{
			var homes = Settlements(country);
			var g = new Growth { Settlements = homes.Count };
			foreach (var (l, pop, wf) in PerSettlement(d, homes))
			{
				l.population = l.population + (pop - wf);
				l.AddWorkforce(wf);     // + wf population and workforce, refreshes the settlement UI
				g.Population += pop;
				g.Workforce += wf;
				Plugin.Logger.LogInfo($"British events: \"{d.Id}\" {Name(l)}: +{pop} population (+{wf} workforce), now {l.population} / {l.workforce}.");
			}
			return g;
		}
	}
}
