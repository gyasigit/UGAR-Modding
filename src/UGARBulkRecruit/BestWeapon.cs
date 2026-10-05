// Auto-equip the best weapon in stock on new companies.
//
// In the game, a new company keeps its template's default weapon (often the civilian rifle).
// WeaponSlot.Company(company, createMode) only fills the dropdown with weapons in storage and blocks
// the ones this unit type can't use. Picking from the list calls WeaponSlot.OnElementClick, which
// fires the slot's onWeaponSelect callback (CompanyCreatePanel.OnWeaponSelect). We make the same call
// for the best usable weapon, so the result is exactly as if the player had clicked it.
using System;
using System.Collections.Generic;
using HarmonyLib;
using World.UI.GeneralPage.RegimentManagement;
using WeaponElement = World.UI.GeneralPage.RegimentManagement.WeaponState.WeaponElement;

namespace UGARBulkRecruit
{
	internal static class BestWeapon
	{
		static bool _selecting;
		// Companies we already equipped, so a later refresh never overrides the player's own pick.
		static readonly HashSet<IntPtr> _handled = new HashSet<IntPtr>();
		// Slots where the player picked a weapon by hand, with the unit type they picked it for.
		static readonly Dictionary<IntPtr, IntPtr> _manual = new Dictionary<IntPtr, IntPtr>();

		public static void Reset()
		{
			_handled.Clear();
			_manual.Clear();
		}

		static IntPtr UnitType(Fight.IFightUnitDataProvider company)
		{
			var e = company?.TryCast<Fight.BrigadeModelInitData>()?.entity;
			return e == null ? IntPtr.Zero : e.Pointer;
		}

		public static void OnPlayerClick(WeaponSlot slot)
		{
			if (!_selecting)
				_manual[slot.Pointer] = UnitType(slot.company);
		}

		static World.ItemStorage PlayerStorage()
		{
			var scene = MonoBehaviourSingleton<World.SceneManager>.instance;
			var country = scene?.PlayerCountry;
			if (country == null)
				return null;
			var inv = country.IsEuropeanAction ? country.europeanManager?.inventory : country.inventory;
			return inv?.itemStorage;
		}

		static float Score(WeaponTemplate w)
		{
			// Efficiency is the overall rating the weapon list shows as a bar; price breaks ties.
			// Price is how the designers rank weapon quality (newer/better patterns cost more);
			// efficiency breaks ties. Logged per weapon so the ranking can be checked against the game.
			return w.price * 1000f + w.Efficiency;
		}

		static string Describe(WeaponTemplate w, float hp, World.ItemStorage storage)
		{
			if (w == null)
				return "none";
			return $"{w.Name} (price {w.price}, eff {w.Efficiency:0.##}, dmg {w.damage:0.##}, reload {w.baseReload:0.##}, need {w.CountForHP(hp)}, stock {storage.GetItemCount(w):0})";
		}

		public static void Apply(WeaponSlot slot, Fight.IFightUnitDataProvider company)
		{
			if (_selecting || company == null)
				return;
			if (!_handled.Add(company.Pointer))
				return;
			if (_manual.TryGetValue(slot.Pointer, out var type) && type == UnitType(company))
				return;

			var brigade = company.TryCast<Fight.BrigadeModelInitData>();
			var storage = PlayerStorage();
			var elements = slot.availableWeaponElements;
			if (brigade == null || storage == null || elements == null)
				return;

			var current = brigade.Weapon;
			float hp = brigade.IntHP;
			WeaponElement best = null;
			float bestScore = current != null && storage.IsEnough(current, current.CountForHP(hp)) ? Score(current) : float.MinValue;
			var log = new System.Text.StringBuilder();
			log.Append($"Weapon pick for new company (hp {hp}, current {Describe(current, hp, storage)}):");

			for (int i = 0; i < elements.Count; i++)
			{
				var el = elements[i];
				if (el == null || !el.gameObject.activeSelf)
					continue;
				var w = el.weapon;
				bool blocked = el.block != null && el.block.activeSelf;
				log.Append($"\n  {Describe(w, hp, storage)}{(blocked ? " [blocked for this unit]" : "")}");
				if (blocked)
					continue; // this unit type can't carry it
				if (w == null || !storage.IsEnough(w, w.CountForHP(hp)))
					continue; // not enough in stock for a full company
				float s = Score(w);
				if (s > bestScore)
				{
					bestScore = s;
					best = el;
				}
			}

			log.Append(best == null ? "\n  -> keeping current" : $"\n  -> {best.weapon.Name}");
			Plugin.Logger.LogInfo(log.ToString());
			if (best == null)
				return;
			_selecting = true;
			try
			{
				slot.OnElementClick(best);
				Plugin.Logger.LogInfo($"Equipped new company with {best.weapon.Name} instead of {(current != null ? current.Name : "nothing")}");
			}
			finally
			{
				_selecting = false;
			}
		}
	}

	internal static class BestWeaponPatches
	{
		[HarmonyPostfix]
		[HarmonyPatch(typeof(WeaponSlot), nameof(WeaponSlot.Company))]
		static void CompanyPostfix(WeaponSlot __instance, Fight.IFightUnitDataProvider __0, bool __1)
		{
			if (Plugin.VerboseLog.Value)
				Plugin.Logger.LogInfo($"WeaponSlot.Company called, createMode={__1}");
			if (!Plugin.Enabled.Value || !Plugin.AutoBestWeapon.Value)
				return;
			// createMode is the "add company" picker. The regiment's own weapon slots (CompanyWeaponPanel)
			// pass false, so also accept those while creating a regiment, but only for companies that are
			// new (dirty) and not yet paid for.
			if (!__1)
			{
				var panel = Patches.OpenPanel;
				var brigade = __0?.TryCast<Fight.BrigadeModelInitData>();
				if (panel == null || panel.mode != World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel.PanelMode.Create
					|| brigade == null || !brigade.dirty)
					return;
			}
			try
			{
				BestWeapon.Apply(__instance, __0);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Best weapon pick failed, keeping the game's default: {e}");
			}
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(WeaponSlot), nameof(WeaponSlot.OnElementClick))]
		static void OnElementClickPrefix(WeaponSlot __instance)
		{
			try
			{
				BestWeapon.OnPlayerClick(__instance);
			}
			catch (Exception) { }
		}
	}
}
