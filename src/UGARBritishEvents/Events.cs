// Checks the event triggers each in-game day, shows fired events in the game's own event window (Choice.cs for events
// with a price: Pay / Pass) and keeps the
// cap bonuses of the events that fired in this campaign.
//
// Event window: World.Event.GlobalEvent.Execute(source) → ShowEventWindow builds a Report
// from the GlobalEventSettings (image, Header, Content, button, effects) and queues it in the
// World.UI.Window.GlobalEvent singleton window (Open). Header/Content fall back to the plain header/content strings
// when the localized term is empty, and Open skips a report only when it has effects and none of them is shown, so a
// runtime-made GlobalEventSettings with no effects is displayed like any parliament event. The game fires its own
// parliament events the same way from Country.CheckParliamentEvents (NationSettings.parliamentEvents, source 9).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using World;
using World.Effect;
using World.Event;
using World.Modifiers;
using World.SceneObject;

namespace UGARBritishEvents
{
	internal struct CapBonus
	{
		public int Factories;
		public int Shipyards;
	}

	internal static class Events
	{
		static List<EventDefinition> _defs = new List<EventDefinition>();
		static readonly System.Random _rng = new System.Random();
		static readonly List<UnityEngine.Object> _keepAlive = new List<UnityEngine.Object>();
		static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

		static IntPtr _country;
		static DateTime _lastDay;
		static int _bonusStaticCount = -2;
		static IntPtr _bonusCountry;
		static CapBonus _bonus;
		static bool _infoWritten;

		public static int DefinitionCount => _defs.Count;

		public static void SetDefinitions(List<EventDefinition> defs) => _defs = defs;

		/// <summary>Recompute the cap bonus on next use (a Repeatable setting changed how paid repeats count).</summary>
		public static void InvalidateBonus() => _bonusStaticCount = -2;

		public static Country PlayerCountry
		{
			get
			{
				var sm = MonoBehaviourSingleton<SceneManager>.instance;
				return sm == null ? null : sm.PlayerCountry;
			}
		}

		static DateTime Today
		{
			get
			{
				var d = RuntimeVars.date;
				return new DateTime(d.Year, d.Month, d.Day);
			}
		}

		/// <summary>Cap bonus from the events that fired in the loaded campaign (0 when the mod is off).</summary>
		public static CapBonus Bonus
		{
			get
			{
				if (!Plugin.Enabled.Value)
					return default;
				var country = PlayerCountry;
				if (country == null)
					return default;
				int count = SaveMarkers.StaticCount(country);
				if (country.Pointer != _bonusCountry || count != _bonusStaticCount)
				{
					_bonusCountry = country.Pointer;
					_bonusStaticCount = count;
					_bonus = Compute(country);
				}
				return _bonus;
			}
		}

		static CapBonus Compute(Country country)
		{
			var paid = SaveMarkers.PaidCounts(country);
			var b = new CapBonus();
			foreach (var d in _defs)
			{
				if (!paid.TryGetValue(d.Code, out int times) || !AppliesTo(d, country)) continue;
				if (!d.IsRepeatable) times = 1;   // a forced repeat of a one-off event doesn't stack its caps
				b.Factories += d.MaxFactories * times;
				b.Shipyards += d.MaxShipyards * times;
			}
			return b;
		}

		static bool AppliesTo(EventDefinition d, Country country)
		{
			foreach (var n in d.Nations)
				if (Enum.TryParse<ENation>(n, true, out var nation) && nation == country.Nation)
					return true;
			return false;
		}

		/// <summary>Called every frame by the Starter.</summary>
		public static void Tick()
		{
			if (!Plugin.Enabled.Value)
				return;
			var country = PlayerCountry;
			if (country == null || country.modifiersManager == null)
			{
				_country = IntPtr.Zero;
				return;
			}
			DateTime today;
			try { today = Today; }
			catch { return; }

			if (country.Pointer != _country)
			{
				// A campaign was loaded (or the campaign scene came back from a battle): no events on this tick.
				_country = country.Pointer;
				_lastDay = today;
				RentCaps.Reset();
				OnCampaignLoaded(country);
				return;
			}
			if (today <= _lastDay)
			{
				_lastDay = today < _lastDay ? today : _lastDay;
				return;
			}
			int days = (int)(today - _lastDay).TotalDays;
			_lastDay = today;
			if (days > 60)
				return;     // a jump, not time passing

			var lastFired = SaveMarkers.LastFired(country);
			var forced = Plugin.ForceEvent.Value?.Trim();
			if (!string.IsNullOrEmpty(forced))
			{
				Plugin.ForceEvent.Value = "";
				var def = _defs.Find(x => string.Equals(x.Id, forced, StringComparison.OrdinalIgnoreCase));
				if (def == null)
					Plugin.Logger.LogWarning($"British events: Debug.ForceEvent \"{forced}\" is not a loaded event id.");
				else
				{
					// Testing: fire even if it already fired in this campaign.
					if (lastFired.ContainsKey(def.Code))
						Plugin.Logger.LogInfo($"British events: \"{def.Id}\" already fired in this campaign; firing again (Debug.ForceEvent).");
					Fire(country, def, today);
					return;
				}
			}

			Context ctx = null;
			HashSet<int> paid = null;
			foreach (var d in _defs)
			{
				if (!AppliesTo(d, country))
					continue;
				if (lastFired.TryGetValue(d.Code, out var last) && (!d.IsRepeatable || (today - last).TotalDays < d.RepeatDays))
					continue;
				ctx ??= new Context(country);
				paid ??= SaveMarkers.PaidCodes(country);
				if (!Check(d, ctx, paid, today, out var why))
				{
					if (Plugin.LogChecks.Value)
						Plugin.Logger.LogInfo($"British events {today:yyyy-MM-dd}: \"{d.Id}\" not yet ({why}).");
					continue;
				}
				// chancePerWeek, spread over the days that passed.
				double p = d.ChancePerWeek >= 1f ? 1 : 1 - Math.Pow(1 - d.ChancePerWeek, days / 7.0);
				if (_rng.NextDouble() >= p)
				{
					if (Plugin.LogChecks.Value)
						Plugin.Logger.LogInfo($"British events {today:yyyy-MM-dd}: \"{d.Id}\" ready, chance roll failed (p={p:0.###}).");
					continue;
				}
				Fire(country, d, today);
				return;     // at most one event per day
			}
		}

		sealed class Context
		{
			public readonly HashSet<string> Owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public readonly List<string> OwnedNames = new List<string>();
			public int Settlements;
			public int RentedFactories;
			public int RentedShipyards;
			public int HomeGoodsKinds;      // trade goods with 2+ units in the home storage

			public Context(Country country)
			{
				var nation = country.Nation;
				var remote = country.colonialStorageManager?.remoteRegion;
				foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
				{
					if (l == null || l.ownerNation != nation) continue;
					if (remote != null && l.region != null && l.region.Pointer == remote.Pointer) continue;
					Settlements++;
					string name = Safe(() => l.Name);
					if (!string.IsNullOrEmpty(name)) { Owned.Add(name); OwnedNames.Add(name); }
					string town = Safe(() => l.townName), fort = Safe(() => l.fortName);
					if (!string.IsNullOrEmpty(town)) Owned.Add(town);
					if (!string.IsNullOrEmpty(fort)) Owned.Add(fort);
				}
				var em = country.europeanManager;
				if (em != null)
				{
					RentedFactories = em.rentedFactories;
					RentedShipyards = em.rentedShipyards;
				}
				try { HomeGoodsKinds = Home.Goods(country, 2f).Count; } catch { }
			}

			static string Safe(Func<string> f)
			{
				try { return f()?.Trim(); } catch { return null; }
			}
		}

		static bool Check(EventDefinition d, Context c, HashSet<int> paid, DateTime today, out string why)
		{
			why = null;
			if (d.From.HasValue && today < d.From.Value) { why = $"before {d.From:yyyy-MM-dd}"; return false; }
			if (d.Until.HasValue && today > d.Until.Value) { why = $"after {d.Until:yyyy-MM-dd}"; return false; }
			foreach (var r in d.Requires)
				if (!paid.Contains(EventDefinition.CodeOf(r))) { why = $"needs event {r} (paid)"; return false; }
			if (c.Settlements < d.MinSettlements) { why = $"{c.Settlements}/{d.MinSettlements} settlements"; return false; }
			foreach (var n in d.OwnsAll)
				if (!c.Owned.Contains(n)) { why = $"doesn't own {n}"; return false; }
			if (d.OwnsAny.Count > 0)
			{
				bool any = false;
				foreach (var n in d.OwnsAny)
					if (c.Owned.Contains(n)) { any = true; break; }
				if (!any) { why = $"owns none of {string.Join(", ", d.OwnsAny)}"; return false; }
			}
			if (c.RentedFactories < d.MinRentedFactories) { why = $"{c.RentedFactories}/{d.MinRentedFactories} rented factories"; return false; }
			if (c.RentedShipyards < d.MinRentedShipyards) { why = $"{c.RentedShipyards}/{d.MinRentedShipyards} rented shipyards"; return false; }
			if (d.HomeStockKinds > 0 && c.HomeGoodsKinds == 0) { why = "no trade goods in the home storage"; return false; }
			return true;
		}

		// ---- Firing ----

		static void Fire(Country country, EventDefinition d, DateTime today)
		{
			SaveMarkers.MarkFired(country, d, today);
			var cost = d.HasCost ? Cost.Roll(d, country) : null;
			if (cost == null || cost.IsFree)
			{
				Grant(country, d, "no cost");
				try { Show(country, d); }
				catch (Exception e) { Plugin.Logger.LogError($"British events: could not show \"{d.Id}\" (its effects still apply): {e}"); }
				return;
			}
			Plugin.Logger.LogInfo($"British events {today:yyyy-MM-dd}: fired \"{d.Id}\", asking {cost.Describe()}.");
			try
			{
				Choice.Show(country, d, cost, EffectText(d, country), ImageFor(d));
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"British events: could not show \"{d.Id}\"; it counts as passed: {e}");
			}
		}

		/// <summary>Applies an event's effects (paid, or it had no cost).</summary>
		public static void Grant(Country country, EventDefinition d, string reason)
		{
			int baseCap = RentCaps.BaseCap();
			var old = Bonus;
			SaveMarkers.MarkPaid(country, d);
			ApplyModifiers(country, d);
			var b = Bonus;
			Plugin.Logger.LogInfo($"British events: \"{d.Id}\" applied ({reason}). Factory cap {baseCap}+{b.Factories} (was +{old.Factories}), " +
				$"shipyard cap {baseCap}+{b.Shipyards} (was +{old.Shipyards}).");
			if (d.HomePopulationPercent != 0f || d.HomePopulation != 0)
			{
				var g = Home.Apply(country, d);
				Plugin.Logger.LogInfo($"British events: \"{d.Id}\" added {g.Population} population ({g.Workforce} workforce) to {g.Settlements} home settlement(s).");
			}
		}
		static void ApplyModifiers(Country country, EventDefinition d)
		{
			if (d.Modifiers.Count == 0)
				return;
			var mm = country.modifiersManager;
			foreach (var m in d.Modifiers)
			{
				if (!Enum.TryParse<EModifier>(m.Modifier, true, out var mod))
				{
					Plugin.Logger.LogWarning($"British events: \"{d.Id}\" has unknown modifier {m.Modifier}.");
					continue;
				}
				var data = new ModifierParameterData(mod, m.Value, 0, country.Nation, true) { percent = m.Percent };
				mm.staticList.Add(data);
			}
			mm.Calculate();
		}

		static void Show(Country country, EventDefinition d)
		{
			var s = ScriptableObject.CreateInstance<GlobalEventSettings>();
			s.name = "UGARMods/Events/" + d.Id;
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_keepAlive.Add(s);
			s.header = d.Title;
			s.content = d.Text + EffectText(d, country);
			s.button = string.IsNullOrEmpty(d.Button) ? "Very well" : d.Button;
			s.image = ImageFor(d);
			s.effects = new Il2CppReferenceArray<EffectAsset>(0);
			s.showEffects = false;
			s.notification = false;
			s.hidden = false;
			s.checkWarWithPlayer = false;
			s.optionalDest = country.Nation;
			var ev = new GlobalEvent(s, country.Nation, country.Nation);
			ev.Execute(EEventSource.Parliament);
		}

		static string EffectText(EventDefinition d, Country country)
		{
			var lines = new List<string>();
			if (d.HomePopulationPercent != 0f || d.HomePopulation != 0)
			{
				var g = Home.Plan(country, d);
				string how = d.HomePopulationPercent != 0f ? $"{d.HomePopulationPercent * 100:+0.#;-0.#}%" : "";
				if (d.HomePopulation != 0) how += (how.Length > 0 ? " and " : "") + $"{d.HomePopulation:+#,0;-#,0} in all";
				lines.Add($"Population of the home settlements in Britain: {how} (about {g.Population:+#,0;-#,0} now, {g.Workforce:#,0} of them workforce)");
			}
			if (d.MaxFactories != 0) lines.Add($"Factories that can be rented in Britain: {d.MaxFactories:+0;-0}");
			if (d.MaxShipyards != 0) lines.Add($"Shipyards that can be rented in Britain: {d.MaxShipyards:+0;-0}");
			foreach (var m in d.Modifiers)
				lines.Add(m.Percent ? $"{m.Modifier}: {m.Value * 100:+0.#;-0.#}%" : $"{m.Modifier}: {m.Value:+0.##;-0.##}");
			return lines.Count == 0 ? "" : "\n\n" + string.Join("\n", lines);
		}

		static Sprite ImageFor(EventDefinition d)
		{
			if (!string.IsNullOrEmpty(d.Image))
			{
				var file = Path.Combine(Path.GetDirectoryName(d.SourceFile) ?? "", d.Image);
				if (_sprites.TryGetValue(file, out var cached) && cached != null)
					return cached;
				try
				{
					var tex = new Texture2D(2, 2);
					if (ImageConversion.LoadImage(tex, File.ReadAllBytes(file)))
					{
						tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
						var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
						sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
						_keepAlive.Add(tex);
						_keepAlive.Add(sp);
						_sprites[file] = sp;
						return sp;
					}
					Plugin.Logger.LogWarning($"British events: {file} is not a png/jpg image.");
				}
				catch (Exception e)
				{
					Plugin.Logger.LogWarning($"British events: image {file} not loaded: {e.Message}");
				}
			}
			Sprite fallback = null;
			try
			{
				foreach (var ev in GameEvents())
				{
					if (ev.image == null) continue;
					if (string.IsNullOrEmpty(d.GameImage))
						return ev.image;    // Britain's first parliament event picture
					if (string.Equals(ev.name, d.GameImage, StringComparison.OrdinalIgnoreCase))
						return ev.image;
					fallback ??= ev.image;
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"British events: game event pictures not searched: {e.Message}");
			}
			if (!string.IsNullOrEmpty(d.GameImage))
				Plugin.Logger.LogWarning($"British events: gameImage \"{d.GameImage}\" not found, see BepInEx\\config\\ugar.britishevents.info.txt.");
			return fallback;
		}

		/// <summary>Parliament events of the player's nation first (the usual look), then every loaded game event.</summary>
		static IEnumerable<TimelineEventSettings> GameEvents()
		{
			var settings = PlayerCountry?.settings;
			var pe = settings?.parliamentEvents;
			if (pe != null)
				for (int i = 0; i < pe.Length; i++)
					if (pe[i] != null) yield return pe[i];
			foreach (var o in Resources.FindObjectsOfTypeAll<TimelineEventSettings>())
				if (o != null) yield return o;
		}

		// ---- Info for event authors ----

		static void OnCampaignLoaded(Country country)
		{
			try
			{
				var b = Bonus;
				int baseCap = RentCaps.BaseCap();
				var em = country.europeanManager;
				Plugin.Logger.LogInfo($"British events: campaign loaded ({country.Nation}, {_lastDay:yyyy-MM-dd}), {SaveMarkers.FiredCodes(country).Count} event(s) fired so far. " +
					$"Factory cap {baseCap}+{b.Factories}, shipyard cap {baseCap}+{b.Shipyards}" +
					(em != null ? $", rented {em.rentedFactories} factories / {em.rentedShipyards} shipyards." : "."));
				if (!_infoWritten && Plugin.WriteInfo.Value)
				{
					_infoWritten = true;
					WriteInfo(country);
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"British events: campaign info failed: {e.Message}");
			}
		}

		static void WriteInfo(Country country)
		{
			var sb = new StringBuilder();
			sb.AppendLine($"UGAR British Events - campaign info written {DateTime.Now:yyyy-MM-dd HH:mm} ({country.Nation}, game date {_lastDay:yyyy-MM-dd})");
			sb.AppendLine();
			sb.AppendLine("Rent caps (England screen): cap = rentCountFromLocalityCount(MAX_REMOTE_LOCALITY), same cap for factories and shipyards.");
			sb.AppendLine($"  rentCountFromLocalityCount keys (home settlements -> cap): {RentCaps.DescribeCurve()}");
			sb.AppendLine($"  MAX_REMOTE_LOCALITY now: {country.modifiersManager.GetModifierValue(EModifier.MAX_REMOTE_LOCALITY, false):0.##}");
			sb.AppendLine($"  game cap now: {RentCaps.BaseCap()}, event bonus: factories {Bonus.Factories:+0;-0;0}, shipyards {Bonus.Shipyards:+0;-0;0}");
			var em = country.europeanManager;
			if (em != null)
				sb.AppendLine($"  rented now: {em.rentedFactories} factories, {em.rentedShipyards} shipyards");
			sb.AppendLine();
			var homes = Home.Settlements(country);
			sb.AppendLine($"Home settlements in Britain ({homes.Count}) - population / workforce / recruits:");
			foreach (var l in homes)
				sb.AppendLine($"  {Home.Name(l)}: {l.population:#,0} / {l.workforce:#,0} / {l.recruits:#,0}");
			sb.AppendLine($"  RegionConfig.workforcePercent = {Config.game?.region?.workforcePercent:0.###} (share of a population boost that becomes workforce)");
			sb.AppendLine();
			sb.AppendLine("Home storage (England screen) - names for homeGoods are EInventoryType names:");
			var hs = Home.Storage(country)?.items;
			if (hs != null)
				for (int i = 0; i < hs.Count; i++)
				{
					var it = hs[i];
					if (it?.asset == null) continue;
					var res = it.asset.TryCast<ResourceItem>();
					sb.AppendLine($"  {Home.ItemName(it.asset)}: {it.count:#,0.##}" + (res != null ? $"  (trade good, type {res.inventoryType}, price {res.price:0.##})" : "  (not a trade good)"));
				}
			sb.AppendLine($"  money {country.inventory.Money:#,0}, supplies {country.inventory.ConstructionMaterials:#,0}, specialists {country.SpecialistCount} (country treasury, shared with America)");
			sb.AppendLine();
			var ctx = new Context(country);
			sb.AppendLine($"Owned settlements in America ({ctx.Settlements}) - names for ownsAll/ownsAny:");
			ctx.OwnedNames.Sort(StringComparer.OrdinalIgnoreCase);
			foreach (var n in ctx.OwnedNames)
				sb.AppendLine("  " + n);
			sb.AppendLine();
			sb.AppendLine("All settlement names (any owner):");
			var all = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
			{
				try { if (l != null && !string.IsNullOrEmpty(l.Name)) all.Add($"{l.Name} ({l.ownerNation})"); } catch { }
			}
			foreach (var n in all)
				sb.AppendLine("  " + n);
			sb.AppendLine();
			sb.AppendLine("Game event pictures for gameImage (asset names):");
			var seen = new HashSet<string>();
			foreach (var ev in GameEvents())
				if (ev.image != null && seen.Add(ev.name))
					sb.AppendLine($"  {ev.name}  ({Trim(ev.Header)})");
			sb.AppendLine();
			sb.AppendLine("Loaded mod events:");
			var fired = SaveMarkers.FiredCodes(country);
			var paidSet = SaveMarkers.PaidCodes(country);
			foreach (var d in _defs)
				sb.AppendLine($"  {d.Id}  {(paidSet.Contains(d.Code) ? "PAID" : fired.Contains(d.Code) ? "PASSED" : "not fired")}  ({Path.GetFileName(d.SourceFile)})");
			File.WriteAllText(Path.Combine(Paths.ConfigPath, "ugar.britishevents.info.txt"), sb.ToString());
		}

		static string Trim(string s)
		{
			if (s == null) return "";
			s = s.Replace("\n", " ");
			return s.Length > 60 ? s.Substring(0, 60) + "..." : s;
		}
	}
}
