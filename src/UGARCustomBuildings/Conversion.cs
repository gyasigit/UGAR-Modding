// Weekly goods-for-renown conversion, its status display, and the per-nation building limit.
//
// Renown: MonoBehaviourSingleton<RenownManager>.instance belongs to the player.
// RenownManager.ReportRenown(amount, ERenownOperation) adds it like events do (RenownActionAsset.Transfer): gains are
// multiplied by RenownConfig.positiveRenownMultiplier(current renown), so high renown earns less. Booked as QUEST so it
// shows in the weekly renown breakdown.
// Storage: MarketManager.BuyItem puts purchases in country.inventory, or europeanManager.inventory (Britain's England
// storage) when bought on the England screen (Country.IsEuropeanAction). Goods count from both: colony stock is used
// first, England stock for the rest.
// Notifications: Country.PostNotification(new NotificationRecord(ENotification, text, eNotificationType, priority)) is the
// game's own notification list (what Country.PostLocalityResourceNotification uses for its "not enough X for Y" notice); record.items holds item assets shown as icons. Records are saved with the campaign.
// Limit: the build click (LocalityInfoPanel.StartConstruction → RegionLocality.StartConstruction) checks
// ConstructionSettings.CanConstruct(player) before the building is placed, and the build menu greys out buildings that
// fail it, so a CanConstruct postfix enforces maxPerNation. It counts buildings already built or under construction.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using World;
using World.Notification;
using World.SceneObject;

namespace UGARCustomBuildings
{
	internal sealed class Need
	{
		public string Name;
		public PathAsset Asset;
		public float Amount;     // per building per week
		public float Colony;
		public float England;
		public float Have => Colony + England;
	}

	internal static class Conversion
	{
		const string Green = "#8FD16A";
		const string Red = "#E07060";
		const string Dim = "#9A9A9A";

		static string C(string color, string s) => $"<color={color}>{s}</color>";

		static PathAsset ItemAsset(EInventoryType type)
		{
			var items = Config.game?.inventorySettings?.inventoryItems;
			int i = (int)type;
			return items != null && i >= 0 && i < items.Length ? items[i] : null;
		}

		static DateTime Today
		{
			get
			{
				var d = RuntimeVars.date;
				return new DateTime(d.Year, d.Month, d.Day);
			}
		}

		/// <summary>What one building needs per week, and how much the owner holds in colony and England storage.</summary>
		static List<Need> Needs(BuildingDefinition d, Country country)
		{
			var colony = country?.inventory?.itemStorage;
			var england = country?.europeanManager?.inventory?.itemStorage;
			var list = new List<Need>();
			foreach (var c in d.WeeklyConsume)
			{
				if (!Enum.TryParse<EInventoryType>(c.Resource, true, out var type) || ItemAsset(type) == null)
					return null;
				var a = ItemAsset(type);
				list.Add(new Need
				{
					Name = c.Resource,
					Asset = a,
					Amount = c.Amount,
					Colony = colony != null ? colony.GetItemCount(a) : 0f,
					England = england != null ? england.GetItemCount(a) : 0f,
				});
			}
			return list;
		}

		// ---- Weekly run ----

		sealed class LastResult
		{
			public DateTime Date;
			public bool Held;
			public float Renown;
			public string Short;
		}

		static readonly Dictionary<IntPtr, LastResult> _last = new Dictionary<IntPtr, LastResult>();
		static DateTime _lastWeekly;

		/// <summary>Runs every finished conversion building in a player settlement once (weekly, before population growth).</summary>
		public static void Weekly(RegionLocality l)
		{
			if (!Plugin.Enabled.Value || l == null || l.ownerNation != RuntimeVars.playerNation)
				return;
			foreach (var (e, strength) in Growth.Built(l))
			{
				var d = e.Def;
				if (!d.HasWeeklyConversion)
					continue;
				try
				{
					_lastWeekly = Today;
					Run(l, e, strength);
				}
				catch (Exception ex)
				{
					Plugin.Logger.LogWarning($"{d.Name} at {l.Name}: weekly conversion failed: {ex.Message}");
				}
			}
		}

		static void Run(RegionLocality l, Entry e, float strength)
		{
			var d = e.Def;
			var country = ENationUtil.GetCountry(l.ownerNation);
			var renown = MonoBehaviourSingleton<RenownManager>.instance;
			if (country == null || renown == null)
				return;
			var needs = Needs(d, country);
			if (needs == null)
			{
				Plugin.Logger.LogWarning($"{d.Name}: unknown resource in weeklyConversion ({Describe(d)}); skipped.");
				return;
			}

			string shortText = ShortText(needs, 1);
			if (shortText != null)
			{
				_last[l.Pointer] = new LastResult { Date = Today, Held = false, Short = shortText };
				if (Plugin.LogGrowth.Value)
					Plugin.Logger.LogInfo($"{d.Name} at {l.Name}: not enough goods this week (short {shortText}); no renown.");
				if (Plugin.NotifySkipped.Value)
					Notify(country, needs, eNotificationType.WARNING,
						$"{d.Name} at {l.Name}: no reception this week, short of {shortText}. Needs {Describe(d)} a week (colonies or England storage).");
				return;
			}

			// Colony stock first, England for the rest. Same removal call the game's own conversion buildings use
			// (LocalityResourceConversion.Convert: Remove(item, 0, null)).
			var colony = country.inventory?.itemStorage;
			var england = country.europeanManager?.inventory?.itemStorage;
			foreach (var n in needs)
			{
				float fromColony = Math.Min(n.Amount, n.Colony);
				if (fromColony > 0f) colony.Remove(new CountItem(n.Asset, fromColony), (EItemSource)0, null);
				float rest = n.Amount - fromColony;
				if (rest > 0f) england.Remove(new CountItem(n.Asset, rest), (EItemSource)0, null);
			}
			float before = renown.RenownData.renown;
			renown.ReportRenown(d.WeeklyRenown * strength, ERenownOperation.QUEST);
			float gained = renown.RenownData.renown - before;
			_last[l.Pointer] = new LastResult { Date = Today, Held = true, Renown = gained };
			Plugin.Logger.LogInfo($"{d.Name} at {l.Name}: used {Describe(d)}, +{gained:0.##} renown (base {d.WeeklyRenown * strength:0.##}).");
			if (Plugin.NotifyHeld.Value)
				Notify(country, null, eNotificationType.NORMAL, $"{d.Name} at {l.Name}: reception held, +{gained:0.#} renown.");

			// Heads-up for next week, with what's left after this week's use.
			if (Plugin.NotifyNextWeek.Value)
			{
				var after = Needs(d, country);
				string nextShort = after == null ? null : ShortText(after, 1);
				if (nextShort != null)
					Notify(country, after, eNotificationType.NORMAL,
						$"{d.Name} at {l.Name}: next week's reception is short of {nextShort}. Buy more before the week ends.");
			}
		}

		static void Notify(Country country, List<Need> needs, eNotificationType level, string text)
		{
			try
			{
				var rec = new NotificationRecord(ENotification.RENOWN, text, level, false);
				if (needs != null && rec.items != null)
					foreach (var n in needs)
						if (n.Have < n.Amount) rec.items.Add(n.Asset);
				country.PostNotification(rec);
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Custom building notification not shown: {ex.Message}");
			}
		}

		/// <summary>"1 Tea, 1 Luxury" for what's missing to cover the given number of buildings for a week, or null.</summary>
		static string ShortText(List<Need> needs, int buildings)
		{
			var parts = new List<string>();
			foreach (var n in needs)
			{
				float missing = n.Amount * buildings - n.Have;
				if (missing > 0f) parts.Add($"{missing:0.#} {n.Name}");
			}
			return parts.Count == 0 ? null : string.Join(", ", parts);
		}

		public static string Describe(BuildingDefinition d)
		{
			var parts = new List<string>();
			foreach (var c in d.WeeklyConsume)
				parts.Add($"{c.Amount:0.#} {c.Resource}");
			return string.Join(" + ", parts);
		}

		// ---- Status for the game's tooltips (build menu and settlement panel) ----

		/// <summary>Lines appended to GetEffectsInfo: stock vs need, ready/short, weeks of supply, next and last reception.</summary>
		public static string Status(Entry e, RegionLocality l)
		{
			var d = e.Def;
			if (!d.HasWeeklyConversion || !Plugin.Enabled.Value)
				return null;
			var nation = l != null ? l.ownerNation : RuntimeVars.playerNation;
			var country = ENationUtil.GetCountry(nation);
			var needs = Needs(d, country);
			if (needs == null)
				return null;

			int built = Math.Max(1, CountBuilt(e, nation));
			var sb = new StringBuilder();
			sb.Append(C(Dim, built > 1 ? $"Goods in storage (need for your {built} buildings):" : "Goods in storage (need per week):")).Append('\n');
			foreach (var n in needs)
			{
				float need = n.Amount * built;
				string have = C(n.Have >= need ? Green : Red, $"{n.Have:0.#}/{need:0.#}");
				string where = n.England > 0f ? C(Dim, $" ({n.England:0.#} in England)") : "";
				sb.Append($"  {n.Name} {have}{where}\n");
			}

			string shortText = ShortText(needs, built);
			int weeks = int.MaxValue;
			foreach (var n in needs)
				weeks = Math.Min(weeks, (int)Math.Floor(n.Have / (n.Amount * built)));
			if (shortText == null)
				sb.Append(C(Green, weeks >= 2 ? $"Ready: enough for {weeks} weeks" : "Ready for next week")).Append('\n');
			else
				sb.Append(C(Red, $"Short: {shortText}")).Append('\n');

			if (_lastWeekly != default)
				sb.Append(C(Dim, $"Next reception: {_lastWeekly.AddDays(7):d MMM yyyy}")).Append('\n');
			else
				sb.Append(C(Dim, "Next reception: at the start of the next week")).Append('\n');

			if (l != null && _last.TryGetValue(l.Pointer, out var r))
				sb.Append(r.Held
					? C(Green, $"Last reception ({r.Date:d MMM}): +{r.Renown:0.#} renown")
					: C(Red, $"Last week ({r.Date:d MMM}): skipped, short of {r.Short}")).Append('\n');
			return sb.ToString().TrimEnd('\n');
		}

		// ---- Counts (maxPerNation, and how many finished buildings share the goods) ----

		static readonly Dictionary<(IntPtr, ENation), int> _counts = new Dictionary<(IntPtr, ENation), int>();
		static readonly Dictionary<(IntPtr, ENation), int> _built = new Dictionary<(IntPtr, ENation), int>();
		static float _countsTime = -1f;

		static void Recount()
		{
			float now = Time.realtimeSinceStartup;
			if (now - _countsTime <= 1f)
				return;
			_counts.Clear();
			_built.Clear();
			_countsTime = now;
			foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
			{
				var slots = l?.constructionSlots;
				for (int i = 0; slots != null && i < slots.Length; i++)
				{
					var c = slots[i];
					var s = c?.settings;
					var own = s == null ? null : Registry.Find(s.Pointer);
					if (own == null) continue;
					var key = (own.Ptr, l.ownerNation);
					_counts.TryGetValue(key, out int n);
					_counts[key] = n + 1;
					if (c.state == EConstructionState.READY)
					{
						_built.TryGetValue(key, out int b);
						_built[key] = b + 1;
					}
				}
			}
		}

		/// <summary>How many of this building the nation has, built or under construction (cached for a second).</summary>
		public static int CountOwned(Entry e, ENation nation)
		{
			Recount();
			return _counts.TryGetValue((e.Ptr, nation), out int c) ? c : 0;
		}

		/// <summary>How many of this building the nation has finished (cached for a second).</summary>
		public static int CountBuilt(Entry e, ENation nation)
		{
			Recount();
			return _built.TryGetValue((e.Ptr, nation), out int c) ? c : 0;
		}

		public static void Invalidate() => _countsTime = -1f;
	}
}
