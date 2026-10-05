using System;
using System.Collections.Generic;
using HarmonyLib;
using World;
using World.UI.GeneralPage.Production;
using World.UI.GeneralPage.RegimentManagement;
using ItemList = World.UI.GeneralPage.Production.NewOrder.ItemList;
using Filters = World.UI.GeneralPage.Production.NewOrder.Filters;

namespace UGARWeaponWorkshop
{
	internal static class Patches
	{
		/// <summary>The production screen while it is open (the workshop tab sits in its New Order filters).</summary>
		public static ProductionPanel OpenProduction;
		public static Filters OpenFilters;

		// ---- Saves: answer Resources.Load for our paths. Resources.Load<T>(path) and PathAsset.LoadAsset both end up here. ----
		// Runs even when the mod is switched off, so saves with designs keep loading.

		[HarmonyPrefix]
		[HarmonyPatch(typeof(UnityEngine.Resources), nameof(UnityEngine.Resources.Load), new[] { typeof(string), typeof(Il2CppSystem.Type) })]
		static bool ResourcesLoadPrefix(string __0, ref UnityEngine.Object __result)
		{
			if (__0 == null || !__0.StartsWith("UGARMods/Weapon", StringComparison.Ordinal))
				return true;
			try
			{
				if (__0.StartsWith(Registry.WeaponPrefix, StringComparison.Ordinal))
				{
					__result = Registry.GetOrCreate(__0.Substring(Registry.WeaponPrefix.Length)).Weapon;
					return false;
				}
				if (__0.StartsWith(Registry.InventionPrefix, StringComparison.Ordinal))
				{
					__result = Registry.InventionFromPath(__0.Substring(Registry.InventionPrefix.Length));
					return false;
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogError($"Could not load workshop weapon {__0}: {ex}");
			}
			return true;
		}

		// ---- Names: the designs' I2 terms ("UGARWW/<id>/Name") aren't in the game's language sources. ----

		[HarmonyPrefix]
		[HarmonyPatch(typeof(I2.Loc.LocalizationManager), nameof(I2.Loc.LocalizationManager.TryGetTranslation))]
		static bool TryGetTranslationPrefix(string __0, ref string __1, ref bool __result)
		{
			if (__0 == null || !__0.StartsWith(Registry.TermPrefix, StringComparison.Ordinal) || !Registry.TryTerm(__0, out var text))
				return true;
			__1 = text;
			__result = true;
			return false;
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(I2.Loc.LocalizationManager), nameof(I2.Loc.LocalizationManager.GetTranslation))]
		static bool GetTranslationPrefix(string __0, ref string __result)
		{
			if (__0 == null || !__0.StartsWith(Registry.TermPrefix, StringComparison.Ordinal) || !Registry.TryTerm(__0, out var text))
				return true;
			__result = text;
			return false;
		}

		// WeaponTemplate.Title shares its native code with Name (identical-code folding, same native
		// address), so the Name patch covers both; patching Title too would detour it twice.
		[HarmonyPostfix]
		[HarmonyPatch(typeof(WeaponTemplate), nameof(WeaponTemplate.Name), MethodType.Getter)]
		static void NamePostfix(WeaponTemplate __instance, ref string __result)
		{
			var e = Registry.FindWeapon(__instance.Pointer);
			if (e != null) __result = e.DisplayName;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(WeaponTemplate), nameof(WeaponTemplate.Description), MethodType.Getter)]
		static void DescriptionPostfix(WeaponTemplate __instance, ref string __result)
		{
			var e = Registry.FindWeapon(__instance.Pointer);
			if (e != null) __result = e.Description;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(Invention), nameof(Invention.Header), MethodType.Getter)]
		static void HeaderPostfix(Invention __instance, ref string __result)
		{
			var s = Registry.FindInvention(__instance.Pointer);
			if (s != null) __result = string.IsNullOrEmpty(s.Name) ? s.E.D.Name : s.Name;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(Invention), nameof(Invention.Description), MethodType.Getter)]
		static void InventionDescriptionPostfix(Invention __instance, ref string __result)
		{
			var s = Registry.FindInvention(__instance.Pointer);
			if (s != null) __result = s.E.Description;
		}

		// ---- New Order list: it offers the item of every ItemInvention in the research list. Archived designs keep
		// their (saved) entry there, so take them out while the list is built and put them back afterwards. ----

		[HarmonyPrefix]
		[HarmonyPatch(typeof(ItemList), nameof(ItemList.UpdateContent))]
		static void ItemListUpdatePrefix(out List<(int, Invention)> __state)
		{
			__state = null;
			try
			{
				var list = Registry.PlayerCountry()?.researchProjectManager?.inventions;
				for (int i = list == null ? -1 : list.Count - 1; i >= 0; i--)
				{
					var it = list[i];
					var s = it == null ? null : Registry.FindInvention(it.Pointer);
					if (s != null && s.Archived)
					{
						(__state ??= new List<(int, Invention)>()).Add((i, it));
						list.RemoveAt(i);
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogWarning($"Hiding archived designs from the order list failed: {ex.Message}");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ItemList), nameof(ItemList.UpdateContent))]
		static void ItemListUpdatePostfix(List<(int, Invention)> __state)
		{
			if (__state == null)
				return;
			try
			{
				var list = Registry.PlayerCountry()?.researchProjectManager?.inventions;
				if (list == null)
					return;
				for (int k = __state.Count - 1; k >= 0; k--) // removed from the back, so re-insert from the front
				{
					var (i, it) = __state[k];
					list.Insert(Math.Min(i, list.Count), it);
				}
			}
			catch (Exception ex)
			{
				Plugin.Logger.LogError($"Could not put archived designs back in the research list: {ex}");
			}
		}

		// ---- Carry lists: unit prefabs may load after a design was registered; catch up when a weapon picker opens. ----

		[HarmonyPrefix]
		[HarmonyPatch(typeof(WeaponSlot), nameof(WeaponSlot.Company))]
		static void WeaponSlotCompanyPrefix()
		{
			try
			{
				Registry.UpdateCarryLists();
			}
			catch (Exception) { }
		}

		// ---- Production screen open/closed (the workshop tab sits in its New Order filters). ----

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ProductionPanel), nameof(ProductionPanel.Show))]
		static void ProductionShowPostfix(ProductionPanel __instance) => OpenProduction = __instance;

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ProductionPanel), nameof(ProductionPanel.Hide))]
		static void ProductionHidePostfix() => OpenProduction = null;

		// Don't patch Filters.Show: its native code is an empty "ret" that identical-code folding shares with every
		// empty method in the game, so a patch there runs for all of them (1.1.0 crashed the game at start-up).
		// WorkshopPanel.OpenFilters reads ProductionPanel.filters instead.
	}
}
