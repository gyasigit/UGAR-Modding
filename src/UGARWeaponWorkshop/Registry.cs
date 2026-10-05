// Turns designs into real game assets and registers them where the game looks for weapons.
//
// How the game handles weapons:
// - A weapon is a WeaponTemplate (PathAsset). Saves store it by assetPath: the company weapon
//   (FightUnitModelInitData.Write/LoadCurrent -> Resources.Load<WeaponTemplate>), storage CountItems, production
//   orders and the research list (PathAssetExt -> PathAsset.LoadAsset -> Resources.Load). We answer Resources.Load for
//   "UGARMods/Weapons/<code>" and "UGARMods/WeaponInventions/<code>[|<state>]", building the asset from the code.
//   Because the code holds the whole design, a weapon can always be rebuilt while this plugin is installed, even after
//   its design was deleted from the workshop.
// - Which units may carry it: UnitEntity.availableWeapons (ArmyCreation.WhoCanUse, WeaponSlot blocks the rest).
//   We add a design to every unit type that carries its base weapon (plus cavalry for short barrels and the rifle
//   units for rifled smoothbores).
// - The master list Config.game.weapons.firearms and its min/max data (stat bars).
// - Production only offers weapons with an ItemInvention in the country's researchProjectManager.inventions, so each
//   design gets an ItemInvention, added to the player's list (saved by path like everything else). The invention's
//   path also carries the design's workshop state: "|a" archived, "n=<name>" renamed. Archived designs stay in the
//   list (the state must be saved) but are hidden from the New Order list (Patches.ItemListUpdatePrefix).
// - Names: the weapon's LocalizedString terms are "UGARWW/<id>/Name|Description"; we answer I2 for them.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using World;
using World.SceneObject;

namespace UGARWeaponWorkshop
{
	internal sealed class Entry
	{
		public Design D;
		public string Code;
		public WeaponTemplate Weapon;
		public bool Placeholder;
		public bool Listed; // in WeaponConfig.firearms
		public readonly List<string> CarrySources = new List<string>();
		public string Description;
		/// <summary>Name set by Rename (stored in the research entry's path); null = the design's own name.</summary>
		public string NameOverride;
		public string DisplayName => string.IsNullOrEmpty(NameOverride) ? D.Name : NameOverride;
		/// <summary>Research entries by state ("" = active, original name).</summary>
		public readonly Dictionary<string, ItemInvention> Inventions = new Dictionary<string, ItemInvention>(StringComparer.Ordinal);
	}

	/// <summary>One research entry of a design: which design, archived or not, and the name it shows.</summary>
	internal sealed class InventionState
	{
		public Entry E;
		public ItemInvention Asset;
		public bool Archived;
		public string Name;
	}

	/// <summary>A design as the player has it: its entry and research list record.</summary>
	internal sealed class PlayerDesign
	{
		public Entry E;
		public InventionState S;
		public int Index; // in researchProjectManager.inventions
		public bool Archived => S.Archived;
	}

	/// <summary>What still uses a design (Delete is only offered when nothing does).</summary>
	internal sealed class Usage
	{
		public float Stock, Market;
		public int Orders, Companies, Regiments;
		public bool InUse => Stock >= 0.5f || Market >= 0.5f || Orders > 0 || Companies > 0;

		public string Text()
		{
			var parts = new List<string>();
			if (Companies > 0) parts.Add($"carried by {Companies} compan{(Companies == 1 ? "y" : "ies")} in {Regiments} regiment{(Regiments == 1 ? "" : "s")}");
			if (Stock >= 0.5f) parts.Add($"{Stock:0} in storage");
			if (Market >= 0.5f) parts.Add($"{Market:0} at the market");
			if (Orders > 0) parts.Add($"{Orders} factory order{(Orders == 1 ? "" : "s")}");
			return parts.Count == 0 ? "not used anywhere" : string.Join(", ", parts);
		}
	}

	internal static class Registry
	{
		public const string WeaponPrefix = "UGARMods/Weapons/";
		public const string InventionPrefix = "UGARMods/WeaponInventions/";
		public const string TermPrefix = "UGARWW/";
		const string VanillaFolder = "World/LandWeapons/";
		const string Fallback = "Musket__Trade";

		static readonly Dictionary<string, Entry> ByCode = new Dictionary<string, Entry>(StringComparer.Ordinal);
		static readonly Dictionary<string, Entry> ById = new Dictionary<string, Entry>(StringComparer.Ordinal);
		static readonly Dictionary<IntPtr, Entry> ByWeapon = new Dictionary<IntPtr, Entry>();
		static readonly Dictionary<IntPtr, InventionState> ByInvention = new Dictionary<IntPtr, InventionState>();
		static readonly Dictionary<string, string> Terms = new Dictionary<string, string>(StringComparer.Ordinal);
		static readonly HashSet<(IntPtr, string)> CarryDone = new HashSet<(IntPtr, string)>();
		static float _nextCarryScan;
		static int _lastEntityCount = -1;

		public static IEnumerable<Entry> Entries => ById.Values;
		public static Entry FindWeapon(IntPtr asset) => asset != IntPtr.Zero && ByWeapon.TryGetValue(asset, out var e) ? e : null;
		public static InventionState FindInvention(IntPtr asset) => asset != IntPtr.Zero && ByInvention.TryGetValue(asset, out var s) ? s : null;
		public static bool IsOurs(IntPtr asset) => FindWeapon(asset) != null || FindInvention(asset) != null;
		public static bool TryTerm(string term, out string text) => Terms.TryGetValue(term, out text);

		// ---------------- Vanilla lookups ----------------

		public static WeaponTemplate Vanilla(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;
			var o = Resources.Load(VanillaFolder + name, Il2CppType.Of<WeaponTemplate>());
			return o == null ? null : o.TryCast<WeaponTemplate>();
		}

		/// <summary>Vanilla small arms a design can start from (muskets, carbines, rifles), in the game's list order.</summary>
		public static List<WeaponTemplate> VanillaSmallArms()
		{
			var res = new List<WeaponTemplate>();
			var list = Config.game?.weapons?.firearms;
			for (int i = 0; list != null && i < list.Count; i++)
			{
				var w = list[i];
				if (w == null || IsOurs(w.Pointer) || w.inventoryType != EInventoryType.Musket)
					continue;
				string n = w.name;
				if (n.StartsWith("Musket", StringComparison.Ordinal) || n.StartsWith("Rifle", StringComparison.Ordinal) || n.StartsWith("Carbine", StringComparison.Ordinal))
					res.Add(w);
			}
			return res;
		}

		// ---------------- Creating assets ----------------

		/// <summary>The entry for a path code (from a save or a new design). Never null: unreadable codes give a placeholder.</summary>
		public static Entry GetOrCreate(string code) => GetOrCreate(code, null);

		/// <param name="existing">A weapon object that already exists for this code (made before a live reload of this
		/// plugin): adopt it instead of building a second copy, so storage, orders and companies keep pointing at it.</param>
		static Entry GetOrCreate(string code, WeaponTemplate existing)
		{
			if (ByCode.TryGetValue(code, out var e))
			{
				if (existing != null && !ByWeapon.ContainsKey(existing.Pointer))
					ByWeapon[existing.Pointer] = e;
				return e;
			}
			var d = Design.Decode(code, out var error);
			bool placeholder = false;
			WeaponTemplate bw = d == null ? null : Vanilla(d.Base);
			if (bw == null)
			{
				Plugin.Logger.LogWarning($"Weapon design \"{code}\" can't be rebuilt ({error ?? "unknown base weapon " + d?.Base}); loading it as a plain trade musket.");
				placeholder = true;
				bw = Vanilla(Fallback);
				d = new Design { Id = "unknown-" + Hash(code).Substring(0, 8), Base = Fallback, Name = "Unknown workshop weapon", Final = Stats.Of(bw) };
			}
			// The path doesn't store what the parts never change; take it from the base.
			d.Final.Damage = bw.damage;
			d.Final.Wood = Stats.Of(bw).Wood;
			if (ById.TryGetValue(d.Id, out var same))
			{
				ByCode[code] = same; // same design under a slightly different code (shouldn't happen)
				return same;
			}
			e = new Entry { D = d, Code = code, Placeholder = placeholder };
			if (existing != null && !placeholder)
			{
				e.Weapon = existing;
				SetTexts(e);
			}
			else
				Build(e, bw);
			ByCode[code] = e;
			ById[d.Id] = e;
			ByWeapon[e.Weapon.Pointer] = e;
			Register(e);
			Plugin.Logger.LogInfo($"Weapon design {d.Name} ({d.Id}){(existing != null ? " adopted from memory" : "")}: {Describe(e)}");
			return e;
		}

		/// <summary>
		/// Picks up designs that already exist in the game's memory but not in this registry. That happens after the mod
		/// manager live-reloads this plugin: the old copy's registry is gone, but its weapon and research objects are still
		/// in the master weapon list, the player's research list, storage and orders. Without this, their names and
		/// descriptions came back empty (1.1.3). Cheap; called every couple of seconds while a campaign is open.
		/// </summary>
		public static void AdoptExisting(Country c)
		{
			try
			{
				var list = Config.game?.weapons?.firearms;
				for (int i = 0; list != null && i < list.Count; i++)
				{
					var w = list[i];
					string path = w?.assetPath;
					if (path != null && path.StartsWith(WeaponPrefix, StringComparison.Ordinal) && FindWeapon(w.Pointer) == null)
						GetOrCreate(path.Substring(WeaponPrefix.Length), w);
				}
				var inv = c?.researchProjectManager?.inventions;
				for (int i = 0; inv != null && i < inv.Count; i++)
				{
					var it = inv[i];
					string path = it?.assetPath;
					if (path == null || !path.StartsWith(InventionPrefix, StringComparison.Ordinal) || FindInvention(it.Pointer) != null)
						continue;
					var item = it.TryCast<ItemInvention>();
					if (item == null)
						continue;
					ParseInventionPath(path.Substring(InventionPrefix.Length), out var code, out var archived, out var name);
					var weapon = item.item?.TryCast<WeaponTemplate>();
					var e = GetOrCreate(code, weapon);
					ApplyName(e, name);
					string state = StateKey(e, archived, ref name);
					e.Inventions[state] = item;
					ByInvention[item.Pointer] = new InventionState { E = e, Asset = item, Archived = archived, Name = name };
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Picking up existing weapon designs failed: {ex.Message}");
			}
		}

		/// <summary>Name and description: our I2 terms on the weapon, and the texts the name/description patches return.</summary>
		static void SetTexts(Entry e)
		{
			var d = e.D;
			var w = e.Weapon;
			e.Description = DescriptionText(e);
			string nameTerm = TermPrefix + d.Id + "/Name", descTerm = TermPrefix + d.Id + "/Description";
			Terms[nameTerm] = e.DisplayName;
			Terms[descTerm] = e.Description;
			var ln = w.textName; ln.mTerm = nameTerm; w.textName = ln;
			var ld = w.textDescription; ld.mTerm = descTerm; w.textDescription = ld;
			if (w.textName.mTerm != nameTerm)
				Plugin.Logger.LogWarning($"{d.Name}: the weapon's name term didn't stick ({w.textName.mTerm}); the name getters still show the design name.");
		}

		public static Entry Create(Design d) => GetOrCreate(d.Encode());

		static void Build(Entry e, WeaponTemplate bw)
		{
			var d = e.D;
			var w = UnityEngine.Object.Instantiate(bw.Cast<UnityEngine.Object>()).Cast<WeaponTemplate>();
			w.name = "UGARWW_" + d.Id;
			w.hideFlags = HideFlags.DontUnloadUnusedAsset;
			w.assetPath = WeaponPrefix + e.Code;
			w.guid = Hash(e.Code);

			var s = d.Final;
			w.effectiveRange = s.Range;
			w.effectiveRangeHint = s.Range;
			w.baseReload = s.Reload;
			w.meleeDamage = s.Melee;
			w.randLow = s.Low;
			w.randHi = s.High;
			w.price = Mathf.RoundToInt(s.Price);
			w.productionCost = s.Work;
			w.productionGoldCost = s.Gold;
			w.components = Components(bw, s);
			w.damageDegradation = Curve(d, bw);
			e.Weapon = w;
			SetTexts(e);
		}

		/// <summary>The research entry of a design in a given workshop state (created once per state).</summary>
		public static ItemInvention InventionFor(Entry e, bool archived, string name)
		{
			string state = StateKey(e, archived, ref name);
			if (e.Inventions.TryGetValue(state, out var inv))
				return inv;
			inv = ScriptableObject.CreateInstance(Il2CppType.Of<ItemInvention>()).Cast<ItemInvention>();
			inv.name = "UGARWW_Invention_" + e.D.Id + (state.Length > 0 ? "_" + Hash(state).Substring(0, 6) : "");
			inv.hideFlags = HideFlags.DontUnloadUnusedAsset;
			inv.assetPath = InventionPrefix + e.Code + (state.Length > 0 ? "|" + state : "");
			inv.guid = Hash("inv:" + inv.assetPath);
			inv.header = string.IsNullOrEmpty(name) ? e.D.Name : name;
			inv.description = e.Description;
			inv.item = e.Weapon;
			e.Inventions[state] = inv;
			ByInvention[inv.Pointer] = new InventionState { E = e, Asset = inv, Archived = archived, Name = name };
			return inv;
		}

		/// <summary>Resolves "UGARMods/WeaponInventions/&lt;code&gt;[|&lt;state&gt;]" (from a save or our own code).</summary>
		public static ItemInvention InventionFromPath(string rest)
		{
			ParseInventionPath(rest, out var code, out var archived, out var name);
			var e = GetOrCreate(code);
			ApplyName(e, name);
			return InventionFor(e, archived, name);
		}

		static void ParseInventionPath(string rest, out string code, out bool archived, out string name)
		{
			code = rest;
			string state = "";
			int bar = rest.IndexOf('|');
			if (bar >= 0)
			{
				code = rest.Substring(0, bar);
				state = rest.Substring(bar + 1);
			}
			archived = state.StartsWith("a", StringComparison.Ordinal);
			name = null;
			int n = state.IndexOf("n=", StringComparison.Ordinal);
			if (n >= 0)
			{
				try { name = Uri.UnescapeDataString(state.Substring(n + 2)); } catch (Exception) { }
			}
		}

		static string StateKey(Entry e, bool archived, ref string name)
		{
			if (name == e.D.Name) name = null;
			return (archived ? "a" : "") + (string.IsNullOrEmpty(name) ? "" : ";n=" + Uri.EscapeDataString(name));
		}

		static void ApplyName(Entry e, string name)
		{
			e.NameOverride = name == e.D.Name ? null : name;
			Terms[TermPrefix + e.D.Id + "/Name"] = e.DisplayName;
		}

		static Il2CppReferenceArray<CountItem> Components(WeaponTemplate bw, Stats s)
		{
			var src = bw.components;
			var list = new List<CountItem>();
			for (int i = 0; src != null && i < src.Length; i++)
			{
				var c = src[i];
				if (c?.asset == null) continue;
				float n = c.count;
				if (c.asset.name == "Iron") n = s.Iron;
				list.Add(new CountItem(c.asset, n));
			}
			return new Il2CppReferenceArray<CountItem>(list.ToArray());
		}

		/// <summary>Range curve: the base's (or, for a rifled smoothbore, the donor rifle's), with the far part scaled by the barrel.</summary>
		static AnimationCurve Curve(Design d, WeaponTemplate bw)
		{
			var src = bw.damageDegradation;
			if (d.AddsRifling)
			{
				var donor = Vanilla(d.Donor);
				if (donor?.damageDegradation != null)
					src = donor.damageDegradation;
			}
			if (src == null)
				return null;
			float far = d.Barrel == 'S' ? Parts.ShortFar : d.Barrel == 'L' ? Parts.LongFar : 1f;
			var keys = src.keys;
			for (int i = 0; i < keys.Length; i++)
			{
				var k = keys[i];
				if (k.time >= Parts.FarFrom)
				{
					k.value *= far;
					k.inTangent *= far;
					k.outTangent *= far;
				}
				keys[i] = k;
			}
			var curve = new AnimationCurve(keys);
			curve.preWrapMode = src.preWrapMode;
			curve.postWrapMode = src.postWrapMode;
			return curve;
		}

		static string Hash(string s)
		{
			using var md5 = MD5.Create();
			var b = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
			var sb = new StringBuilder();
			foreach (var x in b) sb.Append(x.ToString("x2"));
			return sb.ToString();
		}

		static string DescriptionText(Entry e)
		{
			var d = e.D;
			if (e.Placeholder)
				return "A weapon from the Weapon Workshop that could not be rebuilt. It behaves like a trade musket.";
			var bw = Vanilla(d.Base);
			string baseName = bw != null ? bw.Name : d.Base;
			var s = d.Final;
			return $"A workshop design based on the {baseName} ({Parts.CategoryName(d.Category)}): {d.PartsText()}. " +
				$"Range {s.Range:0}, reload {s.Reload:0.#}, accuracy {s.Low:0.##}-{s.High:0.##}, melee {s.Melee * 100f:0.#}.";
		}

		public static string Describe(Entry e)
		{
			var s = e.D.Final;
			return $"base {e.D.Base}, parts {e.D.PartsCode}, range {s.Range:0}, reload {s.Reload:0.#}, accuracy {s.Low:0.##}-{s.High:0.##}, " +
				$"melee {s.Melee:0.###}, efficiency {s.Efficiency * 10f:0.#}, price {s.Price:0}, work {s.Work:0.###}, gold {s.Gold:0.##}, iron {s.Iron:0.####}" +
				(e.Placeholder ? " [placeholder]" : "");
		}

		// ---------------- Registering ----------------

		static void Register(Entry e)
		{
			var d = e.D;
			e.CarrySources.Add(d.Base);
			if (d.Barrel == 'S')
			{
				e.CarrySources.Add("Carbine56_Dragoon");
				e.CarrySources.Add("Musket_BrownBess_Short_1769");
			}
			if (d.AddsRifling && !string.IsNullOrEmpty(d.Donor))
				e.CarrySources.Add(d.Donor);

			try
			{
				var cfg = Config.game?.weapons;
				if (cfg?.firearms != null && !cfg.firearms.Contains(e.Weapon))
				{
					cfg.firearms.Add(e.Weapon);
					cfg.firearmsData?.AddWeapon(e.Weapon); // null = built lazily from the list, which now includes it
					e.Listed = true;
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"{d.Name}: not added to the master weapon list: {ex.Message}");
			}
			_nextCarryScan = 0f;
			UpdateCarryLists(force: true);
		}

		/// <summary>Adds designs to the carry lists of every loaded unit type that carries one of their source weapons.
		/// Unit prefabs can load later, so this is repeated (throttled) whenever a weapon picker opens.</summary>
		public static void UpdateCarryLists(bool force = false)
		{
			if (ById.Count == 0)
				return;
			if (!force && Time.realtimeSinceStartup < _nextCarryScan)
				return;
			_nextCarryScan = Time.realtimeSinceStartup + 5f;
			try
			{
				var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<UnitEntity>());
				if (!force && all.Length == _lastEntityCount)
					return;
				_lastEntityCount = all.Length;
				int added = 0;
				foreach (var o in all)
				{
					var ue = o?.TryCast<UnitEntity>();
					var arr = ue?.availableWeapons;
					if (arr == null || arr.Length == 0)
						continue;
					var names = new HashSet<string>();
					foreach (var w in arr)
						if (w != null) names.Add(w.name);
					List<WeaponTemplate> extra = null;
					foreach (var e in ById.Values)
					{
						if (!CarryDone.Add((ue.Pointer, e.D.Id)))
							continue;
						if (names.Contains(e.Weapon.name))
							continue;
						bool carries = false;
						foreach (var src in e.CarrySources)
							if (names.Contains(src)) { carries = true; break; }
						if (carries)
							(extra ??= new List<WeaponTemplate>()).Add(e.Weapon);
					}
					if (extra == null)
						continue;
					var res = new WeaponTemplate[arr.Length + extra.Count];
					for (int i = 0; i < arr.Length; i++) res[i] = arr[i];
					for (int i = 0; i < extra.Count; i++) res[arr.Length + i] = extra[i];
					ue.availableWeapons = new Il2CppReferenceArray<WeaponTemplate>(res);
					added += extra.Count;
				}
				if (added > 0)
					Plugin.Logger.LogInfo($"Weapon designs added to {added} unit type carry list(s) ({all.Length} unit types loaded).");
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Updating unit carry lists failed: {ex.Message}");
			}
		}

		// ---------------- Player ----------------

		public static Country PlayerCountry()
		{
			try
			{
				return MonoBehaviourSingleton<SceneManager>.instance?.PlayerCountry;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>The player's designs (those in its research list), oldest first.</summary>
		public static List<PlayerDesign> PlayerDesigns(Country c)
		{
			var res = new List<PlayerDesign>();
			var inv = c?.researchProjectManager?.inventions;
			var seen = new HashSet<Entry>();
			for (int i = 0; inv != null && i < inv.Count; i++)
			{
				var it = inv[i];
				var s = it == null ? null : FindInvention(it.Pointer);
				if (s == null || s.E.Placeholder || !seen.Add(s.E))
					continue;
				ApplyName(s.E, s.Name); // the list in this save decides the name
				res.Add(new PlayerDesign { E = s.E, S = s, Index = i });
			}
			res.Sort((a, b) => string.CompareOrdinal(a.E.D.Id, b.E.D.Id));
			return res;
		}

		static float StorageCount(ItemStorage st, WeaponTemplate w)
		{
			try { return st?.GetItemCount(w) ?? 0f; } catch (Exception) { return 0f; }
		}

		public static float Stock(Country c, WeaponTemplate w)
		{
			if (c == null) return 0f;
			float n = StorageCount(c.inventory?.itemStorage, w);
			try { n += StorageCount(c.europeanManager?.inventory?.itemStorage, w); } catch (Exception) { }
			return n;
		}

		/// <summary>Vanilla small arms the player can build on: researched (has the invention) or held in storage.</summary>
		public static List<WeaponTemplate> UnlockedBases(Country c)
		{
			var res = new List<WeaponTemplate>();
			var rpm = c?.researchProjectManager;
			foreach (var w in VanillaSmallArms())
			{
				bool ok = false;
				try { ok = rpm != null && rpm.ContainsItemInvention(w); } catch (Exception) { }
				if (!ok && Stock(c, w) >= 1f)
					ok = true;
				if (ok)
					res.Add(w);
			}
			return res;
		}

		/// <summary>Adds a new design to the player's research list so factories can make it.</summary>
		public static void Unlock(Entry e, Country c)
		{
			var list = c?.researchProjectManager?.inventions;
			if (list == null)
				throw new InvalidOperationException("the player's research list isn't available");
			var inv = InventionFor(e, false, null);
			if (list.Contains(inv))
				return;
			try
			{
				inv.Apply(c); // adds it and fires OnInvented like a finished research project
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"ItemInvention.Apply failed ({ex.Message}); adding {e.D.Name} to the research list directly.");
				if (!list.Contains(inv))
					list.Add(inv);
			}
		}

		/// <summary>Replaces the design's research entry with the one for a new state (archive/restore/rename).</summary>
		public static void SetState(Country c, PlayerDesign p, bool archived, string name)
		{
			var list = c.researchProjectManager.inventions;
			var inv = InventionFor(p.E, archived, name);
			int i = p.Index;
			if (i < 0 || i >= list.Count || list[i] == null || FindInvention(list[i].Pointer)?.E != p.E)
				i = IndexOf(list, p.E);
			if (i < 0)
				throw new InvalidOperationException($"{p.E.DisplayName} is not in the research list any more");
			list[i] = inv;
			ApplyName(p.E, ByInvention[inv.Pointer].Name);
			NotifyResearchChanged(c);
		}

		/// <summary>Removes the design from the research list: gone from the workshop. Guns that still exist keep working.</summary>
		public static void Delete(Country c, PlayerDesign p)
		{
			var list = c.researchProjectManager.inventions;
			for (int i = list.Count - 1; i >= 0; i--)
			{
				var s = list[i] == null ? null : FindInvention(list[i].Pointer);
				if (s != null && s.E == p.E)
					list.RemoveAt(i);
			}
			NotifyResearchChanged(c);
		}

		static int IndexOf(Il2CppSystem.Collections.Generic.List<Invention> list, Entry e)
		{
			for (int i = 0; i < list.Count; i++)
				if (list[i] != null && FindInvention(list[i].Pointer)?.E == e)
					return i;
			return -1;
		}

		static void NotifyResearchChanged(Country c)
		{
			try { c.researchProjectManager.OnInvented?.Invoke(); } catch (Exception) { }
		}

		static IEnumerable<ProductionManager> ProductionManagers(Country c)
		{
			ProductionManager a = null, b = null;
			try { a = c.GetProductionManager(false); } catch (Exception) { }
			try { b = c.GetProductionManager(true); } catch (Exception) { }
			if (a != null) yield return a;
			if (b != null && (a == null || b.Pointer != a.Pointer)) yield return b;
		}

		/// <summary>Removes all factory orders for the design (no refund needed: orders pay per finished gun).</summary>
		public static int CancelOrders(Country c, Entry e)
		{
			int n = 0;
			foreach (var pm in ProductionManagers(c))
			{
				var orders = pm.productionOrderList;
				for (int i = orders == null ? -1 : orders.Count - 1; i >= 0; i--)
				{
					var a = orders[i]?.asset;
					if (a != null && a.Pointer == e.Weapon.Pointer)
					{
						pm.RemoveOrder(i);
						n++;
					}
				}
			}
			return n;
		}

		public static Usage UsageOf(Country c, Entry e)
		{
			var u = new Usage { Stock = Stock(c, e.Weapon) };
			try { u.Market = StorageCount(c.inventory?.marketManager?.storage, e.Weapon); } catch (Exception) { }
			foreach (var pm in ProductionManagers(c))
			{
				var orders = pm.productionOrderList;
				for (int i = 0; orders != null && i < orders.Count; i++)
					if (orders[i]?.asset != null && orders[i].asset.Pointer == e.Weapon.Pointer)
						u.Orders++;
			}
			try
			{
				var am = c.armyManager;
				var units = am?.units;
				for (int i = 0; units != null && i < units.Count; i++)
					CountUnit(units[i], e, u);
				var garrisons = am?.garrisons;
				for (int i = 0; garrisons != null && i < garrisons.Count; i++)
				{
					var g = garrisons[i]?.Units;
					for (int j = 0; g != null && j < g.Count; j++)
						CountRegiment(g[j]?.TryCast<RegimentInitData>()?.companies, e, u);
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Counting units armed with {e.DisplayName} failed: {ex.Message}");
			}
			return u;
		}

		static void CountUnit(WorldUnitModel m, Entry e, Usage u)
		{
			if (m == null) return;
			var r = m.TryCast<RegimentModel>();
			if (r != null) { CountRegiment(r.companies, e, u); return; }
			var b = m.TryCast<WorldBrigadeModel>();
			var regs = b?.regiments;
			for (int i = 0; regs != null && i < regs.Count; i++)
				CountRegiment(regs[i]?.companies, e, u);
		}

		static void CountRegiment(Il2CppReferenceArray<Fight.BrigadeModelInitData> companies, Entry e, Usage u)
		{
			if (companies == null) return;
			int n = 0;
			foreach (var c in companies)
			{
				var w = c?.Weapon;
				if (w != null && w.Pointer == e.Weapon.Pointer) n++;
			}
			if (n > 0) { u.Companies += n; u.Regiments++; }
		}
	}
}
