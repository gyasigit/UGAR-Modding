// Custom building definitions, read from *.building.json files anywhere under BepInEx\plugins.
// See docs/custom-buildings.md for the format.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UGARCustomBuildings
{
	internal sealed class CostEntry
	{
		public string Resource;     // EInventoryType name: Money, ConstructionMaterial, Specialist, Wood, Iron, ...
		public float Amount;
	}

	internal sealed class ModifierEntry
	{
		public string Modifier;     // World.EModifier name, e.g. RECRUITS_PERCENT
		public float Value;         // percent modifiers are fractions: 0.1 = +10%
		public bool Percent = true; // shown as a percentage in the game's effect text
	}

	internal sealed class BuildingDefinition
	{
		public string Id;
		public string Name;
		public string Description;
		public List<string> Templates = new List<string>();     // game buildings to copy the look (and relative cost) from
		public List<string> SettlementTypes = new List<string>(); // ERegionLocality names; empty = wherever the template is buildable
		public List<string> Nations = new List<string>();       // ENation names; empty = all
		public bool AiCanBuild;
		public int MaxPerNation;                                // 0 = no limit (still one per settlement)
		public List<string> Requires = new List<string>();      // game buildings that must be finished first

		// Cost and build time: either absolute values or multipliers of the template's.
		public List<CostEntry> Cost;
		public float CostMultiplier = 1f;
		public float? BuildPoints;
		public float BuildPointsMultiplier = 1f;
		public List<CostEntry> Upkeep;          // null = no upkeep
		public bool Indestructible;

		// Effects the game applies itself (locality modifiers).
		public readonly List<ModifierEntry> Modifiers = new List<ModifierEntry>();

		// Nation-wide modifiers added to the owner country's ModifiersManager while the building stands (summed over
		// all such buildings the country owns), e.g. BLOCK_MILITA = the militia regiment limit.
		public readonly List<ModifierEntry> CountryModifiers = new List<ModifierEntry>();

		// Effects this plugin applies.
		public float PopulationGrowthBonus;     // 1.0 = +100% of the settlement's natural weekly growth
		public int WeeklyWorkforce;             // flat workforce added every week

		// Weekly conversion (player only): if the nation's storage holds all of WeeklyConsume, take it and grant renown.
		public List<CostEntry> WeeklyConsume;
		public float WeeklyRenown;
		public bool HasWeeklyConversion => WeeklyConsume != null && WeeklyConsume.Count > 0 && WeeklyRenown != 0f;

		public string SourceFile;
		public bool Placeholder;                // created for a save that references an unknown id

		public string AssetPath => Registry.PathPrefix + Id;

		public bool HasCustomEffects => PopulationGrowthBonus != 0f || WeeklyWorkforce != 0;

		public static List<BuildingDefinition> LoadAll(string pluginsDir, Action<string> warn)
		{
			var list = new List<BuildingDefinition>();
			if (!Directory.Exists(pluginsDir))
				return list;
			var files = new List<string>(Directory.GetFiles(pluginsDir, "*.building.json", SearchOption.AllDirectories));
			files.Sort(StringComparer.OrdinalIgnoreCase);
			foreach (var file in files)
			{
				try
				{
					var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
					using var doc = JsonDocument.Parse(File.ReadAllText(file), opts);
					var root = doc.RootElement;
					if (root.ValueKind == JsonValueKind.Array)
					{
						foreach (var e in root.EnumerateArray())
							list.Add(Parse(e, file));
					}
					else
					{
						list.Add(Parse(root, file));
					}
				}
				catch (Exception e)
				{
					warn($"Skipped {file}: {e.Message}");
				}
			}
			return list;
		}

		static BuildingDefinition Parse(JsonElement e, string file)
		{
			var d = new BuildingDefinition { SourceFile = file };
			d.Id = Str(e, "id") ?? throw new FormatException("\"id\" is required");
			if (d.Id.IndexOfAny(new[] { '/', '\\', ' ' }) >= 0)
				throw new FormatException($"id \"{d.Id}\" must not contain spaces or slashes");
			d.Name = Str(e, "name") ?? d.Id;
			d.Description = Str(e, "description") ?? "";
			d.Templates = StrList(e, "template");
			d.SettlementTypes = StrList(e, "settlementTypes");
			d.Nations = StrList(e, "nations");
			d.Requires = StrList(e, "requires");
			d.AiCanBuild = Bool(e, "aiCanBuild", false);
			if (e.TryGetProperty("maxPerNation", out var maxN) && maxN.ValueKind == JsonValueKind.Number)
				d.MaxPerNation = maxN.GetInt32();
			d.Indestructible = Bool(e, "indestructible", false);

			if (e.TryGetProperty("cost", out var cost))
			{
				if (cost.ValueKind == JsonValueKind.Array)
					d.Cost = Costs(cost);
				else if (cost.ValueKind == JsonValueKind.Object && cost.TryGetProperty("multiplier", out var m))
					d.CostMultiplier = m.GetSingle();
				else
					throw new FormatException("\"cost\" must be a list of {resource, amount} or {\"multiplier\": x}");
			}
			if (e.TryGetProperty("buildPoints", out var bp))
			{
				if (bp.ValueKind == JsonValueKind.Number)
					d.BuildPoints = bp.GetSingle();
				else if (bp.ValueKind == JsonValueKind.Object && bp.TryGetProperty("multiplier", out var m))
					d.BuildPointsMultiplier = m.GetSingle();
				else
					throw new FormatException("\"buildPoints\" must be a number or {\"multiplier\": x}");
			}
			if (e.TryGetProperty("upkeep", out var up) && up.ValueKind == JsonValueKind.Array)
				d.Upkeep = Costs(up);

			if (e.TryGetProperty("effects", out var fx) && fx.ValueKind == JsonValueKind.Object)
			{
				foreach (var p in fx.EnumerateObject())
				{
					switch (p.Name)
					{
						case "populationGrowthBonus": d.PopulationGrowthBonus = p.Value.GetSingle(); break;
						case "weeklyWorkforce": d.WeeklyWorkforce = p.Value.GetInt32(); break;
						case "modifiers":
							foreach (var m in p.Value.EnumerateArray())
								d.Modifiers.Add(Modifier(m, true));
							break;
						case "weeklyConversion":
							if (!p.Value.TryGetProperty("consume", out var consume) || consume.ValueKind != JsonValueKind.Array)
								throw new FormatException("weeklyConversion needs a \"consume\" list of {resource, amount}");
							d.WeeklyConsume = Costs(consume);
							d.WeeklyRenown = p.Value.TryGetProperty("renown", out var rn) ? rn.GetSingle() : 0f;
							break;
						case "countryModifiers":
							foreach (var m in p.Value.EnumerateArray())
								d.CountryModifiers.Add(Modifier(m, false));
							break;
						default: throw new FormatException($"unknown effect \"{p.Name}\"");
					}
				}
			}
			return d;
		}

		static ModifierEntry Modifier(JsonElement m, bool defaultPercent) => new ModifierEntry
		{
			Modifier = Str(m, "modifier") ?? throw new FormatException("modifier entry needs \"modifier\""),
			Value = m.GetProperty("value").GetSingle(),
			Percent = Bool(m, "percent", defaultPercent),
		};

		static List<CostEntry> Costs(JsonElement arr)
		{
			var list = new List<CostEntry>();
			foreach (var c in arr.EnumerateArray())
				list.Add(new CostEntry { Resource = Str(c, "resource"), Amount = c.GetProperty("amount").GetSingle() });
			return list;
		}

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
