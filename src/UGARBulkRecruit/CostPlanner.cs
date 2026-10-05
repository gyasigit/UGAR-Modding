// Predicts what N copies of the regiment on the recruit screen will cost, how many the player can afford,
// and which weapon each company will get when the chosen one runs out.
//
// Mirrors the game: RegimentManagementPanel.UpdateChanges blocks Create unless money, officers, renown and
// every item (weapons via WeaponTemplate.CountForHP, horses/wagons via Fight.Extensions.ResourcesForUnit) are in
// stock; CreateCompany then pays exactly those per dirty company, plus workforce (informational, never blocks).
using System;
using System.Collections.Generic;
using World;

namespace UGARBulkRecruit
{
	internal sealed class CopyPlan
	{
		/// <summary>Replacement weapon per company index for this copy (absent = keep the designed weapon).</summary>
		public readonly Dictionary<int, WeaponTemplate> Substitutes = new Dictionary<int, WeaponTemplate>();
	}

	internal sealed class Plan
	{
		public int Requested;
		public int Affordable;              // how many of Requested can be made (0 = not even the first)
		public int MaxAffordable;           // how many could be made in total, up to MaxCopies
		public string LimitedBy;            // what stops the next copy, if anything
		public float Money, Officers, Renown;
		public int Workforce;
		public bool HasCommander;
		public int HaveCommanders;              // free officers in the reserve the copies can use
		public float HaveMoney, HaveOfficers, HaveRenown;
		public int HaveWorkforce = -1;
		public string WorkforceSource;
		public readonly List<CopyPlan> Copies = new List<CopyPlan>();
		/// <summary>Item totals for the affordable copies: name, needed, in stock.</summary>
		public readonly List<(string name, float need, float have, bool weapon)> Items = new List<(string, float, float, bool)>();
		/// <summary>Weapon swaps, e.g. "Brown Bess 69 for Brown Bess 76 in copies 4-5".</summary>
		public readonly List<string> SubstitutionNotes = new List<string>();
	}

	internal static class Game
	{
		public static World.SceneObject.Country PlayerCountry()
		{
			var scene = MonoBehaviourSingleton<World.SceneManager>.instance;
			return scene?.PlayerCountry;
		}

		public static World.SceneObject.Country.Inventory PlayerInventory()
		{
			var country = PlayerCountry();
			if (country == null)
				return null;
			return country.IsEuropeanAction ? country.europeanManager?.inventory : country.inventory;
		}

		/// <summary>Money and officers always come from the country's own inventory (UpdateChanges reads them there),
		/// while items come from the European inventory during the British campaign's England phase.</summary>
		public static World.SceneObject.Country.Inventory MoneyInventory() => PlayerCountry()?.inventory;

		static bool Eligible(BaseOfficer o)
		{
			if (o == null || o.TryCast<UnitOfficer>() == null || o.IsGeneral || o.isPlayer || !o.IsAlive)
				return false;
			return true;
		}

		/// <summary>Free land officers in the reserve (what the commander picker lists), best first.</summary>
		public static List<BaseOfficer> FreeCommanders()
		{
			var result = new List<BaseOfficer>();
			var list = PlayerCountry()?.ReserveSpecialistList;
			if (list == null)
				return result;
			for (int i = 0; i < list.Count; i++)
				if (Eligible(list[i]))
					result.Add(list[i]);
			result.Sort((a, b) => b.Level != a.Level ? b.Level.CompareTo(a.Level) : b.Rank.CompareTo(a.Rank));
			return result;
		}

		public static float Renown()
		{
			var rm = MonoBehaviourSingleton<World.RenownManager>.instance;
			var d = rm?.RenownData;
			return d == null ? 0f : d.renown;
		}

		public static float Score(WeaponTemplate w)
		{
			// Price is how the designers rank weapon quality (newer/better patterns cost more);
			// efficiency breaks ties.
			return w.price * 1000f + w.Efficiency;
		}
	}

	internal static class CostPlanner
	{
		sealed class CompanyCost
		{
			public int Index;
			public Fight.BrigadeModelInitData Company;
			public WeaponTemplate Weapon;
			public int Hp;
			public float Money, Renown;
			public int Officers;
			public readonly List<(PathAsset asset, float count)> Items = new List<(PathAsset, float)>();
		}

		static List<CompanyCost> Costs(RegimentInitData reg)
		{
			var list = new List<CompanyCost>();
			var comps = reg.companies;
			if (comps == null)
				return list;
			for (int i = 0; i < comps.Length; i++)
			{
				var c = comps[i];
				if (c == null || !c.dirty)
					continue;
				var provider = new IBrigadeDataProvider(c.Pointer);
				var cc = new CompanyCost
				{
					Index = i,
					Company = c,
					Weapon = c.Weapon,
					Hp = c.IntHP,
					Money = Fight.Extensions.MoneyForUnit(provider),
					Officers = Fight.Extensions.OfficersForUnit(provider),
					Renown = Fight.Extensions.RenownCost(provider),
				};
				var items = new Il2CppSystem.Collections.Generic.List<CountItem>();
				Fight.Extensions.ResourcesForUnit(provider, items, -1f);
				for (int j = 0; j < items.Count; j++)
				{
					var it = items[j];
					if (it?.asset == null)
						continue;
					// The company's weapon is handled separately so it can be swapped.
					if (cc.Weapon != null && it.asset.Pointer == cc.Weapon.Pointer)
						continue;
					cc.Items.Add((it.asset, it.count));
				}
				list.Add(cc);
			}
			return list;
		}

		/// <summary>Best weapon this company can carry with enough left in <paramref name="stock"/>.</summary>
		static WeaponTemplate Substitute(CompanyCost cc, Func<PathAsset, float> stock)
		{
			var entity = cc.Company.entity;
			var allowed = entity?.availableWeapons;
			if (allowed == null || cc.Weapon == null)
				return null;
			WeaponTemplate best = null;
			float bestScore = float.MinValue;
			for (int i = 0; i < allowed.Length; i++)
			{
				var w = allowed[i];
				if (w == null || w.Pointer == cc.Weapon.Pointer || w.inventoryType != cc.Weapon.inventoryType)
					continue;
				if (stock(w) < w.CountForHP(cc.Hp))
					continue;
				float s = Game.Score(w);
				if (s > bestScore)
				{
					bestScore = s;
					best = w;
				}
			}
			return best;
		}

		/// <summary>Plans <paramref name="requested"/> regiments (the first one is the game's own) and how far they can go.</summary>
		public static Plan Build(RegimentInitData reg, World.SceneObject.RegionLocality locality, int requested, int maxCopies, bool hasCommander)
		{
			var plan = new Plan { Requested = requested };
			var inv = Game.MoneyInventory();
			var storage = Game.PlayerInventory()?.itemStorage;
			int freeCommanders = Plugin.AutoCommanders.Value ? Game.FreeCommanders().Count : 0;
			plan.HaveCommanders = freeCommanders;
			plan.HasCommander = hasCommander;
			if (reg == null || inv == null || storage == null)
			{
				plan.LimitedBy = "game data not ready";
				return plan;
			}
			var costs = Costs(reg);
			if (costs.Count == 0)
			{
				plan.LimitedBy = "add at least one company";
				return plan;
			}

			var stock = new Dictionary<IntPtr, float>();
			var names = new Dictionary<IntPtr, string>();
			var assets = new Dictionary<IntPtr, PathAsset>();
			float Stock(PathAsset a)
			{
				if (!stock.TryGetValue(a.Pointer, out var v))
				{
					v = storage.GetItemCount(a);
					stock[a.Pointer] = v;
					assets[a.Pointer] = a;
				}
				return v;
			}
			var used = new Dictionary<IntPtr, float>();
			void Use(PathAsset a, float n, bool isWeapon)
			{
				Stock(a);
				stock[a.Pointer] -= n;
				used[a.Pointer] = (used.TryGetValue(a.Pointer, out var u) ? u : 0f) + n;
				if (!names.ContainsKey(a.Pointer))
				{
					var w = a.TryCast<WeaponTemplate>();
					names[a.Pointer] = (isWeapon ? "W|" : "I|") + (w != null ? w.Name : a.name);
				}
			}

			float money = Math.Max(0f, inv.money), officers = Math.Max(0f, inv.officers), renown = Math.Max(0f, Game.Renown());
			plan.HaveMoney = money;
			plan.HaveOfficers = officers;
			plan.HaveRenown = renown;
			var swaps = new Dictionary<string, List<int>>();

			int limit = Math.Max(requested, maxCopies);
			for (int k = 1; k <= limit; k++)
			{
				var copy = new CopyPlan();
				float m = 0, o = 0, r = 0;
				string fail = null;
				// Weapons first: keep the designed one while it lasts, otherwise the best usable replacement.
				var pending = new Dictionary<IntPtr, float>();
				float Avail(PathAsset a) => Stock(a) - (pending.TryGetValue(a.Pointer, out var p) ? p : 0f);
				var picks = new List<(PathAsset asset, float count)>();
				foreach (var cc in costs)
				{
					m += cc.Money;
					o += cc.Officers;
					r += cc.Renown;
					if (cc.Weapon == null)
						continue;
					var w = cc.Weapon;
					int need = w.CountForHP(cc.Hp);
					if (Avail(w) < need)
					{
						// The first regiment is built by the game exactly as designed, so never swap it.
						var sub = k == 1 ? null : Substitute(cc, a => Avail(a));
						if (sub == null)
						{
							fail = $"not enough {w.Name}";
							break;
						}
						copy.Substitutes[cc.Index] = sub;
						string key = $"{sub.Name} instead of {w.Name}";
						if (!swaps.TryGetValue(key, out var ks))
							swaps[key] = ks = new List<int>();
						if (!ks.Contains(k))
							ks.Add(k);
						w = sub;
						need = w.CountForHP(cc.Hp);
					}
					pending[w.Pointer] = (pending.TryGetValue(w.Pointer, out var p) ? p : 0f) + need;
					picks.Add((w, need));
				}
				if (fail == null)
				{
					foreach (var cc in costs)
						foreach (var (asset, count) in cc.Items)
						{
							pending[asset.Pointer] = (pending.TryGetValue(asset.Pointer, out var p) ? p : 0f) + count;
							if (Stock(asset) < pending[asset.Pointer])
							{
								var w = asset.TryCast<WeaponTemplate>();
								fail = $"not enough {(w != null ? w.Name : asset.name)}";
								break;
							}
						}
				}
				if (fail == null && k == 1 && !hasCommander)
					fail = "pick a commander for the first regiment";
				if (fail == null && k > 1 && freeCommanders < k - 1)
					fail = Plugin.AutoCommanders.Value ? "no free officers left to command" : "each regiment needs its own commander (AutoCommanders is off)";
				if (fail == null && money < m)
					fail = "not enough money";
				if (fail == null && officers < o)
					fail = "not enough officers";
				if (fail == null && renown < r)
					fail = "not enough renown";
				if (fail != null)
				{
					if (k <= requested)
						plan.LimitedBy = fail;
					else if (plan.LimitedBy == null)
						plan.LimitedBy = fail;
					break;
				}

				plan.MaxAffordable = k;
				if (k > requested)
					continue; // only counting how many more would fit
				plan.Affordable = k;
				plan.Copies.Add(copy);
				money -= m;
				officers -= o;
				renown -= r;
				plan.Money += m;
				plan.Officers += o;
				plan.Renown += r;
				foreach (var (asset, count) in picks)
					Use(asset, count, true);
				foreach (var cc in costs)
				{
					plan.Workforce += cc.Hp;
					foreach (var (asset, count) in cc.Items)
						Use(asset, count, false);
				}
			}
			if (plan.MaxAffordable >= maxCopies && plan.LimitedBy == null)
				plan.LimitedBy = null;

			foreach (var kv in used)
			{
				var label = names[kv.Key];
				float have = storage.GetItemCount(assets[kv.Key]);
				plan.Items.Add((label.Substring(2), kv.Value, have, label[0] == 'W'));
			}
			foreach (var kv in swaps)
				plan.SubstitutionNotes.Add($"{kv.Key} in {Ranges(kv.Value)}");

			// Workforce is spent per soldier (informational: the game never blocks on it).
			try
			{
				var country = Game.PlayerCountry();
				var em = country?.europeanManager;
				if (em != null && locality != null && locality.colonyLocality && reg.kind != (EUnitKind)9 && reg.kind != (EUnitKind)5)
				{
					plan.HaveWorkforce = em.Recruits;
					plan.WorkforceSource = "British recruit pool";
				}
				else if (locality != null)
				{
					plan.HaveWorkforce = locality.Workforce;
					plan.WorkforceSource = $"{locality.Name} workforce";
				}
			}
			catch (Exception) { }
			return plan;
		}

		static string Ranges(List<int> copies)
		{
			copies.Sort();
			var parts = new List<string>();
			int start = copies[0], prev = copies[0];
			for (int i = 1; i <= copies.Count; i++)
			{
				if (i < copies.Count && copies[i] == prev + 1)
				{
					prev = copies[i];
					continue;
				}
				parts.Add(start == prev ? $"#{start}" : $"#{start}-{prev}");
				if (i < copies.Count)
					start = prev = copies[i];
			}
			return (copies.Count == 1 ? "regiment " : "regiments ") + string.Join(", ", parts);
		}

		/// <summary>
		/// Swaps weapons on a copy that is about to be created, against the live stock right now.
		/// Returns false if some company has no usable weapon in stock.
		/// </summary>
		public static bool ApplySubstitutes(RegimentInitData copy, out string swapped)
		{
			swapped = null;
			var storage = Game.PlayerInventory()?.itemStorage;
			if (storage == null)
				return true;
			var pending = new Dictionary<IntPtr, float>();
			float Avail(PathAsset a) => storage.GetItemCount(a) - (pending.TryGetValue(a.Pointer, out var p) ? p : 0f);
			var notes = new List<string>();
			foreach (var cc in Costs(copy))
			{
				var w = cc.Weapon;
				if (w == null)
					continue;
				if (Avail(w) < w.CountForHP(cc.Hp))
				{
					var sub = Substitute(cc, a => Avail(a));
					if (sub == null)
						return false;
					cc.Company.Weapon = sub;
					notes.Add($"{sub.Name} for {w.Name}");
					w = sub;
				}
				pending[w.Pointer] = (pending.TryGetValue(w.Pointer, out var p) ? p : 0f) + w.CountForHP(cc.Hp);
			}
			if (notes.Count > 0)
				swapped = string.Join(", ", notes);
			return true;
		}
	}
}
