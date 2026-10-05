using System;
using HarmonyLib;
using World.UI.GeneralPage.RegimentManagement;
using PanelMode = World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel.PanelMode;

namespace UGARBulkRecruit
{
	internal static class Patches
	{
		internal static RegimentManagementPanel OpenPanel;

		[HarmonyPostfix]
		[HarmonyPatch(typeof(RegimentManagementPanel), nameof(RegimentManagementPanel.Show))]
		static void ShowPostfix(RegimentManagementPanel __instance, PanelMode __2)
		{
			OpenPanel = __instance;
			BestWeapon.Reset();
			if (__2 == PanelMode.Create && Plugin.ResetCopiesOnOpen.Value)
				BulkRecruit.Copies = 1;
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(RegimentManagementPanel), nameof(RegimentManagementPanel.UpdateChanges))]
		static void UpdateChangesPostfix(bool __result)
		{
			BulkRecruit.OnUpdateChanges(__result);
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(RegimentManagementPanel), nameof(RegimentManagementPanel.ApplyChanges))]
		static void ApplyChangesPrefix(RegimentManagementPanel __instance, out Design __state)
		{
			__state = null;
			if (!Plugin.Enabled.Value || BulkRecruit.Busy)
				return;
			try
			{
				if (__instance.mode != PanelMode.Create)
					return;
				var reg = __instance.regiment?.TryCast<World.RegimentInitData>();
				if (reg == null)
					return;
				var design = BulkRecruit.Capture(reg);
				// No new (unpaid) companies means nothing would be charged for a copy, so never duplicate it.
				if (Array.IndexOf(design.DirtyCompanies, true) < 0)
					return;
				design.Locality = __instance.Locality;
				design.Initial = __instance.initialRegiment;
				__state = design;
				BulkRecruit.BeginCapture();
				if (Plugin.VerboseLog.Value && BulkRecruit.Copies > 1)
					BulkRecruit.LogCostDiagnostics(__instance, reg, "first regiment, before the game's check");
			}
			catch (Exception e)
			{
				__state = null;
				Plugin.Logger.LogError($"Bulk recruit could not read the new regiment, falling back to a single one: {e}");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(RegimentManagementPanel), nameof(RegimentManagementPanel.ApplyChanges))]
		static void ApplyChangesPostfix(RegimentManagementPanel __instance, Design __state)
		{
			if (__state == null)
				return;
			try
			{
				// The first regiment only exists if the game's own affordability check passed.
				if (!BulkRecruit.EndCapture())
					return;
				BulkRecruit.Remember(__instance.initialRegiment, __state);
				int copies = Math.Min(BulkRecruit.Copies, Plugin.MaxCopies.Value);
				if (copies > 1)
					BulkRecruit.CreateCopies(__instance, __state, copies);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Bulk recruit failed after the first regiment: {e}");
			}
		}
	}
}
