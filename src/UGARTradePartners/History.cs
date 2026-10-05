// Price history. The game keeps none, so after each market's daily trade (TradingManager.DailyUpdate postfix) we
// record every good's buy/sell price for one unit and its stock, once per game day. Read-only for the game.
//
// Stored as plain text in BepInEx\config\ugar.tradepartners-history\<Nation>.tsv (the mod manager only watches *.cfg
// in the config folder itself, so this doesn't trigger a reload). One line per market/good/day:
//   yyyy-MM-dd <tab> market <tab> good asset name <tab> buy <tab> sell <tab> stock
// Saves have no id, so the history follows the timeline you play: when a day comes in that is not later than the last
// one recorded (you loaded an earlier save), everything from that day on is dropped first. At most MaxDays per series.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using World;
using TradingManager = World.TradingManager.TradingManager;

namespace UGARTradePartners
{
	internal struct PricePoint
	{
		public int Day; // DateTime.Date.Ticks / TicksPerDay
		public float Buy, Sell, Stock;
		public DateTime Date => new DateTime((long)Day * TimeSpan.TicksPerDay);
	}

	[HarmonyPatch]
	internal static class History
	{
		const int MaxDays = 1100; // about three years of campaign
		static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

		static string _nation;
		// market label -> good key -> points (oldest first)
		static readonly Dictionary<string, Dictionary<string, List<PricePoint>>> _data = new Dictionary<string, Dictionary<string, List<PricePoint>>>();

		public static string Key(PathAsset a)
		{
			try { return a.name; } catch (Exception) { return "?"; }
		}

		static string Dir => Path.Combine(Paths.ConfigPath, "ugar.tradepartners-history");
		static string FileFor(string nation) => Path.Combine(Dir, nation + ".tsv");

		static void Ensure(string nation)
		{
			if (_nation == nation) return;
			_nation = nation;
			_data.Clear();
			try
			{
				var file = FileFor(nation);
				if (!File.Exists(file)) return;
				foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
				{
					var p = line.Split('\t');
					if (p.Length < 6 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d)) continue;
					Add(p[1], p[2], new PricePoint
					{
						Day = (int)(d.Ticks / TimeSpan.TicksPerDay),
						Buy = F(p[3]), Sell = F(p[4]), Stock = F(p[5]),
					});
				}
				foreach (var m in _data.Values)
					foreach (var s in m.Values)
						if (s.Count > MaxDays) s.RemoveRange(0, s.Count - MaxDays);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Price history: could not read {FileFor(nation)} ({e.Message}); starting fresh.");
			}
		}

		static float F(string s) => float.TryParse(s, NumberStyles.Float, Inv, out var f) ? f : 0f;

		static void Add(string market, string good, PricePoint pt)
		{
			if (!_data.TryGetValue(market, out var m)) _data[market] = m = new Dictionary<string, List<PricePoint>>();
			if (!m.TryGetValue(good, out var s)) m[good] = s = new List<PricePoint>();
			if (s.Count > 0 && s[s.Count - 1].Day >= pt.Day) s.RemoveAll(x => x.Day >= pt.Day);
			s.Add(pt);
		}

		static string NationKey() => RuntimeVars.playerNation.ToString();

		/// <summary>The campaign date (RuntimeVars.date is an Il2CppSystem.DateTime) as a .NET date.</summary>
		public static DateTime Today()
		{
			var d = RuntimeVars.date;
			return new DateTime(d.Year, d.Month, d.Day);
		}

		public static int TodayIndex() => (int)(Today().Ticks / TimeSpan.TicksPerDay);

		public static List<PricePoint> Series(string market, PathAsset good)
		{
			try
			{
				Ensure(NationKey());
				if (_data.TryGetValue(market, out var m) && m.TryGetValue(Key(good), out var s)) return s;
			}
			catch (Exception) { }
			return null;
		}

		public static DateTime? FirstDay()
		{
			try
			{
				Ensure(NationKey());
				int first = int.MaxValue;
				foreach (var m in _data.Values) foreach (var s in m.Values) if (s.Count > 0) first = Math.Min(first, s[0].Day);
				return first == int.MaxValue ? (DateTime?)null : new DateTime((long)first * TimeSpan.TicksPerDay);
			}
			catch (Exception) { return null; }
		}

		static void Record(MarketView v)
		{
			var nation = NationKey();
			Ensure(nation);
			int day = History.TodayIndex();
			bool rewind = false;
			if (_data.TryGetValue(v.Label, out var m))
				foreach (var s in m.Values)
					if (s.Count > 0 && s[s.Count - 1].Day >= day) { rewind = true; break; }
			if (rewind)
			{
				// An earlier save was loaded: drop that day and everything after it, in every market.
				foreach (var mm in _data.Values) foreach (var s in mm.Values) s.RemoveAll(x => x.Day >= day);
			}
			var sb = new StringBuilder();
			string date = History.Today().ToString("yyyy-MM-dd", Inv);
			foreach (var g in v.Goods)
			{
				if (g.Buy <= 0f && g.Sell <= 0f) continue;
				var pt = new PricePoint { Day = day, Buy = g.Buy, Sell = g.Sell, Stock = g.Stock };
				string key = Key(g.Asset);
				Add(v.Label, key, pt);
				sb.Append(date).Append('\t').Append(v.Label).Append('\t').Append(key).Append('\t')
					.Append(g.Buy.ToString("0.##", Inv)).Append('\t').Append(g.Sell.ToString("0.##", Inv)).Append('\t')
					.Append(g.Stock.ToString("0.##", Inv)).Append('\n');
			}
			Directory.CreateDirectory(Dir);
			if (rewind) WriteAll(nation);
			else File.AppendAllText(FileFor(nation), sb.ToString(), Encoding.UTF8);
		}

		static void WriteAll(string nation)
		{
			var sb = new StringBuilder();
			foreach (var m in _data)
				foreach (var s in m.Value)
					foreach (var p in s.Value)
						sb.Append(p.Date.ToString("yyyy-MM-dd", Inv)).Append('\t').Append(m.Key).Append('\t').Append(s.Key).Append('\t')
							.Append(p.Buy.ToString("0.##", Inv)).Append('\t').Append(p.Sell.ToString("0.##", Inv)).Append('\t')
							.Append(p.Stock.ToString("0.##", Inv)).Append('\n');
			File.WriteAllText(FileFor(nation), sb.ToString(), Encoding.UTF8);
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(TradingManager), nameof(TradingManager.DailyUpdate))]
		static void DailyPostfix(MarketManager __0)
		{
			if (!Plugin.RecordHistory.Value || __0 == null) return;
			try
			{
				var c = Trade.Player();
				if (c == null) return;
				bool england;
				if (c.market != null && c.market.Pointer == __0.Pointer) england = false;
				else if (Trade.HasEngland(c) && c.europeanManager.marketManager.Pointer == __0.Pointer) england = true;
				else return;
				var v = Trade.Build(c, england);
				if (v.Error == null) Record(v);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Price history: recording failed ({e.Message})");
			}
		}
	}
}
