// Where the resources on the Production screen come from (see
// docs/resource-breakdown.md).
//
// The panel (World.UI.GeneralPage.Production.GeneralPanel.AvailableResouces.OnProductionOrderChanged) shows, per
// resource, "N (M)": N = Mathf.RoundToInt(stock) in country.inventory.itemStorage (europeanManager.inventory on the
// England screen), M = sum over production orders with plants > 0 of component.count * ProductionOrder.PerTick,
// only when M > 0. PerTick = free plants of the order's type left after earlier orders, capped at maxPlants,
// / item.ProductionCost: the items it can make per day, so M is the daily use at full speed.
//
// Coming in, weekly (State.WeeklyUpdate -> State.UpdateRegions): State.localStorage is cleared, every active region
// runs Region.ManualUpdate -> CalculateRegion -> CalculateResourcesProduction, which reports the player's share
// (Region.owners[player]) to State.ReportResources: ore = orePoints * ResourceItem.miningComplexity for each ore the
// region has (availableResources), plus horse/furs/cigars/rum/cloth/supplies/cotton/tobacco/sugar points. Then
// State.TransferResources hands each stored item to Country.ReportResources: items whose ResourceItem.freeProduction
// is set go in as they are; the rest are multiplied by State.resourceExtraction * DepartmentEffect(ResourceExtraction)
// and cost money (mining expenses, money operation 0x17). Nothing transfers when State.resourceExtraction <= 0.
// Region.CurrentResourceProduction(nation, list, false, -1, true) returns the same raw per-region numbers, which is
// what this file uses. Settlement conversion buildings (Weaver's etc., LocalityConstructionSettings.conversionSettings)
// also run weekly, moving from[] -> to[] in country.inventory.
//
// Going out, daily (Country.DailyUpdate -> Country.UpdateProductionManager): each order makes
// min(plants / ProductionCost * department bonus, what resources and money allow) items and removes
// component.count * made * (1 + DepartmentEffect(ResourceConsumption)) of each component.
using System;
using System.Collections.Generic;
using World;
using World.ColonialStorage;
using World.SceneObject;

namespace UGARResourceBreakdown
{
	internal sealed class Line
	{
		public string Label;
		public string Note;
		public float Amount;
	}

	internal sealed class ResourceReport
	{
		public string Name;
		public bool England;            // the screen shows Britain's England storage/production
		public float Stock;
		public float OtherStock = -1f;  // the other storage (England when viewing the colony, and the other way round)
		public float PanelDaily;        // the "(M)" number, computed the game's way
		public float DailyUse;          // with the ResourceConsumption department effect
		public float ConsumptionEffect;
		public float Factor;            // weekly extraction multiplier for mined resources (1 for free production)
		public bool FreeProduction;
		public readonly List<Line> Regions = new List<Line>();
		public readonly List<Line> Conversions = new List<Line>();   // + made / - used per week by settlement buildings
		public readonly List<Line> Orders = new List<Line>();        // per day
		public float WeeklyRegions, WeeklyConversion;
		public int StatesStopped;      // states with resource extraction at 0 that hold producing regions
		public float AtSeaToColony, AtSeaToEurope, InPorts;
		public float LastWeekReceived = -1f;
		public string LastWeekDate;
		public bool OrdersShort;
		public string Error;

		public float WeeklyIn => WeeklyRegions + WeeklyConversion;
		public float WeeklyNet => WeeklyIn - DailyUse * 7f;
	}

	internal sealed class PlantReport
	{
		public bool Shipyard;
		public bool England;
		public int Total, Used;
		public readonly List<Line> Orders = new List<Line>();
		public string Error;
	}

	internal static class Breakdown
	{
		public static readonly EInventoryType[] PanelTypes =
		{
			EInventoryType.Coal, EInventoryType.Copper, EInventoryType.Iron,
			EInventoryType.Saltpeter, EInventoryType.Wood, EInventoryType.Textiles,
		};

		public static Country PlayerCountry
		{
			get
			{
				var sm = MonoBehaviourSingleton<SceneManager>.instance;
				return sm == null ? null : sm.PlayerCountry;
			}
		}

		public static DateTime Today
		{
			get
			{
				try
				{
					var d = RuntimeVars.date;
					return new DateTime(d.Year, d.Month, d.Day);
				}
				catch (Exception) { return default; }
			}
		}

		public static PathAsset Asset(EInventoryType type)
		{
			var items = Config.game?.inventorySettings?.inventoryItems;
			int i = (int)type;
			return items != null && i >= 0 && i < items.Length ? items[i] : null;
		}

		static bool Same(PathAsset a, PathAsset b) => a != null && b != null && a.Pointer == b.Pointer;

		static string AssetName(PathAsset a, EInventoryType fallback)
		{
			try
			{
				var r = a?.TryCast<ResourceItem>();
				if (r != null && !string.IsNullOrEmpty(r.Name))
					return r.Name;
			}
			catch (Exception) { }
			return fallback == EInventoryType.Textiles ? "Cloth" : fallback.ToString();
		}

		static float DeptEffect(EDepartmentEffect e)
		{
			try
			{
				var dm = MonoBehaviourSingleton<DepartmentManager>.instance;
				return dm != null ? dm.GetEffect(e) : 0f;
			}
			catch (Exception) { return 0f; }
		}

		public static ItemStorage ShownStorage(Country c, out bool england)
		{
			england = false;
			try { england = c.IsEuropeanAction && c.europeanManager != null; } catch (Exception) { }
			return england ? c.europeanManager.inventory?.itemStorage : c.inventory?.itemStorage;
		}

		static ProductionManager ShownProduction(Country c)
		{
			try { return c.GetProductionManager(true); } catch (Exception) { return c.productionManager; }
		}

		public static IEnumerable<State> States()
		{
			var sm = MonoBehaviourSingleton<SceneManager>.instance;
			var arr = sm != null ? sm.stateArray : null;
			if (arr == null)
				yield break;
			for (int i = 0; i < arr.Length; i++)
				if (arr[i] != null)
					yield return arr[i];
		}

		// ---------------- Resource ----------------

		public static ResourceReport Resource(EInventoryType type)
		{
			var rep = new ResourceReport();
			var country = PlayerCountry;
			var asset = Asset(type);
			rep.Name = AssetName(asset, type);
			if (country == null || asset == null)
			{
				rep.Error = "No campaign loaded.";
				return rep;
			}
			try
			{
				var storage = ShownStorage(country, out rep.England);
				rep.Stock = storage != null ? storage.GetItemCount(asset) : 0f;
				var other = rep.England ? country.inventory?.itemStorage : country.europeanManager?.inventory?.itemStorage;
				if (other != null)
					rep.OtherStock = other.GetItemCount(asset);

				var res = asset.TryCast<ResourceItem>();
				rep.FreeProduction = res != null && res.freeProduction;
				Production(country, asset, rep);
				Regions(country, asset, rep);
				Conversions(country, asset, rep);
				Ships(country, asset, rep);
				if (Received.TryGet(asset, out float got, out DateTime when))
				{
					rep.LastWeekReceived = got;
					rep.LastWeekDate = when.ToString("d MMM yyyy");
				}
			}
			catch (Exception e)
			{
				rep.Error = e.Message;
				Plugin.Logger.LogWarning($"Resource breakdown for {type} failed: {e}");
			}
			return rep;
		}

		static void Production(Country country, PathAsset asset, ResourceReport rep)
		{
			var pm = ShownProduction(country);
			var orders = pm?.productionOrderList;
			rep.ConsumptionEffect = DeptEffect(EDepartmentEffect.ResourceConsumption);
			if (orders == null)
				return;
			for (int i = 0; i < orders.Count; i++)
			{
				var o = orders[i];
				if (o == null || o.plants <= 0)
					continue;
				var item = o.Item;
				var comps = item?.Components;
				if (comps == null)
					continue;
				float perTick = o.PerTick;
				for (int k = 0; k < comps.Length; k++)
				{
					var comp = comps[k];
					if (comp == null || !Same(comp.asset, asset))
						continue;
					float daily = comp.count * perTick;
					rep.PanelDaily += daily;
					string name = SafeName(item);
					rep.Orders.Add(new Line
					{
						Label = name,
						Note = $"{o.plants} plant{(o.plants == 1 ? "" : "s")}, {perTick:0.##} made/day × {comp.count:0.##} each" +
							(o.showNotEnoughtResources ? " — short of resources" : ""),
						Amount = daily,
					});
					if (o.showNotEnoughtResources)
						rep.OrdersShort = true;
				}
			}
			rep.DailyUse = rep.PanelDaily * (1f + rep.ConsumptionEffect);
			rep.Orders.Sort((a, b) => b.Amount.CompareTo(a.Amount));
		}

		static string SafeName(IManufactureItem item)
		{
			try { return item.Name; } catch (Exception) { return "?"; }
		}

		static void Regions(Country country, PathAsset asset, ResourceReport rep)
		{
			var nation = RuntimeVars.playerNation;
			float dept = DeptEffect(EDepartmentEffect.ResourceExtraction);
			var list = new Il2CppSystem.Collections.Generic.List<CountItem>();
			foreach (var state in States())
			{
				var regions = state.regions;
				if (regions == null)
					continue;
				float extraction = state.resourceExtraction;
				float factor = rep.FreeProduction ? 1f : extraction * dept;
				bool stopped = extraction <= 0f;
				bool counted = false;
				for (int i = 0; i < regions.Count; i++)
				{
					var region = regions[i];
					if (region == null || !Counts(region, nation))
						continue;
					if (!ReportsTo(region, state))
					{
						Excluded?.Add($"{region.Name} (listed in {SafeStateName(state)}, reports to {(region.state != null ? SafeStateName(region.state) : "none")})");
						continue;
					}
					list.Clear();
					try
					{
						region.CurrentResourceProduction(nation, list, false, -1f, true);
					}
					catch (Exception)
					{
						continue;
					}
					float raw = 0f;
					for (int k = 0; k < list.Count; k++)
						if (list[k] != null && Same(list[k].asset, asset))
							raw += list[k].count;
					if (raw <= 0f)
						continue;
					if (stopped)
					{
						if (!counted)
							rep.StatesStopped++;
						counted = true;
					}
					float amount = stopped ? 0f : raw * factor;
					string where = SafeStateName(state);
					rep.Regions.Add(new Line
					{
						Label = region.Name,
						Note = stopped ? $"{where}: state extraction is 0, nothing delivered"
							: rep.FreeProduction ? where : $"{where}, {raw:0.#} × {factor:0.##}",
						Amount = amount,
					});
					rep.WeeklyRegions += amount;
					rep.Factor = factor;
				}
			}
			rep.Regions.Sort((a, b) => b.Amount.CompareTo(a.Amount));
		}

		// State.UpdateRegions only runs regions whose BaseRegion.inited is set, and CalculateResourcesProduction only
		// reports owners whose control is not 0 (found by the weekly check, 1777-02-24: coal/copper 0.32 too high).
		static bool Counts(Region region, ENation nation)
		{
			try
			{
				if (!region.inited)
					return false;
				var owners = region.owners;
				int n = (int)nation;
				return owners != null && n >= 0 && n < owners.Length && owners[n].control != 0f;
			}
			catch (Exception)
			{
				return true;
			}
		}

		// A region reports to Region.state (CalculateResourcesProduction → State.ReportResources), but it's run by the state
		// whose list holds it (State.WeeklyUpdate). Each state clears its localStorage at the start of its own update and
		// transfers at the end, so a region listed under a different state than its own never gets delivered: either its
		// own state already transferred, or it clears the report when its turn comes. (Theory for the 1777-06 coal/copper
		// +0.31/week gap; Excluded lists such regions in the weekly check.)
		static bool ReportsTo(Region region, State listing)
		{
			try
			{
				var own = region.state;
				return own == null || own.Pointer == listing.Pointer;
			}
			catch (Exception)
			{
				return true;
			}
		}

		/// <summary>
		/// Weekly check diagnostics: states where the raw amount we add up from their regions differs from what the
		/// game put in State.localStorage this week (it keeps last week's reports until the next update clears it).
		/// </summary>
		internal static string StateDiff(EInventoryType type)
		{
			var asset = Asset(type);
			if (asset == null)
				return "";
			var nation = RuntimeVars.playerNation;
			var list = new Il2CppSystem.Collections.Generic.List<CountItem>();
			var parts = new List<string>();
			foreach (var state in States())
			{
				float mine = 0f, game = 0f;
				try { game = state.localStorage != null ? state.localStorage.GetItemCount(asset) : 0f; } catch (Exception) { }
				var regions = state.regions;
				if (regions != null)
				{
					for (int i = 0; i < regions.Count; i++)
					{
						var region = regions[i];
						if (region == null || !Counts(region, nation) || !ReportsTo(region, state))
							continue;
						list.Clear();
						try { region.CurrentResourceProduction(nation, list, false, -1f, true); } catch (Exception) { continue; }
						for (int k = 0; k < list.Count; k++)
							if (list[k] != null && Same(list[k].asset, asset))
								mine += list[k].count;
					}
				}
				if (Math.Abs(mine - game) > 0.001f)
					parts.Add($"{SafeStateName(state)} regions {mine:0.###} vs stored {game:0.###} (extraction {state.resourceExtraction:0.##})");
			}
			return parts.Count == 0 ? "" : $" {type} raw by state: {string.Join("; ", parts)}.";
		}

		/// <summary>When set, Regions() adds the regions it leaves out because of ReportsTo (weekly check log).</summary>
		internal static List<string> Excluded;

		static string SafeStateName(State s)
		{
			try { return string.IsNullOrEmpty(s.stateName) ? s.name : s.stateName; } catch (Exception) { return "?"; }
		}

		static void Conversions(Country country, PathAsset asset, ResourceReport rep)
		{
			var nation = RuntimeVars.playerNation;
			foreach (var state in States())
			{
				var regions = state.regions;
				if (regions == null)
					continue;
				for (int i = 0; i < regions.Count; i++)
				{
					var locs = regions[i]?.localities;
					if (locs == null)
						continue;
					for (int j = 0; j < locs.Count; j++)
					{
						var l = locs[j];
						if (l == null || l.ownerNation != nation || l.constructionSlots == null)
							continue;
						var slots = l.constructionSlots;
						for (int s = 0; s < slots.Length; s++)
						{
							var settings = slots[s]?.settings?.TryCast<LocalityConstructionSettings>();
							if (settings == null || !settings.hasResourceConversion || settings.conversionSettings == null)
								continue;
							float made = Sum(settings.conversionSettings.to, asset);
							float used = Sum(settings.conversionSettings.from, asset);
							if (made == 0f && used == 0f)
								continue;
							float net = made - used;
							rep.Conversions.Add(new Line
							{
								Label = $"{SafeHeader(settings)}, {l.Name}",
								Note = made > 0f ? Inputs(settings.conversionSettings.from) : "input",
								Amount = net,
							});
							rep.WeeklyConversion += net;
						}
					}
				}
			}
			rep.Conversions.Sort((a, b) => b.Amount.CompareTo(a.Amount));
		}

		static float Sum(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<CountItem> items, PathAsset asset)
		{
			float n = 0f;
			if (items == null)
				return n;
			for (int i = 0; i < items.Length; i++)
				if (items[i] != null && Same(items[i].asset, asset))
					n += items[i].count;
			return n;
		}

		static string Inputs(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<CountItem> from)
		{
			if (from == null || from.Length == 0)
				return "";
			var parts = new List<string>();
			for (int i = 0; i < from.Length; i++)
			{
				var c = from[i];
				if (c == null)
					continue;
				string n;
				try
				{
					var r = c.asset?.TryCast<ResourceItem>();
					n = r != null ? r.Name : c.Type.ToString();
				}
				catch (Exception) { n = "?"; }
				parts.Add($"{c.count:0.##} {n}");
			}
			return parts.Count > 0 ? "uses " + string.Join(", ", parts) : "";
		}

		static string SafeHeader(LocalityConstructionSettings s)
		{
			try { return s.Header; } catch (Exception) { return s.name; }
		}

		static void Ships(Country country, PathAsset asset, ResourceReport rep)
		{
			ColonialStorageManager csm = null;
			try { csm = country.colonialStorageManager; } catch (Exception) { }
			if (csm == null)
				return;
			try { rep.AtSeaToColony = csm.ItemOnWay(asset, EDestination.USA); } catch (Exception) { }
			try { rep.AtSeaToEurope = csm.ItemOnWay(asset, EDestination.Europe); } catch (Exception) { }
			try { rep.InPorts = csm.ItemInPorts(asset, EDestination.USA); } catch (Exception) { }
		}

		// ---------------- Factories / shipyards ----------------

		public static PlantReport Plants(bool shipyard)
		{
			var rep = new PlantReport { Shipyard = shipyard };
			var country = PlayerCountry;
			if (country == null)
			{
				rep.Error = "No campaign loaded.";
				return rep;
			}
			try
			{
				ShownStorage(country, out rep.England);
				var pm = ShownProduction(country);
				if (pm == null)
					return rep;
				rep.Total = shipyard ? pm.Shipyard : pm.Factory;
				var used = pm.usedPlants;
				int idx = shipyard ? 1 : 0;
				rep.Used = used != null && used.Length > idx ? used[idx] : 0;
				var orders = pm.productionOrderList;
				if (orders != null)
				{
					for (int i = 0; i < orders.Count; i++)
					{
						var o = orders[i];
						if (o == null || o.plantType != idx)
							continue;
						string left = o.orderCount > 0 ? $"{o.finishedCount}/{o.orderCount} done" : "continuous";
						rep.Orders.Add(new Line
						{
							Label = SafeName(o.Item),
							Note = o.plants > 0 ? $"{o.PerTick:0.##} made/day, {left}" + (o.showNotEnoughtResources ? ", short of resources" : "") +
								(o.showNotEnoughtMoney ? ", short of money" : "") : $"waiting for a free plant, {left}",
							Amount = o.plants,
						});
					}
				}
			}
			catch (Exception e)
			{
				rep.Error = e.Message;
				Plugin.Logger.LogWarning($"Plant breakdown failed: {e}");
			}
			return rep;
		}
	}

	/// <summary>What Country.ReportResources actually delivered to the player in the last weekly update.</summary>
	internal static class Received
	{
		static readonly Dictionary<IntPtr, float> _batch = new Dictionary<IntPtr, float>();
		static readonly Dictionary<IntPtr, float> _last = new Dictionary<IntPtr, float>();
		static DateTime _batchDate, _lastDate;

		public static void Add(PathAsset asset, float count)
		{
			var today = Breakdown.Today;
			if (today != _batchDate)
				Flush(today);
			_batch.TryGetValue(asset.Pointer, out float n);
			_batch[asset.Pointer] = n + count;
		}

		static void Flush(DateTime newDate)
		{
			if (_batch.Count > 0)
			{
				_last.Clear();
				foreach (var kv in _batch)
					_last[kv.Key] = kv.Value;
				_lastDate = _batchDate;
				_batch.Clear();
			}
			_batchDate = newDate;
		}

		public static bool TryGet(PathAsset asset, out float count, out DateTime date)
		{
			// The batch of the current day is complete once the weekly update has run (it all happens in one frame).
			if (_batch.Count > 0 && Breakdown.Today != _batchDate)
				Flush(Breakdown.Today);
			var src = _batch.Count > 0 ? _batch : _last;
			date = _batch.Count > 0 ? _batchDate : _lastDate;
			count = 0f;
			if (src.Count == 0)
				return false;
			src.TryGetValue(asset.Pointer, out count);
			return true;
		}
	}
}
