// Example mod for UGAR ModKit (not part of the player pack). Once per campaign, a few days after it is loaded, a
// travelling merchant offers supplies for money in the game's own question window. The offer is remembered in the
// save, so it never comes back. See docs/modkit.md.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UGAR.ModKit;
using World.SceneObject;

namespace UGARModKitExample
{
	[BepInPlugin("ugar.example.merchant", "UGAR ModKit Example", "0.1.0")]
	[BepInDependency(ModKit.Guid)]
	public class Plugin : BasePlugin
	{
		const string OfferedKey = "example.merchant.offered";
		const string DaysKey = "example.merchant.days";

		static ConfigEntry<bool> _enabled;
		static ConfigEntry<int> _afterDays;

		public override void Load()
		{
			_enabled = Config.Bind("General", "Enabled", true, "Show the merchant's offer once per campaign.");
			_afterDays = Config.Bind("General", "AfterDays", 3, "In-game days after the campaign is loaded before the merchant arrives.");

			Game.CampaignLoaded += country =>
				Log.LogInfo($"Campaign loaded: {country.Nation}, {Game.Today:yyyy-MM-dd}, merchant {(SaveData.HasFlag(country, OfferedKey) ? "already came" : "not yet")}.");
			Game.NewDay += OnNewDay;
			Log.LogInfo("UGAR ModKit Example loaded.");
		}

		static void OnNewDay(Country country, DateTime today, int days)
		{
			if (!_enabled.Value || SaveData.HasFlag(country, OfferedKey))
				return;
			// Counted in the save, so quitting and reloading doesn't restart the wait.
			if (SaveData.AddNumber(country, DaysKey, days) < _afterDays.Value)
				return;

			SaveData.SetFlag(country, OfferedKey);
			SaveData.RemoveNumber(country, DaysKey);
			var price = new Price(money: 500);
			Popups.Ask(
				"A Travelling Merchant",
				"A merchant with a wagon of timber and iron offers to sell his whole load to the army.",
				Option.Paid("Buy the load", price, () =>
				{
					Treasury.AddSupplies(country, 10);
					Popups.Show("A Bargain", "The supplies have been delivered: +10 supplies.");
				}),
				new Option("Send him away"));
		}
	}
}
