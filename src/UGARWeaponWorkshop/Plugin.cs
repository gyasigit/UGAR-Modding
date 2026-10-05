// UGAR Weapon Workshop: design your own muskets and rifles from the gun technology you have unlocked, then improve
// them as Mk II, Mk III... Each design is a real game weapon (a WeaponTemplate copied from its base weapon): factories
// make it, companies carry it, battles use its numbers, and the save stores it by a path that encodes the whole
// design. See Registry.cs for the game's rules and docs/weapon-workshop.md for the player guide.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UGARWeaponWorkshop
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.weaponworkshop";
		public const string Name = "UGAR Weapon Workshop";
		public const string Version = "1.1.4";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<string> Hotkey;
		internal static ConfigEntry<float> DevelopmentCost;
		internal static ConfigEntry<float> UiScale;

		public override void Load()
		{
			Logger = Log;
			Enabled = Config.Bind("General", "Enabled", true,
				"Show the Weapon Workshop (WORKSHOP tab under MUSKETS/CANNONS/SHIPS/SUPPLY in the production screen, and the hotkey). When off you can't make new designs, " +
				"but designs already in a save keep working: factories still make them and units still carry them.");
			Hotkey = Config.Bind("General", "Hotkey", "F10", "Key that opens and closes the Weapon Workshop on the campaign map (a Unity KeyCode name, e.g. F10, F6, W).");
			DevelopmentCost = Config.Bind("Balance", "DevelopmentCostMultiplier", 1f, new ConfigDescription(
				"Multiplier on the one-time money cost of developing a design (base: 400 + 30 x the gun's price; Mk II pays 75%). 0 = free.",
				new AcceptableValueRange<float>(0f, 5f)));
			UiScale = Config.Bind("Window", "Scale", 1f, new ConfigDescription("Size multiplier for the workshop window.", new AcceptableValueRange<float>(0.5f, 3f)));

			ClassInjector.RegisterTypeInIl2Cpp<WorkshopPanel>();
			AddComponent<WorkshopPanel>();
			new Harmony(Guid).PatchAll(typeof(Patches));
			Log.LogInfo($"{Name} {Version} loaded{(Enabled.Value ? "" : " (workshop hidden; saved designs still load)")}.");
		}

		internal static KeyCode HotkeyCode()
		{
			return Enum.TryParse<KeyCode>(Hotkey.Value?.Trim(), true, out var k) ? k : KeyCode.F10;
		}
	}
}
