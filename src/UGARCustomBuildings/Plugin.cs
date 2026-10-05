// UGAR Custom Buildings: adds new settlement buildings defined in *.building.json files. They are real game
// buildings (LocalityConstructionSettings assets), so they appear in the game's own build menu, cost resources,
// take construction time, apply their effects through the game's modifier system and are saved with the campaign.
// See Registry.cs for the game's rules and docs/custom-buildings.md for the file format.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UGARCustomBuildings
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.custombuildings";
		public const string Name = "UGAR Custom Buildings";
		public const string Version = "1.3.0";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<bool> LogGrowth;
		internal static ConfigEntry<bool> DumpGameBuildings;
		internal static ConfigEntry<bool> NotifySkipped;
		internal static ConfigEntry<bool> NotifyNextWeek;
		internal static ConfigEntry<bool> NotifyHeld;

		public override void Load()
		{
			Logger = Log;
			Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
				"Offer custom buildings and apply their effects. When off, saves that contain them still load, but they have no " +
				"effect, can't be built, and the game may remove and refund them.", null, "RestartRequired"));
			LogGrowth = Config.Bind("Debug", "LogGrowth", true, "Log each week's extra population and workforce from custom buildings to BepInEx\\LogOutput.log.");
			DumpGameBuildings = Config.Bind("Debug", "DumpGameBuildings", true,
				"At game start, write the game's own settlement buildings (costs, build points, effects) to BepInEx\\config\\ugar.custombuildings.game-buildings.txt.");

			NotifySkipped = Config.Bind("Notifications", "ReceptionSkipped", true,
				"Game notification when a weekly reception (Governor's Assembly Rooms, Coffeehouse) is skipped for missing goods.");
			NotifyNextWeek = Config.Bind("Notifications", "ShortNextWeek", true,
				"Game notification right after a reception when the goods left won't cover next week's.");
			NotifyHeld = Config.Bind("Notifications", "ReceptionHeld", false,
				"Game notification every time a reception is held (with the renown gained).");

			Registry.SetDefinitions(BuildingDefinition.LoadAll(Paths.PluginPath, msg => Log.LogWarning(msg)));

			ClassInjector.RegisterTypeInIl2Cpp<Starter>();
			AddComponent<Starter>();
			new Harmony(Guid).PatchAll(typeof(Patches));
			Log.LogInfo($"{Name} {Version} loaded with {Registry.DefinitionCount} building definition(s){(Enabled.Value ? "" : " (disabled)")}.");
		}
	}

	/// <summary>Sets the buildings up as soon as the game config exists (before any save is loaded), then keeps the
	/// nation-wide effects in sync.</summary>
	public class Starter : MonoBehaviour
	{
		public Starter(IntPtr ptr) : base(ptr) { }

		float _next;
		bool _ready;

		void Update()
		{
			if (_ready)
			{
				CountryEffects.Tick();
				return;
			}
			if (Time.realtimeSinceStartup < _next)
				return;
			_next = Time.realtimeSinceStartup + 1f;
			try
			{
				_ready = Registry.EnsureReady();
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Custom buildings setup failed: {e}");
				enabled = false;
			}
		}
	}
}
