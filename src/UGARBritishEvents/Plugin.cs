// UGAR British Events: campaign events defined in *.event.json files. When an event's trigger holds (date window,
// settlements owned, other events paid, rentals in use, weekly chance) it appears in the game's own event window, asks
// a random price (money, supplies, specialists) and, if the player pays, its effects apply: more factories / shipyards that Britain can rent in the England screen, and optional permanent
// country modifiers. Fired events are remembered in the campaign save. See Events.cs, RentCaps.cs, SaveMarkers.cs and
// docs/british-events.md.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UGARBritishEvents
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.britishevents";
		public const string Name = "UGAR British Events";
		public const string Version = "1.3.0";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<bool> LogChecks;
		internal static ConfigEntry<bool> WriteInfo;
		internal static ConfigEntry<string> ForceEvent;

		public override void Load()
		{
			Logger = Log;
			Enabled = Config.Bind("General", "Enabled", true,
				"Fire the mod's events and apply the extra factory/shipyard caps of events that already fired. When off, nothing fires " +
				"and the caps are the game's own (rentals above the cap are kept until you lower them).");
			LogChecks = Config.Bind("Debug", "LogChecks", false,
				"Log each day why an event hasn't fired yet to BepInEx\\LogOutput.log.");
			WriteInfo = Config.Bind("Debug", "WriteInfo", true,
				"When a campaign loads, write the rent caps, settlement names and game event pictures to BepInEx\\config\\ugar.britishevents.info.txt (for writing events).");
			ForceEvent = Config.Bind("Debug", "ForceEvent", "",
				"Testing: the id of an event to fire on the next in-game day, ignoring its trigger and whether it already fired. Cleared after use.");

			var defs = EventDefinition.LoadAll(Paths.PluginPath, msg => Log.LogWarning(msg));
			BindEventSettings(defs);
			Events.SetDefinitions(defs);

			ClassInjector.RegisterTypeInIl2Cpp<Starter>();
			AddComponent<Starter>();
			var harmony = new Harmony(Guid);
			harmony.CreateClassProcessor(typeof(RentCaps.FactoriesPatch)).Patch();
			harmony.CreateClassProcessor(typeof(RentCaps.ShipyardsPatch)).Patch();
			harmony.CreateClassProcessor(typeof(Choice.CanBeExecutedPatch)).Patch();
			harmony.CreateClassProcessor(typeof(Choice.ExecutePatch)).Patch();
			Log.LogInfo($"{Name} {Version} loaded with {Events.DefinitionCount} event(s){(Enabled.Value ? "" : " (disabled)")}.");
		}

		/// <summary>One Repeatable switch and one repeat interval per loaded event, shown in the F8 mod manager. The json
		/// values are the defaults; once saved in the cfg the setting wins. Read live, so changes apply at once.</summary>
		void BindEventSettings(System.Collections.Generic.List<EventDefinition> defs)
		{
			foreach (var d in defs)
			{
				try
				{
					// Keys can't hold quotes or brackets, so the id is the key and the title goes in the description.
					d.RepeatableSetting = Config.Bind("Repeatable events", d.Id, d.Repeatable,
						$"{d.Title}: on = it can fire again after the interval below, and paying again stacks its effects " +
						"(caps included). Off = once per campaign.");
					d.RepeatAfterDaysSetting = Config.Bind("Repeatable events", d.Id + " days", d.RepeatAfterDays,
						new ConfigDescription($"{d.Title}: in-game days after it last fired before it can fire again (when repeatable).",
							new AcceptableValueRange<int>(7, 3650)));
					d.RepeatableSetting.SettingChanged += (_, _) => Events.InvalidateBonus();
				}
				catch (Exception e)
				{
					Log.LogWarning($"No Repeatable setting for event \"{d.Id}\": {e.Message}");
				}
			}
		}
	}

	public class Starter : MonoBehaviour
	{
		public Starter(IntPtr ptr) : base(ptr) { }

		float _nextError;

		void Update()
		{
			try
			{
				Events.Tick();
			}
			catch (Exception e)
			{
				if (Time.realtimeSinceStartup >= _nextError)
				{
					_nextError = Time.realtimeSinceStartup + 30f;
					Plugin.Logger.LogError($"British events check failed: {e}");
				}
			}
		}
	}
}
