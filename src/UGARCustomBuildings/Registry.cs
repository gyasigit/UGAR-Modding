// Turns building definitions into real LocalityConstructionSettings assets the game treats like its own.
//
// How the game handles settlement buildings:
// - ConstructionConfig.GetBuildings(settlement type, nation, region) lists what a settlement can build: the
//   LocalityConstructionSettings in localityBuildings[type], filtered by availableNations and required resource.
//   The build menu (LocalityInfoPanel.ShowConstructionsPanel), the AI (NeedSkeep) and FixConstructions (which
//   removes and refunds buildings that are no longer in the list) all use it. We add our assets in a postfix.
// - A building is a new one (free slot) when level == 0; level >= 1 means "upgrade of a building whose upgrades
//   list contains it". RequireConstruction and StartBuild need non-null preconditions/upgrades arrays.
// - Uniqueness per settlement is by buildingType (+ level), so every custom building gets its own buildingType value
//   above ELocalityConstruction.Max. The only array indexed by it (territory statistics) goes through
//   Construction.get_BuildingType, which we map back to the template's type.
// - Built effects: RegionLocality.CalculateConstructionEffects copies settings.effects into the settlement's
//   modifiers (scaled by damage). roundToInt also rounds the applied value, so it must be off for fractions.
// - Saves store the asset by assetPath (PathAssetExt.WritePathAsset) and load it with Resources.Load(path, type).
//   We answer Resources.Load for our "UGARMods/Buildings/<id>" paths.
using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using World;
using World.Configuration;
using World.Modifiers;
using World.SceneObject;

namespace UGARCustomBuildings
{
	internal sealed class Entry
	{
		public BuildingDefinition Def;
		public LocalityConstructionSettings Asset;
		public IntPtr Ptr;
		public ELocalityConstruction CustomType;
		public ELocalityConstruction TemplateType;
		public LocalityConstructionSettings Template;
		public readonly HashSet<ERegionLocality> SettlementTypes = new HashSet<ERegionLocality>();
		public readonly HashSet<ENation> Nations = new HashSet<ENation>();
		public bool Configured;
	}

	internal static class Registry
	{
		public const string PathPrefix = "UGARMods/Buildings/";
		const int FirstCustomType = 1000;

		static readonly Dictionary<string, BuildingDefinition> Defs = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
		static readonly Dictionary<string, Entry> ById = new Dictionary<string, Entry>(StringComparer.Ordinal);
		static readonly Dictionary<IntPtr, Entry> ByPtr = new Dictionary<IntPtr, Entry>();
		static readonly Dictionary<int, Entry> ByType = new Dictionary<int, Entry>();
		static int _nextType = FirstCustomType;
		static bool _configured;

		public static IEnumerable<Entry> Entries => ById.Values;

		public static void SetDefinitions(List<BuildingDefinition> defs)
		{
			defs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // stable buildingType numbers
			foreach (var d in defs)
			{
				if (Defs.ContainsKey(d.Id))
				{
					Plugin.Logger.LogWarning($"Duplicate building id \"{d.Id}\" in {d.SourceFile}; keeping the one from {Defs[d.Id].SourceFile}.");
					continue;
				}
				Defs[d.Id] = d;
			}
		}

		public static int DefinitionCount => Defs.Count;

		public static Entry Find(IntPtr settings) => settings != IntPtr.Zero && ByPtr.TryGetValue(settings, out var e) ? e : null;

		public static Entry FindByType(int buildingType) => ByType.TryGetValue(buildingType, out var e) ? e : null;

		/// <summary>The asset for an id; creates it (and a placeholder definition for unknown ids from old saves).</summary>
		public static Entry GetOrCreate(string id)
		{
			if (ById.TryGetValue(id, out var e))
				return e;
			if (!Defs.TryGetValue(id, out var def))
			{
				def = new BuildingDefinition
				{
					Id = id,
					Name = "Removed modded building",
					Description = $"This building came from a mod that is no longer installed ({id}). It has no effect and can be demolished.",
					Placeholder = true,
				};
				Defs[id] = def;
				Plugin.Logger.LogWarning($"A save uses custom building \"{id}\", which no installed *.building.json defines. Loading it as an empty placeholder.");
			}

			var so = ScriptableObject.CreateInstance(Il2CppType.Of<LocalityConstructionSettings>()).Cast<LocalityConstructionSettings>();
			so.name = id;
			so.hideFlags = HideFlags.DontUnloadUnusedAsset;
			so.assetPath = def.AssetPath;
			so.header = def.Name;
			so.description = def.Description;
			so.level = 0;
			so.roundToInt = false;
			so.effectsModifier = LocalityConstructionSettings.EEffectsModifier.None;
			so.hasResourceCondition = false;
			so.hasResourceConversion = false;
			so.indestructible = def.Indestructible;
			so.upgrades = new Il2CppReferenceArray<LocalityConstructionSettings>(0);
			so.preconditions = new Il2CppReferenceArray<LocalityConstructionSettings>(0);
			so.cost = new Il2CppReferenceArray<Requirement>(0);
			so.upkeep = new Il2CppReferenceArray<Requirement>(0);
			so.effects = new Il2CppReferenceArray<ModifierParameterData>(0);
			so.pointsToConstruct = 1f;

			e = new Entry { Def = def, Asset = so, Ptr = so.Pointer, CustomType = (ELocalityConstruction)_nextType++ };
			so.buildingType = e.CustomType;
			ById[id] = e;
			ByPtr[e.Ptr] = e;
			ByType[(int)e.CustomType] = e;
			if (_configured)
				Configure(e);
			return e;
		}

		static ConstructionConfig Constructions
		{
			get
			{
				var game = Config.game;
				return game == null ? null : game.constructions;
			}
		}

		/// <summary>Creates and fills every defined building once the game config is loaded. Safe to call often.</summary>
		public static bool EnsureReady()
		{
			if (_configured)
				return true;
			var cc = Constructions;
			if (cc == null || cc.localityBuildings == null)
				return false;
			_configured = true;
			try
			{
				if (Plugin.DumpGameBuildings.Value)
					Dump.Write(cc);
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Building dump failed: {ex.Message}");
			}
			foreach (var id in new List<string>(Defs.Keys))
				GetOrCreate(id);
			foreach (var e in new List<Entry>(ById.Values))
				Configure(e);
			return true;
		}

		static void Configure(Entry e)
		{
			if (e.Configured)
				return;
			e.Configured = true;
			var d = e.Def;
			var so = e.Asset;
			try
			{
				var cc = Constructions;
				e.Template = FindGameBuilding(cc, d.Templates, out var templateTypes);
				if (e.Template == null && !d.Placeholder)
					Plugin.Logger.LogWarning($"{d.Name}: none of the templates [{string.Join(", ", d.Templates)}] is a settlement building of this game; using a plain look and the absolute cost.");

				var t = e.Template ?? FirstGameBuilding(cc);
				if (t != null)
				{
					so.icon = t.icon;
					so.headerImage = t.headerImage;
					so.image = t.image;
					so.textColor = t.textColor;
					so.type = t.type;
					so.proportialEffectFromDamage = t.proportialEffectFromDamage;
					so.lootItems = t.lootItems;
					so.forageItem = t.forageItem;
					e.TemplateType = t.buildingType;
				}

				// Cost and build time.
				if (d.Cost != null)
					so.cost = Requirements(d.Cost, d.Name);
				else if (e.Template != null)
					so.cost = Scaled(e.Template.cost, d.CostMultiplier);
				if (d.BuildPoints.HasValue)
					so.pointsToConstruct = d.BuildPoints.Value;
				else if (e.Template != null)
					so.pointsToConstruct = e.Template.pointsToConstruct * d.BuildPointsMultiplier;
				if (d.Upkeep != null)
					so.upkeep = Requirements(d.Upkeep, d.Name);

				// Where and for whom.
				foreach (var s in d.SettlementTypes)
				{
					if (Enum.TryParse<ERegionLocality>(s, true, out var lt)) e.SettlementTypes.Add(lt);
					else Plugin.Logger.LogWarning($"{d.Name}: unknown settlement type \"{s}\" (use TOWN, FORT, PORT).");
				}
				if (e.SettlementTypes.Count == 0)
					foreach (var lt in templateTypes)
						e.SettlementTypes.Add(lt);
				foreach (var n in d.Nations)
				{
					if (Enum.TryParse<ENation>(n, true, out var nation)) e.Nations.Add(nation);
					else Plugin.Logger.LogWarning($"{d.Name}: unknown nation \"{n}\".");
				}
				if (d.Requires.Count > 0)
				{
					var req = new List<LocalityConstructionSettings>();
					foreach (var r in d.Requires)
					{
						var b = FindGameBuilding(cc, new List<string> { r }, out _);
						if (b != null) req.Add(b);
						else Plugin.Logger.LogWarning($"{d.Name}: required building \"{r}\" not found; ignored.");
					}
					so.preconditions = new Il2CppReferenceArray<LocalityConstructionSettings>(req.ToArray());
				}

				// Native effects (applied by the game while the building stands).
				var mods = new List<ModifierParameterData>();
				if (Plugin.Enabled.Value)
				{
					foreach (var m in d.Modifiers)
					{
						if (!Enum.TryParse<EModifier>(m.Modifier, true, out var mod))
						{
							Plugin.Logger.LogWarning($"{d.Name}: unknown modifier \"{m.Modifier}\"; see EModifier in docs/custom-buildings.md.");
							continue;
						}
						// durationDays 0 = permanent; nation -1 = whoever owns the settlement (CalculateConstructionEffects sets it too).
						var p = new ModifierParameterData(mod, m.Value, 0, (ENation)(-1), true);
						p.percent = m.Percent;
						mods.Add(p);
					}
				}
				so.effects = new Il2CppReferenceArray<ModifierParameterData>(mods.ToArray());

				Plugin.Logger.LogInfo($"Custom building {d.Name} ({d.Id}): {Summary(e)}");
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogError($"Could not set up custom building {d.Id}: {ex}");
			}
		}

		public static string Summary(Entry e)
		{
			var so = e.Asset;
			var sb = new StringBuilder();
			sb.Append(e.Template != null ? $"template {GameName(e.Template)}" : "no template");
			sb.Append($", settlements [{string.Join(", ", e.SettlementTypes)}]");
			sb.Append(e.Nations.Count == 0 ? ", all nations" : $", nations [{string.Join(", ", e.Nations)}]");
			sb.Append($", cost {Costs(so.cost)}, {so.pointsToConstruct:0.#} construction points");
			if (so.upkeep != null && so.upkeep.Length > 0) sb.Append($", upkeep {Costs(so.upkeep)}");
			if (e.Def.PopulationGrowthBonus != 0f) sb.Append($", population growth {e.Def.PopulationGrowthBonus:+0%;-0%}");
			if (e.Def.WeeklyWorkforce != 0) sb.Append($", workforce {e.Def.WeeklyWorkforce:+0;-0}/week");
			foreach (var m in e.Def.Modifiers) sb.Append($", {m.Modifier} {m.Value:+0.###;-0.###}");
			foreach (var m in e.Def.CountryModifiers)
				sb.Append(Enum.TryParse<EModifier>(m.Modifier, true, out _) ? $", nation {m.Modifier} {m.Value:+0.###;-0.###}" : $", UNKNOWN nation modifier {m.Modifier} (ignored)");
			if (e.Def.HasWeeklyConversion) sb.Append($", weekly {Conversion.Describe(e.Def)} -> +{e.Def.WeeklyRenown:0.#} renown");
			if (e.Def.MaxPerNation > 0) sb.Append($", max {e.Def.MaxPerNation} per nation");
			if (!Plugin.Enabled.Value) sb.Append(" [mod disabled: not buildable, no effects]");
			return sb.ToString();
		}

		public static string Costs(Il2CppReferenceArray<Requirement> reqs)
		{
			if (reqs == null || reqs.Length == 0) return "free";
			var parts = new List<string>();
			foreach (var r in reqs)
				if (r != null) parts.Add($"{r.amount:0.#} {r.type}");
			return string.Join(" + ", parts);
		}

		static Il2CppReferenceArray<Requirement> Requirements(List<CostEntry> list, string building)
		{
			var res = new List<Requirement>();
			foreach (var c in list)
			{
				if (!Enum.TryParse<EInventoryType>(c.Resource, true, out var type))
				{
					Plugin.Logger.LogWarning($"{building}: unknown resource \"{c.Resource}\" (use EInventoryType names: Money, ConstructionMaterial, Specialist, Wood, Iron, ...).");
					continue;
				}
				res.Add(new Requirement { type = type, amount = c.Amount });
			}
			return new Il2CppReferenceArray<Requirement>(res.ToArray());
		}

		static Il2CppReferenceArray<Requirement> Scaled(Il2CppReferenceArray<Requirement> src, float k)
		{
			if (src == null)
				return new Il2CppReferenceArray<Requirement>(0);
			var res = new Requirement[src.Length];
			for (int i = 0; i < src.Length; i++)
				res[i] = new Requirement { type = src[i].type, amount = Mathf.Round(src[i].amount * k) };
			return new Il2CppReferenceArray<Requirement>(res);
		}

		public static string GameName(LocalityConstructionSettings s)
		{
			string bt = Enum.IsDefined(typeof(ELocalityConstruction), s.buildingType) ? s.buildingType.ToString() : ((int)s.buildingType).ToString();
			return s.level > 0 ? $"{bt} (level {s.level})" : bt;
		}

		static bool Matches(LocalityConstructionSettings s, string want)
		{
			if (s == null || string.IsNullOrEmpty(want)) return false;
			return string.Equals(s.buildingType.ToString(), want, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(s.name, want, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(s.header, want, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(s.assetPath, want, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>First of the wanted names that exists (base level preferred), and the settlement types it is listed for.</summary>
		static LocalityConstructionSettings FindGameBuilding(ConstructionConfig cc, List<string> wanted, out List<ERegionLocality> types)
		{
			types = new List<ERegionLocality>();
			if (cc?.localityBuildings == null)
				return null;
			foreach (var want in wanted)
			{
				LocalityConstructionSettings best = null;
				for (int t = 0; t < cc.localityBuildings.Length; t++)
				{
					var list = cc.localityBuildings[t]?.constuctions;
					if (list == null) continue;
					for (int i = 0; i < list.Count; i++)
					{
						var s = list[i];
						if (s == null || Find(s.Pointer) != null || !Matches(s, want)) continue;
						if (best == null || s.level < best.level) best = s;
						if (!types.Contains((ERegionLocality)t)) types.Add((ERegionLocality)t);
					}
				}
				if (best != null)
					return best;
				types.Clear();
			}
			return null;
		}

		static LocalityConstructionSettings FirstGameBuilding(ConstructionConfig cc)
		{
			if (cc?.localityBuildings == null) return null;
			foreach (var lc in cc.localityBuildings)
			{
				var list = lc?.constuctions;
				if (list != null && list.Count > 0) return list[0];
			}
			return null;
		}

		/// <summary>Should this building be offered to a settlement of this type and owner?</summary>
		public static bool Offered(Entry e, ERegionLocality type, ENation nation)
		{
			if (!Plugin.Enabled.Value || e.Def.Placeholder || !e.Configured)
				return false;
			if (!e.SettlementTypes.Contains(type))
				return false;
			if (e.Nations.Count > 0 && !e.Nations.Contains(nation))
				return false;
			if (!e.Def.AiCanBuild && nation != RuntimeVars.playerNation)
				return false;
			return true;
		}
	}
}
