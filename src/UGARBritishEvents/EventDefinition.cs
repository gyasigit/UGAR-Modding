// British event definitions, read from *.event.json files anywhere under BepInEx\plugins.
// See docs/british-events.md for the format.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace UGARBritishEvents
{
	internal sealed class ModifierEntry
	{
		public string Modifier;     // World.EModifier name, e.g. RECRUITS_PERCENT
		public float Value;         // percent modifiers are fractions: 0.1 = +10%
		public bool Percent = true;
	}

	internal sealed class CostRange
	{
		public float Min;
		public float Max;
	}

	internal sealed class EventDefinition
	{
		public string Id;
		public string Title;
		public string Text;
		public string Button;
		public string Image;                // png file next to the json, or empty
		public string GameImage;            // name of a game event asset to borrow the picture from
		public List<string> Nations = new List<string>();

		// Trigger (all must hold).
		public DateTime? From;
		public DateTime? Until;
		public float ChancePerWeek = 1f;
		public int MinSettlements;          // owned settlements in America (home localities in Britain don't count)
		public List<string> OwnsAll = new List<string>();
		public List<string> OwnsAny = new List<string>();
		public List<string> Requires = new List<string>();  // other event ids that must have fired
		public int MinRentedFactories;
		public int MinRentedShipyards;
		public bool Repeatable;             // may fire again after RepeatAfterDays (effects stack when paid again)
		public int RepeatAfterDays = 365;
		// F8 mod manager settings (Plugin.BindEventSettings), defaulting to the json values; they override them.
		public BepInEx.Configuration.ConfigEntry<bool> RepeatableSetting;
		public BepInEx.Configuration.ConfigEntry<int> RepeatAfterDaysSetting;
		public bool IsRepeatable => RepeatableSetting?.Value ?? Repeatable;
		public int RepeatDays => Math.Max(1, RepeatAfterDaysSetting?.Value ?? RepeatAfterDays);

		// Cost asked when the event fires (each a random amount in its range); the player pays it or passes.
		// No cost = the effects apply at once and the event only has an acknowledge button.
		public CostRange CostMoney;
		public CostRange CostSupplies;      // construction materials, the resource buildings cost
		public CostRange CostSpecialists;   // officers from the reserve pool
		// Goods from Britain's home (England screen) storage, europeanManager.inventory.itemStorage:
		public readonly Dictionary<string, CostRange> CostHomeGoods = new Dictionary<string, CostRange>(StringComparer.OrdinalIgnoreCase); // EInventoryType -> amount
		public int HomeStockKinds;          // pick this many of the trade goods actually in home storage...
		public CostRange HomeStockShare;    // ...and ask this share of each (0.25 = a quarter of the stock)
		public string PayButton;
		public string PassButton;
		public bool HasCost => CostMoney != null || CostSupplies != null || CostSpecialists != null ||
			CostHomeGoods.Count > 0 || HomeStockKinds > 0;

		// Effects (only when paid, for events with a cost).
		public int MaxFactories;            // extra rentable factories in Britain
		public int MaxShipyards;            // extra rentable shipyards in Britain
		public float HomePopulationPercent; // each home settlement gains this share of its population (0.3 = +30%)
		public int HomePopulation;          // flat population added, split evenly over the home settlements
		public readonly List<ModifierEntry> Modifiers = new List<ModifierEntry>();   // permanent country modifiers

		public string SourceFile;

		/// <summary>Stable 24-bit code of the id, stored in the save (see SaveMarkers).</summary>
		public int Code => CodeOf(Id);

		public static int CodeOf(string id)
		{
			uint h = 2166136261;
			foreach (char c in id.ToLowerInvariant())
			{
				h ^= c;
				h *= 16777619;
			}
			return (int)(h & 0xFFFFFF);
		}

		public static List<EventDefinition> LoadAll(string pluginsDir, Action<string> warn)
		{
			var list = new List<EventDefinition>();
			if (!Directory.Exists(pluginsDir))
				return list;
			var files = new List<string>(Directory.GetFiles(pluginsDir, "*.event.json", SearchOption.AllDirectories));
			files.Sort(StringComparer.OrdinalIgnoreCase);
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var codes = new Dictionary<int, string>();
			foreach (var file in files)
			{
				try
				{
					var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
					using var doc = JsonDocument.Parse(File.ReadAllText(file), opts);
					var root = doc.RootElement;
					var parsed = new List<EventDefinition>();
					if (root.ValueKind == JsonValueKind.Array)
						foreach (var e in root.EnumerateArray())
							parsed.Add(Parse(e, file));
					else
						parsed.Add(Parse(root, file));
					foreach (var d in parsed)
					{
						if (!ids.Add(d.Id))
						{
							warn($"Skipped duplicate event id \"{d.Id}\" in {file}");
							continue;
						}
						if (codes.TryGetValue(d.Code, out var other))
						{
							warn($"Skipped event \"{d.Id}\" in {file}: its id collides with \"{other}\", rename one of them");
							continue;
						}
						codes[d.Code] = d.Id;
						list.Add(d);
					}
				}
				catch (Exception e)
				{
					warn($"Skipped {file}: {e.Message}");
				}
			}
			return list;
		}

		static EventDefinition Parse(JsonElement e, string file)
		{
			var d = new EventDefinition { SourceFile = file };
			d.Id = Str(e, "id") ?? throw new FormatException("\"id\" is required");
			d.Title = Str(e, "title") ?? d.Id;
			d.Text = Str(e, "text") ?? "";
			d.Button = Str(e, "button");
			d.Image = Str(e, "image");
			d.GameImage = Str(e, "gameImage");
			d.Nations = StrList(e, "nations");
			d.Repeatable = Bool(e, "repeatable", false);
			if (e.TryGetProperty("repeatAfterDays", out var rad) && rad.ValueKind == JsonValueKind.Number)
				d.RepeatAfterDays = Math.Max(1, rad.GetInt32());
			d.PayButton = Str(e, "payButton");
			d.PassButton = Str(e, "passButton");
			if (e.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object)
			{
				foreach (var p in cost.EnumerateObject())
				{
					switch (p.Name)
					{
						case "money": d.CostMoney = Range(p.Value); break;
						case "supplies": d.CostSupplies = Range(p.Value); break;
						case "specialists": d.CostSpecialists = Range(p.Value); break;
						case "homeGoods":
							foreach (var g in p.Value.EnumerateObject())
								d.CostHomeGoods[g.Name] = Range(g.Value);
							break;
						case "homeStock":
							d.HomeStockKinds = p.Value.TryGetProperty("kinds", out var k) ? k.GetInt32() : 2;
							d.HomeStockShare = p.Value.TryGetProperty("share", out var sh) ? Range(sh) : new CostRange { Min = 0.25f, Max = 0.5f };
							if (d.HomeStockKinds < 1 || d.HomeStockShare.Max > 1f)
								throw new FormatException("homeStock needs kinds >= 1 and a share range within 0..1");
							break;
						default: throw new FormatException($"unknown cost \"{p.Name}\" (money, supplies, specialists, homeGoods, homeStock)");
					}
				}
			}
			if (d.Nations.Count == 0)
				d.Nations.Add("Britain");

			if (e.TryGetProperty("trigger", out var t) && t.ValueKind == JsonValueKind.Object)
			{
				foreach (var p in t.EnumerateObject())
				{
					switch (p.Name)
					{
						case "from": d.From = Date(p.Value); break;
						case "until": d.Until = Date(p.Value); break;
						case "chancePerWeek": d.ChancePerWeek = Math.Clamp(p.Value.GetSingle(), 0f, 1f); break;
						case "minSettlements": d.MinSettlements = p.Value.GetInt32(); break;
						case "ownsAll": d.OwnsAll = StrList(t, p.Name); break;
						case "ownsAny": d.OwnsAny = StrList(t, p.Name); break;
						case "requires": d.Requires = StrList(t, p.Name); break;
						case "minRentedFactories": d.MinRentedFactories = p.Value.GetInt32(); break;
						case "minRentedShipyards": d.MinRentedShipyards = p.Value.GetInt32(); break;
						default: throw new FormatException($"unknown trigger \"{p.Name}\"");
					}
				}
			}

			if (e.TryGetProperty("effects", out var fx) && fx.ValueKind == JsonValueKind.Object)
			{
				foreach (var p in fx.EnumerateObject())
				{
					switch (p.Name)
					{
						case "maxFactories": d.MaxFactories = p.Value.GetInt32(); break;
						case "maxShipyards": d.MaxShipyards = p.Value.GetInt32(); break;
						case "homePopulationPercent": d.HomePopulationPercent = p.Value.GetSingle(); break;
						case "homePopulation": d.HomePopulation = p.Value.GetInt32(); break;
						case "modifiers":
							foreach (var m in p.Value.EnumerateArray())
								d.Modifiers.Add(new ModifierEntry
								{
									Modifier = Str(m, "modifier") ?? throw new FormatException("modifier entry needs \"modifier\""),
									Value = m.GetProperty("value").GetSingle(),
									Percent = Bool(m, "percent", true),
								});
							break;
						default: throw new FormatException($"unknown effect \"{p.Name}\"");
					}
				}
			}
			return d;
		}

		static CostRange Range(JsonElement v)
		{
			CostRange r;
			if (v.ValueKind == JsonValueKind.Number)
				r = new CostRange { Min = v.GetSingle(), Max = v.GetSingle() };
			else if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2)
				r = new CostRange { Min = v[0].GetSingle(), Max = v[1].GetSingle() };
			else
				throw new FormatException("a cost must be a number or [min, max]");
			if (r.Min < 0 || r.Max < r.Min)
				throw new FormatException($"bad cost range [{r.Min}, {r.Max}]");
			return r;
		}

		static DateTime Date(JsonElement v) =>
			DateTime.ParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

		static string Str(JsonElement e, string name) =>
			e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

		static bool Bool(JsonElement e, string name, bool def) =>
			e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;

		static List<string> StrList(JsonElement e, string name)
		{
			var list = new List<string>();
			if (!e.TryGetProperty(name, out var v))
				return list;
			if (v.ValueKind == JsonValueKind.String)
				list.Add(v.GetString());
			else if (v.ValueKind == JsonValueKind.Array)
				foreach (var s in v.EnumerateArray())
					list.Add(s.GetString());
			return list;
		}
	}
}
