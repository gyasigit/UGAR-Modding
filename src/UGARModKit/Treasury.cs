// Money, supplies (construction materials) and specialists (reserve officers). Same calls as UGAR British Events'
// Costs.cs: Inventory.PostMoneyOperation / PostConstructionOperation, Country.ReserveSpecialistList.
using System;
using System.Collections.Generic;
using System.Globalization;
using World;
using World.SceneObject;

namespace UGAR.ModKit
{
	/// <summary>A price in money, supplies and specialists.</summary>
	public struct Price
	{
		/// <summary>Money.</summary>
		public float Money;
		/// <summary>Supplies: construction materials, the resource buildings cost.</summary>
		public float Supplies;
		/// <summary>Specialists: officers from the reserve pool.</summary>
		public int Specialists;

		/// <summary>Makes a price.</summary>
		public Price(float money, float supplies = 0f, int specialists = 0)
		{
			Money = money;
			Supplies = supplies;
			Specialists = specialists;
		}

		/// <summary>True when nothing is asked.</summary>
		public bool IsFree => Money <= 0 && Supplies <= 0 && Specialists <= 0;

		/// <summary>Whether the country can pay; otherwise <paramref name="missing"/> says what is short.</summary>
		public bool CanAfford(Country country, out string missing)
		{
			var parts = new List<string>();
			if (country == null)
			{
				missing = "No campaign loaded";
				return false;
			}
			var inv = country.inventory;
			if (Money > 0 && inv.Money < Money) parts.Add($"{N(Money - inv.Money)} money");
			if (Supplies > 0 && inv.ConstructionMaterials < Supplies) parts.Add($"{N(Supplies - inv.ConstructionMaterials)} supplies");
			if (Specialists > 0 && country.SpecialistCount < Specialists) parts.Add($"{Specialists - country.SpecialistCount} specialist(s)");
			missing = parts.Count == 0 ? null : "Not enough: " + string.Join(", ", parts) + " short";
			return parts.Count == 0;
		}

		/// <summary>Pays the price if the country can afford it. Returns false (and pays nothing) otherwise.</summary>
		public bool TryPay(Country country)
		{
			if (!CanAfford(country, out _))
				return false;
			var inv = country.inventory;
			if (Money > 0)
				inv.PostMoneyOperation(EMoneyOperation.OTHER, -Money);
			if (Supplies > 0)
				inv.PostConstructionOperation(EConstructionOperation.FUNDING, -Supplies, true);
			if (Specialists > 0)
			{
				// Lowest level first.
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
			Treasury.Refresh(country);
			return true;
		}

		/// <summary>"500 money, 10 supplies", or "nothing".</summary>
		public override string ToString()
		{
			var parts = new List<string>();
			if (Money > 0) parts.Add($"{N(Money)} money");
			if (Supplies > 0) parts.Add($"{N(Supplies)} supplies");
			if (Specialists > 0) parts.Add(Specialists == 1 ? "1 specialist" : $"{Specialists} specialists");
			return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
		}

		internal static string N(float v) => v.ToString("#,0", CultureInfo.InvariantCulture);
	}

	/// <summary>Reads and changes a country's money and supplies.</summary>
	public static class Treasury
	{
		/// <summary>Current money.</summary>
		public static float Money(Country country) => country.inventory.Money;

		/// <summary>Current supplies (construction materials).</summary>
		public static float Supplies(Country country) => country.inventory.ConstructionMaterials;

		/// <summary>Officers in the reserve pool.</summary>
		public static int Specialists(Country country) => country.SpecialistCount;

		/// <summary>Adds money (negative takes it away). Shows in the treasury history as "other".</summary>
		public static void AddMoney(Country country, float amount)
		{
			country.inventory.PostMoneyOperation(EMoneyOperation.OTHER, amount);
			Refresh(country);
		}

		/// <summary>Adds supplies (negative takes them away).</summary>
		public static void AddSupplies(Country country, float amount)
		{
			country.inventory.PostConstructionOperation(EConstructionOperation.FUNDING, amount, true);
			Refresh(country);
		}

		internal static void Refresh(Country country)
		{
			try { country.inventory.TriggerChanged(); } catch { /* only refreshes the resource bar */ }
		}
	}
}
