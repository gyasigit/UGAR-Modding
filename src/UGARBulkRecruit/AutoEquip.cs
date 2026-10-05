// Equip each newly added company with the best weapon actually in stock.
//
// Why here: a new company is built from its template with the template's default weapon (e.g. Dragoon Carbine '57),
// whether or not any are in the arsenal. The WeaponSlot.Company hook only runs when the player opens the weapon
// dropdown, so it never fired for companies added straight away. RegimentManagementPanel.ApplyAddCompany is the one
// step every added company goes through (it clones the picker's company into the selected slot, then refreshes the
// cost display), so we fix the weapon right after it, unless the player picked a weapon in the picker by hand.
using System;
using System.Text;
using HarmonyLib;
using World.UI.GeneralPage.RegimentManagement;

namespace UGARBulkRecruit
{
	internal static class AutoEquip
	{
		static bool _playerPicked;

		public static void OnPlayerPicked() => _playerPicked = true;

		/// <summary>
		/// Best weapon from <paramref name="allowed"/> of the same kind as <paramref name="current"/> (musket vs cannon)
		/// with enough left for <paramref name="hp"/> soldiers after what other new companies already take.
		/// Null keeps the current one.
		/// </summary>
		static WeaponTemplate PickFrom(WeaponTemplate current, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<WeaponTemplate> allowed,
			float hp, World.ItemStorage storage, Func<WeaponTemplate, float> reserved, StringBuilder log)
		{
			if (current == null || allowed == null)
				return null;
			float Avail(WeaponTemplate w) => storage.GetItemCount(w) - reserved(w);
			bool currentOk = Avail(current) >= current.CountForHP(hp);
			log?.Append($"current {current.Name} need {current.CountForHP(hp)} have {Avail(current):0}{(currentOk ? "" : " (short)")}");
			WeaponTemplate best = null;
			float bestScore = currentOk ? Game.Score(current) : float.MinValue;
			for (int i = 0; i < allowed.Length; i++)
			{
				var w = allowed[i];
				if (w == null || w.Pointer == current.Pointer || w.inventoryType != current.inventoryType)
					continue;
				float have = Avail(w);
				int need = w.CountForHP(hp);
				log?.Append($"\n  {w.Name}: price {w.price}, eff {w.Efficiency:0.#}, need {need}, have {have:0}");
				if (have < need)
					continue;
				float s = Game.Score(w);
				if (s > bestScore)
				{
					bestScore = s;
					best = w;
				}
			}
			log?.Append(best == null ? "\n  -> keeping current" : $"\n  -> {best.Name}");
			return best;
		}

		/// <summary>Weapons already claimed by the other new (unpaid) companies of the regiment being built.</summary>
		static Func<WeaponTemplate, float> ReservedBy(RegimentManagementPanel panel, IntPtr except)
		{
			var used = new System.Collections.Generic.Dictionary<IntPtr, float>();
			try
			{
				var comps = panel?.regiment?.TryCast<World.RegimentInitData>()?.companies;
				for (int i = 0; comps != null && i < comps.Length; i++)
				{
					var o = comps[i];
					if (o == null || !o.dirty || o.Pointer == except)
						continue;
					void Add(WeaponTemplate w, float hp)
					{
						if (w != null)
							used[w.Pointer] = (used.TryGetValue(w.Pointer, out var u) ? u : 0f) + w.CountForHP(hp);
					}
					Add(o.Weapon, o.IntHP);
					var crew = o.TryCast<Fight.CrewBrigadeModelInitData>();
					if (crew != null)
						Add(crew.crewWeapon, crew.crewHp);
				}
			}
			catch (Exception) { }
			return w => used.TryGetValue(w.Pointer, out var v) ? v : 0f;
		}

		/// <summary>Equips the company's main weapon (musket or cannon) and, for artillery, its crew's musket.</summary>
		static bool Equip(Fight.BrigadeModelInitData c, World.ItemStorage storage, Func<WeaponTemplate, float> reserved, bool keepMain, StringBuilder log, bool onlyIfShort = false)
		{
			bool changed = false;
			bool Short(WeaponTemplate w, float hp, Func<WeaponTemplate, float> res) => w != null && storage.GetItemCount(w) - res(w) < w.CountForHP(hp);
			if (!keepMain && onlyIfShort && !Short(c.Weapon, c.IntHP, reserved))
			{
				keepMain = true;
				log.Append($"\n main: kept {c.Weapon?.Name} (in stock)");
			}
			if (!keepMain)
			{
				log.Append("\n main: ");
				var best = PickFrom(c.Weapon, c.entity?.availableWeapons, c.IntHP, storage, reserved, log);
				if (best != null)
				{
					c.Weapon = best;
					changed = true;
				}
			}
			var crew = c.TryCast<Fight.CrewBrigadeModelInitData>();
			if (crew != null && crew.crewWeapon != null)
			{
				// Gun crews carry their own muskets (UpdateChanges counts them via ICrewBrigadeDataProvider.CrewWeapon).
				var mainNow = c.Weapon;
				Func<WeaponTemplate, float> res = w => reserved(w) + (mainNow != null && w.Pointer == mainNow.Pointer ? mainNow.CountForHP(c.IntHP) : 0f);
				if (onlyIfShort && !Short(crew.crewWeapon, crew.crewHp, res))
				{
					log.Append($"\n crew: kept {crew.crewWeapon.Name} (in stock)");
					return changed;
				}
				log.Append("\n crew: ");
				var bestCrew = PickFrom(crew.crewWeapon, crew.crewEntity?.availableWeapons, crew.crewHp, storage, res, log);
				if (bestCrew != null)
				{
					crew.crewWeapon = bestCrew;
					changed = true;
				}
			}
			return changed;
		}

		public static void EquipPreview(CompanyCreatePanel picker)
		{
			_playerPicked = false; // a new company type was chosen: nothing picked by hand for it yet
			if (!Plugin.Enabled.Value || !Plugin.AutoBestWeapon.Value)
				return;
			var element = picker.companyCreateElement?.companyElement;
			var c = element?.company?.TryCast<Fight.BrigadeModelInitData>();
			var storage = Game.PlayerInventory()?.itemStorage;
			if (c == null || storage == null)
				return;
			var log = new StringBuilder("Auto weapon for picker preview:");
			bool changed = Equip(c, storage, ReservedBy(Patches.OpenPanel, IntPtr.Zero), false, log);
			Plugin.Logger.LogInfo(log.ToString());
			if (!changed)
				return;
			element.UpdateContent();
			picker.weaponSlot?.UpdateContent();
		}

		public static void AfterAdd(RegimentManagementPanel panel, int index)
		{
			bool picked = _playerPicked;
			_playerPicked = false;
			if (!Plugin.Enabled.Value || !Plugin.AutoBestWeapon.Value || index < 0)
				return;
			var reg = panel.regiment?.TryCast<World.RegimentInitData>();
			var comps = reg?.companies;
			if (comps == null || index >= comps.Length)
				return;
			var c = comps[index];
			var storage = Game.PlayerInventory()?.itemStorage;
			if (c == null || storage == null || !c.dirty)
				return;
			var reserved = ReservedBy(panel, c.Pointer);
			var log = new StringBuilder($"Auto weapon for new company {index}:");
			// Respect a weapon chosen in the picker, unless there isn't enough of it to hand out.
			bool keepMain = false;
			if (picked)
			{
				var w = c.Weapon;
				keepMain = w == null || storage.GetItemCount(w) - reserved(w) >= w.CountForHP(c.IntHP);
				if (keepMain)
					log.Append($"\n main: kept {w?.Name} (picked by the player)");
			}
			// The picker preview already chose the best weapon and the player may have changed it there, so after
			// adding we only step in when the company's weapon can't actually be supplied (e.g. the cheaper HP need
			// after adding, or another company took the stock). Never upgrade over the player's choice.
			bool changed = Equip(c, storage, reserved, keepMain, log, onlyIfShort: true);
			Plugin.Logger.LogInfo(log.ToString());
			if (!changed)
				return;
			reg.UpdateWeapon();
			World.Extensions.UpdateRegimentAttributes(panel.regiment);
			try
			{
				panel.regimentInfoPanel?.UpdateContent();
				panel.companySelectPanel?.UpdateContent();
				panel.UpdateChanges();
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Auto weapon: display refresh failed: {e.Message}");
			}
		}
	}

	internal static class AutoEquipPatches
	{
		[HarmonyPrefix]
		[HarmonyPatch(typeof(RegimentManagementPanel), nameof(RegimentManagementPanel.ApplyAddCompany))]
		static void ApplyAddCompanyPrefix(RegimentManagementPanel __instance, out int __state)
		{
			__state = -1;
			try
			{
				var sel = __instance.companyCreatePanel?.companySelectElement;
				if (sel != null)
					__state = sel.index;
			}
			catch (Exception) { }
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(RegimentManagementPanel), nameof(RegimentManagementPanel.ApplyAddCompany))]
		static void ApplyAddCompanyPostfix(RegimentManagementPanel __instance, int __state)
		{
			try
			{
				AutoEquip.AfterAdd(__instance, __state);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Auto weapon failed, keeping the game's default: {e}");
			}
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(CompanyCreatePanel), nameof(CompanyCreatePanel.OnWeaponSelect))]
		static void OnWeaponSelectPrefix() => AutoEquip.OnPlayerPicked();

		// The "add company" picker: choosing a company type shows a preview company (template weapon) in
		// CompanyCreatePanel.set_CompanyCreateElement. Equip the preview too, so the picker already shows the
		// weapon that will be used and the player can still change it before pressing Add.
		[HarmonyPostfix]
		[HarmonyPatch(typeof(CompanyCreatePanel), nameof(CompanyCreatePanel.CompanyCreateElement), MethodType.Setter)]
		static void SetCompanyCreateElementPostfix(CompanyCreatePanel __instance)
		{
			try
			{
				AutoEquip.EquipPreview(__instance);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Auto weapon (picker preview) failed: {e}");
			}
		}
	}
}
