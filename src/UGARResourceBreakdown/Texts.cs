using System;
using System.Collections.Generic;
using System.Text;

namespace UGARResourceBreakdown
{
	/// <summary>
	/// Rich text for the popover, in the same visual language as Recruit Growth: gold headers, white values, grey notes,
	/// green for income, orange/red for use and warnings. Two-column rows put the value right-aligned on the label's line.
	/// </summary>
	internal static class Texts
	{
		const string Gold = "#E8C66A";
		const string Good = "#8FD16A";
		const string Warn = "#E0A040";
		const string Bad = "#E07060";
		const string Dim = "#9A9A9A";
		const int MaxRows = 8;

		static string C(string color, string s) => $"<color={color}>{s}</color>";
		static string Small(string s) => $"<size=80%>{C(Dim, s)}</size>";
		static string Section(string s) => $"\n<size=90%><b>{C(Gold, s.ToUpperInvariant())}</b></size>\n";

		static string Row(string label, string value) =>
			$"<align=left>{label}<line-height=0>\n<align=right>{value}<line-height=100%>\n<align=left>";

		static string Num(float f) => Math.Abs(f) >= 100f ? f.ToString("N0") : Math.Abs(f) >= 10f ? f.ToString("0.#") : f.ToString("0.##");
		static string Signed(float f) => (f >= 0f ? "+" : "−") + Num(Math.Abs(f));
		static string In(float f) => C(f > 0f ? Good : f < 0f ? Bad : Dim, Signed(f));
		static string Out(float f) => C(f > 0f ? Warn : Dim, "−" + Num(f));

		static string Hero(string a, string aLabel, string b, string bLabel, string c, string cLabel) =>
			$"<size=150%><b><pos=0%>{a}<pos=36%>{b}<pos=70%>{c}</b></size>\n" +
			$"<size=80%>{C(Dim, $"<pos=0%>{aLabel}<pos=36%>{bLabel}<pos=70%>{cLabel}")}</size>\n";

		static void Rows(StringBuilder sb, List<Line> lines, Func<float, string> value, string unit)
		{
			int shown = 0;
			float rest = 0f;
			foreach (var l in lines)
			{
				if (shown >= MaxRows)
				{
					rest += l.Amount;
					continue;
				}
				shown++;
				string note = string.IsNullOrEmpty(l.Note) ? "" : " " + Small(l.Note);
				sb.Append(Row($"{l.Label}{note}", value(l.Amount) + Small(unit)));
			}
			if (lines.Count > MaxRows)
				sb.Append(Row(C(Dim, $"{lines.Count - MaxRows} more"), value(rest) + Small(unit)));
		}

		public static string Resource(ResourceReport r)
		{
			var sb = new StringBuilder();
			sb.Append($"<size=120%><b>{C(Gold, r.Name)}</b></size>{(r.England ? " " + Small("England storage") : "")}\n");
			if (r.Error != null)
				return sb.Append(C(Warn, r.Error)).ToString();

			string net = r.WeeklyNet >= 0f ? C(Good, Signed(r.WeeklyNet)) : C(Bad, Signed(r.WeeklyNet));
			sb.Append(Hero(Num(r.Stock), "in storage (N)", r.PanelDaily > 0f ? "−" + Num(r.PanelDaily) : "0", "used per day (M)", net, "net per week"));
			sb.Append(Small($"The Production screen shows \"N (M)\": N is what's in {(r.England ? "England" : "your")} storage, " +
				"M is what your running orders use per day at full speed (hidden when nothing uses it).") + "\n");

			// Coming in.
			sb.Append(Section("Coming in, each week"));
			if (r.Regions.Count == 0 && r.Conversions.Count == 0 && r.WeeklyRegions == 0f)
				sb.Append(C(Dim, "Nothing produces it in your territory.") + "\n");
			Rows(sb, r.Regions, In, "/wk");
			if (r.Conversions.Count > 0)
				Rows(sb, r.Conversions, In, "/wk");
			if (r.StatesStopped > 0)
				sb.Append(C(Warn, $"{r.StatesStopped} state{(r.StatesStopped == 1 ? " has" : "s have")} resource extraction at 0: their regions deliver nothing.") + "\n");
			if (!r.FreeProduction && r.Regions.Count > 0)
				sb.Append(Small("Mines deliver raw output × the state's extraction rate × your Resource Extraction department, and cost money (mining expenses).") + "\n");
			if (r.LastWeekReceived >= 0f)
				sb.Append(Row(C(Dim, $"Last delivery from regions ({r.LastWeekDate})"), C(Dim, Signed(r.LastWeekReceived))));

			// Going out.
			sb.Append(Section("Used by production, each day"));
			if (r.Orders.Count == 0)
				sb.Append(C(Dim, "No running order uses it.") + "\n");
			Rows(sb, r.Orders, Out, "/day");
			if (Math.Abs(r.ConsumptionEffect) > 0.001f && r.PanelDaily > 0f)
				sb.Append(Row(C(Dim, $"Resource Consumption department ({(r.ConsumptionEffect > 0 ? "+" : "")}{r.ConsumptionEffect:P0})"),
					Out(r.DailyUse) + Small("/day")));
			if (r.OrdersShort)
				sb.Append(C(Warn, "Some orders are short of this resource and run slower.") + "\n");

			// Ships, ports, the other storage.
			if (r.AtSeaToColony > 0f || r.AtSeaToEurope > 0f || r.InPorts > 0f || r.OtherStock > 0f)
			{
				sb.Append(Section("Ships and storage"));
				if (r.OtherStock >= 0f)
					sb.Append(Row(r.England ? "In colony storage" : "In England storage", Num(r.OtherStock)));
				if (r.AtSeaToColony > 0f)
					sb.Append(Row("At sea, bound for America", Num(r.AtSeaToColony)));
				if (r.AtSeaToEurope > 0f)
					sb.Append(Row("At sea, bound for England", Num(r.AtSeaToEurope)));
				if (r.InPorts > 0f)
					sb.Append(Row("Waiting in ports", Num(r.InPorts)));
			}

			// Net.
			sb.Append(Section("Net"));
			sb.Append(Row("Produced per week", In(r.WeeklyIn)));
			sb.Append(Row("Used per week (7 × daily)", Out(r.DailyUse * 7f)));
			sb.Append(Row("<b>Net per week</b>", "<b>" + net + "</b>"));
			if (r.DailyUse > 0f && r.WeeklyNet < 0f)
			{
				float days = r.Stock / Math.Max(0.0001f, r.DailyUse - r.WeeklyIn / 7f);
				sb.Append(C(Warn, $"Storage runs out in about {Math.Max(0, (int)days)} days at this rate.") + "\n");
			}
			sb.Append(Small("Market purchases, trade deliveries, loot and events add to storage as they happen and aren't forecast here."));
			return sb.ToString();
		}

		public static string Plants(PlantReport p)
		{
			var sb = new StringBuilder();
			string what = p.Shipyard ? "Shipyards" : "Factories";
			sb.Append($"<size=120%><b>{C(Gold, what)}</b></size>{(p.England ? " " + Small("England") : "")}\n");
			if (p.Error != null)
				return sb.Append(C(Warn, p.Error)).ToString();
			sb.Append(Hero(p.Used.ToString(), "in use", p.Total.ToString(), "available", (p.Total - p.Used).ToString(), "idle"));
			sb.Append(Small($"The Production screen shows \"used/total\" {(p.Shipyard ? "shipyards (anchor icon)" : "factories")}. " +
				"Orders take free plants from the top of the list down, each up to its own maximum.") + "\n");
			sb.Append(Section(p.Shipyard ? "Ship orders" : "Orders"));
			if (p.Orders.Count == 0)
				sb.Append(C(Dim, "No orders.") + "\n");
			foreach (var l in p.Orders)
				sb.Append(Row($"{l.Label} {Small(l.Note)}", $"{l.Amount:0}" + Small(" plants")));
			return sb.ToString();
		}
	}
}
