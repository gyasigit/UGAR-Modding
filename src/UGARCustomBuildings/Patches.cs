using System;
using System.Collections.Generic;
using HarmonyLib;
using World;
using World.Configuration;
using World.SceneObject;

namespace UGARCustomBuildings
{
	internal static class Patches
	{
		// ---- Saves: answer Resources.Load for our asset paths (PathAsset.LoadAsset -> Resources.Load(path, type)). ----

		[HarmonyPrefix]
		[HarmonyPatch(typeof(UnityEngine.Resources), nameof(UnityEngine.Resources.Load), new[] { typeof(string), typeof(Il2CppSystem.Type) })]
		static bool ResourcesLoadPrefix(string __0, ref UnityEngine.Object __result)
		{
			if (__0 == null || !__0.StartsWith(Registry.PathPrefix, StringComparison.Ordinal))
				return true;
			try
			{
				Registry.EnsureReady();
				var e = Registry.GetOrCreate(__0.Substring(Registry.PathPrefix.Length));
				__result = e.Asset;
				return false;
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogError($"Could not load custom building {__0}: {ex}");
				return true;
			}
		}

		// ---- Build menu, AI and FixConstructions all read this list. ----

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ConstructionConfig), nameof(ConstructionConfig.GetBuildings))]
		static void GetBuildingsPostfix(ERegionLocality __0, ENation __1, Il2CppSystem.Collections.Generic.List<LocalityConstructionSettings> __result)
		{
			if (__result == null)
				return;
			try
			{
				if (!Registry.EnsureReady())
					return;
				foreach (var e in Registry.Entries)
				{
					if (Registry.Offered(e, __0, __1) && !__result.Contains(e.Asset))
						__result.Add(e.Asset);
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Custom buildings not added to the build list: {ex.Message}");
			}
		}

		// ---- The build menu (ConstructionSelectionPanel.Show) only lists buildings the player has "invented":
		// ResearchProjectManager.ContainsInvention(building), i.e. in its saved inventions list. Custom buildings
		// count as unlocked without being added to that list (nothing extra in the save). ----

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.ResearchProjectManager), nameof(World.ResearchProjectManager.ContainsInvention))]
		static void ContainsInventionPostfix(Invention __0, ref bool __result)
		{
			if (__result || __0 == null)
				return;
			var e = Registry.Find(__0.Pointer);
			if (e != null && Plugin.Enabled.Value && !e.Def.Placeholder)
				__result = true;
		}

		// ---- maxPerNation: the build menu and the build click both go through CanConstruct (Conversion.cs). ----

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ConstructionSettings), nameof(ConstructionSettings.CanConstruct))]
		static void CanConstructPostfix(ConstructionSettings __instance, ENation __0, ref bool __result)
		{
			if (!__result)
				return;
			var e = Registry.Find(__instance.Pointer);
			if (e == null || e.Def.MaxPerNation <= 0)
				return;
			try
			{
				if (Conversion.CountOwned(e, __0) >= e.Def.MaxPerNation)
					__result = false;
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Custom building limit check skipped: {ex.Message}");
			}
		}

		// ---- Territory statistics index an array by building type; give it the template's type. ----

		[HarmonyPostfix]
		[HarmonyPatch(typeof(Construction), nameof(Construction.BuildingType), MethodType.Getter)]
		static void BuildingTypePostfix(ref ELocalityConstruction __result)
		{
			if ((int)__result < (int)ELocalityConstruction.Max)
				return;
			var e = Registry.FindByType((int)__result);
			__result = e != null ? e.TemplateType : ELocalityConstruction.None;
		}

		// ---- Names and texts (the asset has no localization term, so these just make sure our text wins). ----

		[HarmonyPostfix]
		[HarmonyPatch(typeof(Invention), nameof(Invention.Header), MethodType.Getter)]
		static void HeaderPostfix(Invention __instance, ref string __result)
		{
			var e = Registry.Find(__instance.Pointer);
			if (e != null)
				__result = e.Def.Name;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(Invention), nameof(Invention.Description), MethodType.Getter)]
		static void DescriptionPostfix(Invention __instance, ref string __result)
		{
			var e = Registry.Find(__instance.Pointer);
			if (e != null)
				__result = e.Def.Description;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(LocalityConstructionSettings), nameof(LocalityConstructionSettings.GetDescription))]
		static void GetDescriptionPostfix(LocalityConstructionSettings __instance, ref string __result)
		{
			var e = Registry.Find(__instance.Pointer);
			if (e != null)
				__result = e.Def.Description;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(LocalityConstructionSettings), nameof(LocalityConstructionSettings.GetEffectsInfo))]
		static void GetEffectsInfoPostfix(LocalityConstructionSettings __instance, RegionLocality __0, ref string __result)
		{
			var e = Registry.Find(__instance.Pointer);
			if (e == null || !Plugin.Enabled.Value)
				return;
			try
			{
				var lines = Texts.CustomEffectLines(e.Def);
				string status = Conversion.Status(e, __0);
				if (status != null)
					lines.Add(status);
				if (lines.Count == 0)
					return;
				string extra = string.Join("\n", lines);
				__result = string.IsNullOrEmpty(__result) ? extra : __result.TrimEnd('\n') + "\n" + extra;
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Custom building effect text skipped: {ex.Message}");
			}
		}

		// ---- Nation-wide effects (CountryEffects.cs). ----

		[HarmonyPostfix]
		[HarmonyPatch(typeof(World.Modifiers.ModifiersManager), nameof(World.Modifiers.ModifiersManager.Calculate))]
		static void ModifiersCalculatePostfix(World.Modifiers.ModifiersManager __instance)
		{
			try
			{
				CountryEffects.OnCalculated(__instance);
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Custom building nation effect skipped: {ex.Message}");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(RegionLocality), nameof(RegionLocality.CalculateConstructionEffects))]
		static void ConstructionEffectsPostfix()
		{
			CountryEffects.MarkDirty();
			Conversion.Invalidate();
		}

		// ---- Weekly population growth bonus. ----

		[HarmonyPrefix]
		[HarmonyPatch(typeof(Region), nameof(Region.CalculatePopulationGrows))]
		static void PopulationGrowsPrefix(Region __instance, out List<(RegionLocality, GrowthBonus)> __state)
		{
			__state = null;
			if (!Plugin.Enabled.Value)
				return;
			try
			{
				var locs = __instance.localities;
				for (int i = 0; locs != null && i < locs.Count; i++)
				{
					var l = locs[i];
					Conversion.Weekly(l);
					var b = Growth.Weekly(l); // from the population before this week's growth, like the game
					if (b.Sources != null)
						(__state ??= new List<(RegionLocality, GrowthBonus)>()).Add((l, b));
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Custom building growth skipped: {ex.Message}");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(Region), nameof(Region.CalculatePopulationGrows))]
		static void PopulationGrowsPostfix(List<(RegionLocality, GrowthBonus)> __state)
		{
			if (__state == null)
				return;
			var d = RuntimeVars.date;
			string date = $"{d.Year:0000}-{d.Month:00}-{d.Day:00}";
			foreach (var (l, b) in __state)
			{
				try
				{
					Growth.Apply(l, b);
					if (Plugin.LogGrowth.Value)
						Plugin.Logger.LogInfo($"Custom buildings {date} {l.Name}: +{b.Population} population, +{b.Workforce} workforce ({string.Join(", ", b.Sources)}).");
				}
				catch (Exception ex)
				{
					Plugin.Logger.LogWarning($"Custom building growth failed at {l?.Name}: {ex.Message}");
				}
			}
		}
	}
}
