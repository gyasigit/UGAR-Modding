// The price an event asks: rolled when it fires, checked against the treasury, paid immediately.
//
// - Money: Inventory.Money, paid with Inventory.PostMoneyOperation(OTHER, -amount) (State.PayConstructionCost uses
//   CONSTRUCTION the same way).
// - Supplies: construction materials, Inventory.ConstructionMaterials, paid with PostConstructionOperation(FUNDING,
//   -amount, true) (buildings pay with LOCALITY).
// - Specialists: the reserve officer pool, Country.ReserveSpecialistList (Country.SpecialistCount = its Count). Paying
//   removes officers from it like Country.RemoveReserveSpecialist does (lowest level first).
// - Home goods: Britain's England-screen storage (Home.Storage = europeanManager.inventory.itemStorage), checked with
//   GetItemCount and taken with ItemStorage.Remove(new CountItem(asset, n), 0, null), the call the game's own goods
//   conversion uses. "homeStock" picks goods that are actually in that storage when the event fires.
using System;
using System.Collections.Generic;
using System.Globalization;
using World;
using World.SceneObject;

namespace UGARBritishEvents
{
	internal sealed class Cost
	{
		public float Money;
		public float Supplies;
		public int Specialists;
		public readonly List<(PathAsset asset, string name, float amount)> HomeGoods = new List<(PathAsset, string, float)>();

		static readonly Random _rng = new Random();

		public static Cost Roll(EventDefinition d, Country country)
		{
			var c = new Cost
			{
				Money = RollOne(d.CostMoney, 50f),
				Supplies = RollOne(d.CostSupplies, 5f),
				Specialists = (int)RollOne(d.CostSpecialists, 1f),
			};
			foreach (var kv in d.CostHomeGoods)
			{
				var asset = Enum.TryParse<EInventoryType>(kv.Key, true, out var type) ? Home.StoredAsset(country, type) : null;
				if (asset == null)
				{
					Plugin.Logger.LogWarning($"British events: \"{d.Id}\" has unknown home good \"{kv.Key}\"; skipped.");
					continue;
				}
				float n = RollOne(kv.Value, 5f);
				if (n > 0)
					c.HomeGoods.Add((asset, Home.ItemName(asset), n));
			}
			if (d.HomeStockKinds > 0)
			{
				var stock = Home.Goods(country, 2f);
				for (int i = 0; i < d.HomeStockKinds && stock.Count > 0; i++)
				{
					int pick = _rng.Next(stock.Count);
					var (asset, name, count) = stock[pick];
					stock.RemoveAt(pick);
					float share = RollOne(d.HomeStockShare, 0f);
					float n = Math.Max(1f, (float)Math.Round(count * share));
					c.HomeGoods.Add((asset, name, n));
				}
			}
			return c;
		}

		/// <summary>Random amount in [min, max], rounded to a tidy step (but never outside the range).</summary>
		static float RollOne(CostRange r, float step)
		{
			if (r == null)
				return 0f;
			float v = r.Min + (float)_rng.NextDouble() * (r.Max - r.Min);
			if (step > 0f && r.Max - r.Min >= step * 2)
				v = (float)Math.Round(v / step) * step;
			else if (step >= 1f)
				v = (float)Math.Round(v);
			return Math.Clamp(v, r.Min, r.Max);
		}

		public bool IsFree => Money <= 0 && Supplies <= 0 && Specialists <= 0 && HomeGoods.Count == 0;

		/// <summary>Whether the country can pay; otherwise what is missing.</summary>
		public bool CanAfford(Country country, out string missing)
		{
			var inv = country.inventory;
			var parts = new List<string>();
			if (Money > 0 && inv.Money < Money) parts.Add($"{N(Money - inv.Money)} money");
			if (Supplies > 0 && inv.ConstructionMaterials < Supplies) parts.Add($"{N(Supplies - inv.ConstructionMaterials)} supplies");
			if (Specialists > 0 && country.SpecialistCount < Specialists) parts.Add($"{Specialists - country.SpecialistCount} specialist(s)");
			if (HomeGoods.Count > 0)
			{
				var storage = Home.Storage(country);
				foreach (var (asset, name, amount) in HomeGoods)
				{
					float have = storage == null ? 0f : storage.GetItemCount(asset);
					if (have < amount) parts.Add($"{N(amount - have)} {name} in Britain");
				}
			}
			missing = parts.Count == 0 ? null : "Not enough: " + string.Join(", ", parts) + " short";
			return parts.Count == 0;
		}

		public void Pay(Country country)
		{
			var inv = country.inventory;
			if (Money > 0)
				inv.PostMoneyOperation(EMoneyOperation.OTHER, -Money);
			if (Supplies > 0)
				inv.PostConstructionOperation(EConstructionOperation.FUNDING, -Supplies, true);
			if (Specialists > 0)
			{
				var list = country.ReserveSpecialistList;
				for (int i = 0; i < Specialists && list != null && list.Count > 0; i++)
				{
					int pick = 0;
					for (int j = 1; j < list.Count; j++)
						if (list[j] != null && (list[pick] == null || list[j].Level < list[pick].Level))
							pick = j;
					list.RemoveAt(pick);
				}
			}
			if (HomeGoods.Count > 0)
			{
				var storage = Home.Storage(country);
				foreach (var (asset, _, amount) in HomeGoods)
					storage?.Remove(new CountItem(asset, amount), (EItemSource)0, null);
				try { country.europeanManager?.inventory?.TriggerChanged(); } catch { }
			}
			try { inv.TriggerChanged(); } catch { /* only refreshes the resource bar */ }
		}

		public string Describe()
		{
			var parts = new List<string>();
			if (Money > 0) parts.Add($"{N(Money)} money");
			if (Supplies > 0) parts.Add($"{N(Supplies)} supplies");
			if (Specialists > 0) parts.Add(Specialists == 1 ? "1 specialist" : $"{Specialists} specialists");
			foreach (var (_, name, amount) in HomeGoods)
				parts.Add($"{N(amount)} {name}");
			string s = parts.Count == 0 ? "nothing" : string.Join(", ", parts);
			return HomeGoods.Count > 0 ? s + " from the home storage" : s;
		}

		static string N(float v) => v.ToString("#,0", CultureInfo.InvariantCulture);
	}
}
