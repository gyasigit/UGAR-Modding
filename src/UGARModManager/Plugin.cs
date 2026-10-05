// UGAR Mod Manager: one in-game window (F8, or the small "Mods" button) that lists every installed BepInEx
// mod, lets the player switch each one on or off, and edits its BepInEx config settings live.
//
// Mods need no reference to this plugin. Everything is discovered from BepInEx itself:
//   - loaded plugins come from IL2CPPChainloader, their settings from their ConfigFile;
//   - switched-off mods are plugin dlls renamed to "*.dll.disabled", which BepInEx skips on the next launch.
// Mods can optionally describe themselves with a ugar-mod.json next to their dll and tag config entries
// with "RestartRequired", "Advanced" or "Hidden" (see docs/mod-manager.md).
// Live reload (HotReload.cs) applies changed mod dlls, data files and edited .cfg files without restarting the game.
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UGARModManager
{
	public enum ButtonCorner
	{
		BottomLeft,
		BottomRight,
		TopLeft,
		TopRight,
	}

	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.modmanager";
		public const string Name = "UGAR Mod Manager";
		public const string Version = "1.1.2";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<KeyCode> ToggleKey;
		internal static ConfigEntry<bool> ShowButton;
		internal static ConfigEntry<ButtonCorner> ButtonPosition;
		internal static ConfigEntry<float> UiScale;
		internal static ConfigEntry<bool> BlockGameClicks;
		internal static ConfigEntry<bool> ShowAdvanced;
		internal static ConfigEntry<bool> WatchModFiles;
		internal static ConfigEntry<bool> WatchConfigFiles;
		internal static ConfigEntry<float> ReloadDelay;

		public override void Load()
		{
			Logger = Log;
			ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F8, "Key that opens and closes the mod manager window.");
			ShowButton = Config.Bind("General", "ShowButton", true, "Show a small 'Mods' button in a corner of the screen.");
			ButtonPosition = Config.Bind("General", "ButtonCorner", ButtonCorner.BottomLeft, "Corner of the screen for the 'Mods' button.");
			UiScale = Config.Bind("General", "Scale", 1f, new ConfigDescription("Size multiplier for the window and button.", new AcceptableValueRange<float>(0.6f, 2.5f)));
			BlockGameClicks = Config.Bind("General", "BlockGameClicks", true, new ConfigDescription("Stop clicks on the window from also reaching the game UI behind it.", null, "Advanced"));
			ShowAdvanced = Config.Bind("General", "ShowAdvanced", false, new ConfigDescription("Show advanced and window-position settings of every mod.", null, "Hidden"));
			WatchModFiles = Config.Bind("LiveReload", "ReloadChangedMods", true, "When a mod's dll or its data files (*.json) change on disk, reload that mod in the running game. No restart needed.");
			WatchConfigFiles = Config.Bind("LiveReload", "ReloadEditedSettings", true, "When a mod's .cfg file is edited outside the game, re-read it so the new values apply straight away.");
			ReloadDelay = Config.Bind("LiveReload", "Delay", 1f, new ConfigDescription("Seconds to wait after the last file change before reloading, so a copy has finished.", new AcceptableValueRange<float>(0.2f, 10f), "Advanced"));

			ClassInjector.RegisterTypeInIl2Cpp<ManagerWindow>();
			AddComponent<ManagerWindow>();
			try
			{
				new HarmonyLib.Harmony(Guid).CreateClassProcessor(typeof(InputLock.IsInputLockedPatch)).Patch();
			}
			catch (System.Exception e)
			{
				Log.LogWarning($"Typing in the window may also trigger game hotkeys: input lock patch failed: {e.Message}");
			}
			Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value} in game to open it.");
		}
	}
}
