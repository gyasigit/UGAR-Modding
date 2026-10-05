// UGAR Recruit Growth: shows how many recruits each settlement gains per day, where that number comes from,
// and when the next recruits from Britain arrive. Read-only: it calls the game's own formulas and changes nothing.
// See RecruitMath.cs for the game's rules and docs/recruit-growth.md for the player guide.
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UGARRecruitGrowth
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.recruitgrowth";
		public const string Name = "UGAR Recruit Growth";
		public const string Version = "1.3.1";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<bool> ShowInTooltips;
		internal static ConfigEntry<KeyCode> OverviewKey;
		internal static ConfigEntry<bool> VerifyLog;
		internal static ConfigEntry<float> UiScale;
		internal static ConfigEntry<bool> ColonialTopBar;

		public override void Load()
		{
			Logger = Log;
			Enabled = Config.Bind("General", "Enabled", true, "Turn the recruit growth display on or off.");
			ShowInTooltips = Config.Bind("General", "ShowInTooltips", true, "Add a short recruit growth summary to the game's recruits tooltip (settlement card) and population tooltip (top bar).");
			OverviewKey = Config.Bind("General", "OverviewKey", KeyCode.None, "Optional key that opens the all-settlements overview. None by default (F7, F8 and F9 are used by other mods). Clicking the population number in the top bar also opens it.");
			VerifyLog = Config.Bind("Debug", "VerifyLog", true, "Each in-game day, write the predicted and actual recruit gains to BepInEx\\LogOutput.log, so you can check the numbers.");
			UiScale = Config.Bind("Window", "Scale", 1f, new ConfigDescription("Width multiplier for the pinned popovers.", new AcceptableValueRange<float>(0.5f, 2f)));

			ColonialTopBar = Config.Bind("General", "ColonialRecruitsOnTopBar", true, "British campaigns: show the total recruits waiting in your settlements in America as an extra number on the campaign top bar. Click it for the per-town list.");

			ClassInjector.RegisterTypeInIl2Cpp<Overlay>();
			AddComponent<Overlay>();
			ClassInjector.RegisterTypeInIl2Cpp<TopBar>();
			AddComponent<TopBar>();
			new Harmony(Guid).PatchAll(typeof(Patches));
			Log.LogInfo($"{Name} {Version} loaded. Hover or click a settlement's recruits number, or the population number in the top bar.");
		}
	}
}
