// UGAR Trade Partners: a read-only window that shows the trade the game runs behind its markets. Eight partner nations
// feed and drain each market's stock every day (TradingManager.DailyUpdate); the game only hints at this in the
// Diplomacy tab. The window lists each partner's goods per day after tension and lost trade ships, what the market
// holds, buy/sell prices and the market's price multipliers. Nothing in the game is changed. See Trade.cs for the
// game's rules and docs/trade-partners.md for the player guide.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UGARTradePartners
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.tradepartners";
		public const string Name = "UGAR Trade Partners";
		public const string Version = "1.1.0";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<string> Hotkey;
		internal static ConfigEntry<float> UiScale;
		internal static ConfigEntry<bool> VerifyLog;
		internal static ConfigEntry<bool> RecordHistory;

		public override void Load()
		{
			Logger = Log;
			Enabled = Config.Bind("General", "Enabled", true,
				"Show the Trade Partners window (PARTNERS tab on the market screen, and the hotkey).");
			Hotkey = Config.Bind("General", "Hotkey", "F6", "Key that opens and closes the Trade Partners window on the campaign map (a Unity KeyCode name, e.g. F6, T).");
			UiScale = Config.Bind("Window", "Scale", 1f, new ConfigDescription("Size multiplier for the window.", new AcceptableValueRange<float>(0.5f, 3f)));
			VerifyLog = Config.Bind("Debug", "VerifyLog", true,
				"Each game day, write a 'Trade check' line to the BepInEx log comparing the window's prediction with what the game's trade actually moved.");

			RecordHistory = Config.Bind("History", "Record", true,
				"Record every market's buy/sell price and stock once per game day for the PRICE HISTORY view " +
				"(BepInEx\\config\\ugar.tradepartners-history\\<nation>.tsv). The game keeps no price history, so it starts when this is on.");

			ClassInjector.RegisterTypeInIl2Cpp<TradePanel>();
			AddComponent<TradePanel>();
			var harmony = new Harmony(Guid);
			harmony.PatchAll(typeof(Verify));
			harmony.PatchAll(typeof(History));
			Log.LogInfo($"{Name} {Version} loaded. Open it from the PARTNERS tab on the market screen or with {Hotkey.Value}.");
		}

		internal static KeyCode HotkeyCode()
		{
			return Enum.TryParse<KeyCode>(Hotkey.Value?.Trim(), true, out var k) ? k : KeyCode.F6;
		}
	}
}
