// Read-only check that the window's numbers match the game: around each TradingManager.DailyUpdate(market) we predict
// tonight's change with Trade.Build (prefix) and compare it with the market's stock afterwards (postfix). One log line
// per market per day. Nothing is changed; the patches only read.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using World;
using TradingManager = World.TradingManager.TradingManager;

namespace UGARTradePartners
{
	[HarmonyPatch]
	internal static class Verify
	{
		sealed class Snapshot
		{
			public MarketView View;
		}

		[HarmonyPrefix]
		[HarmonyPatch(typeof(TradingManager), nameof(TradingManager.DailyUpdate))]
		static void DailyPrefix(MarketManager __0, out Snapshot __state)
		{
			__state = null;
			if (!Plugin.VerifyLog.Value || __0 == null) return;
			try
			{
				var c = Trade.Player();
				if (c == null) return;
				bool england;
				if (c.market != null && c.market.Pointer == __0.Pointer) england = false;
				else if (Trade.HasEngland(c) && c.europeanManager.marketManager.Pointer == __0.Pointer) england = true;
				else return;
				__state = new Snapshot { View = Trade.Build(c, england) };
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Trade check: prediction failed ({e.Message})");
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(TradingManager), nameof(TradingManager.DailyUpdate))]
		static void DailyPostfix(MarketManager __0, Snapshot __state)
		{
			if (__state?.View == null || __0?.storage == null) return;
			try
			{
				var v = __state.View;
				var sb = new StringBuilder();
				int shown = 0, matched = 0, moved = 0;
				var worst = new List<(float diff, string text)>();
				foreach (var g in v.Goods)
				{
					float after = __0.storage.GetItemCount(g.Asset);
					float actual = after - g.Stock;
					if (Math.Abs(actual) < 0.0005f && Math.Abs(g.Net) < 0.0005f) continue;
					moved++;
					float diff = Math.Abs(actual - g.Net);
					if (diff < 0.01f) matched++;
					worst.Add((diff, $"{g.Name} {S(g.Net)}/{S(actual)}"));
				}
				worst.Sort((a, b) => b.diff.CompareTo(a.diff));
				foreach (var w in worst)
				{
					if (shown++ >= 6) break;
					sb.Append(shown > 1 ? ", " : "").Append(w.text);
				}
				Plugin.Logger.LogInfo($"Trade check ({v.Label}, {History.Today():yyyy-MM-dd}): {matched}/{moved} goods matched the prediction" +
					(sb.Length > 0 ? $"; predicted/actual: {sb}" : "") + ".");
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Trade check failed: {e.Message}");
			}
		}

		static string S(float f) => (f >= 0 ? "+" : "") + f.ToString("0.##", CultureInfo.InvariantCulture);
	}
}
