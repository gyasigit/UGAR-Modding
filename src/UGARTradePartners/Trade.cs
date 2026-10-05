// Read-only model of the game's overseas/native trade, rebuilt from the live game objects each time the window refreshes.
//
// How the game trades :
// - A market (MarketManager: storage + pricesSettings) has a TradingManager whose settings list 8 partners, one per
//   ENation by array index (0 France ... 7 Miamis). Each partner has seaConnection and goods Records
//   {offerCurve, demandCurve, product, offers, demands}. The player never trades with a partner directly.
// - Every day (SceneManager.OnDayEnded) TradingManager.DailyUpdate runs for the colony market (country.market /
//   country.tradingManager) and, for Britain, the England market (europeanManager.marketManager / .tradingManager):
//     f      = sea ? (1 - tension(partner, player)) * player colony tradingManager.data.deliveryPercent : 1
//     offer  = offerCurve(curvedDate) * offers * f;  adds min(offer / 7, max(0, priceOfferCurve(price) - stock))
//     demand = demandCurve(curvedDate) * demands * f; removes min(|stock|, demand / 7)
//   War, "no ports" and high tension are only warnings there (TradingManager.Notifications, set weekly); what cuts
//   sea trade is the tension and the share of trade ships lost last week (deliveryPercent = 1 - lost / created).
// - Weekly (TradingManager.WeeklyUpdate): items above maxStorageFromPriceCurve(price) lose stock * autoDecrement.
// - Prices: MarketManager.ItemCostModifier(asset, amount, factor) = costMarketAmountCurve(priceAmountConverter(price)
//   * amount) * MarketSettings.GetModifier(type) * factor. BuyItem uses (stock - count, 1.2), SellItem (stock + count, 0.8).
using System;
using System.Collections.Generic;
using UnityEngine;
using World;
using World.SceneObject;
using TradingManager = World.TradingManager.TradingManager;
using PartnerCountry = World.TradingManagerSettings.Country;

namespace UGARTradePartners
{
	internal sealed class Flow
	{
		public PathAsset Asset;
		public string Name;
		public float Offer;   // per day, before the market's room limit
		public float Demand;  // per day, before the stock limit
		public int BaseOffer, BaseDemand; // the data numbers (per week at full delivery)
	}

	internal sealed class Partner
	{
		public int Nation;
		public string Name;
		public bool Sea;
		public float Tension;      // 0..1
		public bool War, HighTension, NoPorts;
		public float Factor;       // multiplier on this partner's flows
		public readonly List<Flow> Flows = new List<Flow>();
		public bool Trades => Flows.Count > 0;
	}

	internal sealed class Good
	{
		public PathAsset Asset;
		public string Name;
		public int Type = -1;
		public float Stock, Cap, In, Out, Buy, Sell, Modifier = 1f, BasePrice;
		// What tonight's DailyUpdate will actually move, replayed in the game's order (room and stock limits applied).
		public float SimIn, SimOut;
		public float Net => SimIn - SimOut;
		public readonly List<string> Suppliers = new List<string>();
		public readonly List<string> Buyers = new List<string>();
	}

	internal sealed class MarketView
	{
		public string Label;
		public bool England;
		public MarketManager Market;
		public readonly List<Partner> Partners = new List<Partner>();
		public readonly List<Good> Goods = new List<Good>();
		public readonly List<(string name, float mod)> Modifiers = new List<(string, float)>();
		public float Delivery = 1f, CurvedDate;
		public int Ports;
		public string Error;
	}

	internal static class Trade
	{
		public static readonly string[] NationNames = { "France", "Spain", "United Colonies", "Britain", "Cherokee", "Creeks", "Iroquois", "Miamis" };

		public static string NationName(int i) => i >= 0 && i < NationNames.Length ? NationNames[i] : $"Nation {i}";

		public static Country Player()
		{
			try { return MonoBehaviourSingleton<SceneManager>.instance?.PlayerCountry; }
			catch (Exception) { return null; }
		}

		public static bool HasEngland(Country c)
		{
			try { return c?.europeanManager?.marketManager != null && c.europeanManager.tradingManager != null; }
			catch (Exception) { return false; }
		}

		public static string ItemName(PathAsset asset)
		{
			if (asset == null) return "?";
			try
			{
				var res = asset.TryCast<ResourceItem>();
				var n = res?.Name;
				if (!string.IsNullOrEmpty(n)) return Clean(n);
			}
			catch (Exception) { }
			try
			{
				var n = new IStorageItem(asset.Pointer).Name;
				if (!string.IsNullOrEmpty(n)) return Clean(n);
			}
			catch (Exception) { }
			return Clean(asset.name);
		}

		// The game's own item names are its enum spellings for some goods.
		static readonly Dictionary<string, string> Friendly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Textiles", "Cloth" }, { "Cigares", "Cigars" }, { "Tobbaco", "Tobacco" }, { "Spices", "Spice" },
			{ "ConstructionMaterial", "Construction materials" }, { "Provision", "Provisions" },
		};

		static string Clean(string s)
		{
			s = s.Replace('_', ' ').Trim();
			if (Friendly.TryGetValue(s, out var f)) return f;
			return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
		}

		static float Price(PathAsset asset)
		{
			try { return new IPriceItem(asset.Pointer).Price; }
			catch (Exception) { return 0f; }
		}

		static int TypeOf(PathAsset asset)
		{
			try { return (int)new IStorageItem(asset.Pointer).InventoryType; }
			catch (Exception) { return -1; }
		}

		static float Eval(AnimationCurve c, float t)
		{
			try { return c != null ? c.Evaluate(t) : 1f; }
			catch (Exception) { return 1f; }
		}

		/// <summary>The colony market (england = false) or Britain's England market, as the game will trade it tonight.</summary>
		public static MarketView Build(Country country, bool england)
		{
			var v = new MarketView { England = england };
			try
			{
				MarketManager market;
				TradingManager trading;
				if (england)
				{
					v.Label = "England";
					market = country.europeanManager?.marketManager;
					trading = country.europeanManager?.tradingManager;
				}
				else
				{
					v.Label = HasEngland(country) ? "Colonies" : "Market";
					market = country.market;
					trading = country.tradingManager;
				}
				v.Market = market;
				var data = trading?.data;
				var settings = data?.settings;
				if (market == null || settings == null || settings.countries == null)
				{
					v.Error = "This market has no trade partners.";
					return v;
				}

				// DailyUpdate always takes the delivery share from the player's colony trading manager, also for England.
				try { v.Delivery = country.tradingManager?.data?.deliveryPercent ?? 1f; } catch (Exception) { }
				try { v.CurvedDate = MonoBehaviourSingleton<DateTimeManager>.instance?.curvedDate ?? 0f; } catch (Exception) { }
				try { v.Ports = country.inventory?.ports ?? 0; } catch (Exception) { }

				var player = RuntimeVars.playerNation;
				PoliticsManager politics = null;
				try { politics = MonoBehaviourSingleton<PoliticsManager>.instance; } catch (Exception) { }
				var notes = data.notifications;
				AnimationCurve offerCap = null;
				try { offerCap = Config.game?.inventorySettings?.priceOfferCurve; } catch (Exception) { }
				var storage = market.storage;

				var goods = new Dictionary<IntPtr, Good>();
				Good GoodFor(PathAsset a)
				{
					if (goods.TryGetValue(a.Pointer, out var g)) return g;
					g = new Good { Asset = a, Name = ItemName(a), Type = TypeOf(a), BasePrice = Price(a) };
					try { g.Stock = storage != null ? storage.GetItemCount(a) : 0f; } catch (Exception) { }
					g.Cap = offerCap != null ? Eval(offerCap, g.BasePrice) : float.MaxValue;
					try
					{
						g.Buy = g.BasePrice * market.ItemCostModifier(a, Mathf.Round(g.Stock) - 1f, 1.2f);
						g.Sell = g.BasePrice * market.ItemCostModifier(a, Mathf.Round(g.Stock) + 1f, 0.8f);
					}
					catch (Exception) { }
					try { if (g.Type >= 0 && market.pricesSettings != null) g.Modifier = market.pricesSettings.GetModifier((EInventoryType)g.Type); } catch (Exception) { }
					goods[a.Pointer] = g;
					return g;
				}

				var countries = settings.countries;
				for (int i = 0; i < countries.Length; i++)
				{
					PartnerCountry pc = countries[i];
					if (pc == null) continue;
					// The player's own nation is a partner too: for Britain it is home trade (England supplies the tea).
					var p = new Partner { Nation = i, Name = i == (int)player ? NationName(i) + " (home)" : NationName(i), Sea = pc.seaConnection };
					try { if (politics != null) p.Tension = politics.GetTension((ENation)i, player); } catch (Exception) { }
					try { if (politics != null) p.War = politics.GetRelation(player, (ENation)i) == ERelationState.WAR; } catch (Exception) { }
					p.HighTension = p.Tension > 0.6f;
					try { p.NoPorts = notes?.noPorts != null && notes.noPorts.Contains((ENation)i); } catch (Exception) { }
					if (p.Sea && v.Ports == 0) p.NoPorts = true;
					p.Factor = p.Sea ? (1f - p.Tension) * v.Delivery : 1f;

					var recs = pc.goods;
					for (int r = 0; recs != null && r < recs.Length; r++)
					{
						var rec = recs[r];
						var a = rec?.product;
						if (a == null || (rec.offers <= 0 && rec.demands <= 0)) continue;
						var f = new Flow
						{
							Asset = a,
							Name = ItemName(a),
							BaseOffer = rec.offers,
							BaseDemand = rec.demands,
							Offer = Eval(rec.timelineDependencyOfferCurve, v.CurvedDate) * rec.offers * p.Factor / 7f,
							Demand = Eval(rec.timelineDependencyDemandCurve, v.CurvedDate) * rec.demands * p.Factor / 7f,
						};
						p.Flows.Add(f);
						var g = GoodFor(a);
						if (f.Offer > 0f || rec.offers > 0) { g.In += Math.Max(0f, f.Offer); g.Suppliers.Add(p.Name); }
						if (f.Demand > 0f || rec.demands > 0) { g.Out += Math.Max(0f, f.Demand); g.Buyers.Add(p.Name); }
					}
					v.Partners.Add(p);
				}

				// Goods the market holds that no partner touches still have a price.
				try
				{
					var items = storage?.items;
					for (int k = 0; items != null && k < items.Count; k++)
					{
						var a = items[k]?.asset;
						if (a != null && !goods.ContainsKey(a.Pointer) && Price(a) > 0f) GoodFor(a);
					}
				}
				catch (Exception) { }

				Simulate(v, goods);
				v.Goods.AddRange(goods.Values);
				v.Goods.Sort((x, y) =>
				{
					int c = (y.In + y.Out > 0f).CompareTo(x.In + x.Out > 0f);
					return c != 0 ? c : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
				});

				try
				{
					var ms = market.pricesSettings;
					if (ms != null)
						for (int t = 0; t < (int)EInventoryType.Max; t++)
						{
							float m = ms.GetModifier((EInventoryType)t);
							if (Math.Abs(m - 1f) > 0.001f) v.Modifiers.Add((TypeName(t), m));
						}
				}
				catch (Exception) { }
			}
			catch (Exception e)
			{
				v.Error = "Could not read this market: " + e.Message;
				Plugin.Logger.LogWarning($"Trade partners: reading the {v.Label} market failed: {e}");
			}
			return v;
		}

		// TradingManager.DailyUpdate per partner, per record: the stock is read once; an offer adds
		// min(max(0, cap - stock), offer / 7); a demand removes min(|stock read before the offer|, demand / 7).
		static void Simulate(MarketView v, Dictionary<IntPtr, Good> goods)
		{
			var stock = new Dictionary<IntPtr, float>();
			foreach (var kv in goods) stock[kv.Key] = kv.Value.Stock;
			foreach (var p in v.Partners)
				foreach (var f in p.Flows)
				{
					if (!goods.TryGetValue(f.Asset.Pointer, out var g)) continue;
					float s = stock[f.Asset.Pointer];
					if (f.Offer > 0f)
					{
						float add = Math.Min(Math.Max(0f, g.Cap - s), f.Offer);
						stock[f.Asset.Pointer] += add;
						g.SimIn += add;
					}
					if (f.Demand > 0f)
					{
						float rem = Math.Min(Math.Abs(s), f.Demand);
						stock[f.Asset.Pointer] -= rem;
						g.SimOut += rem;
					}
				}
		}

		public static string TypeName(int t)
		{
			switch ((EInventoryType)t)
			{
				case EInventoryType.Musket: return "Muskets";
				case EInventoryType.Cannon: return "Cannons";
				case EInventoryType.NavalGun: return "Naval guns";
				case EInventoryType.Textiles: return "Cloth";
				case EInventoryType.Cigares: return "Cigars";
				case EInventoryType.Tobbaco: return "Tobacco";
				case EInventoryType.ConstructionMaterial: return "Materials";
				case EInventoryType.SupplyLand: return "Supply";
				default: return ((EInventoryType)t).ToString();
			}
		}
	}
}
