using System;
using System.Collections.Generic;
using System.Text;
using World.SceneObject;

namespace UGARRecruitGrowth
{
	/// <summary>
	/// Rich text (TextMeshPro tags) for the game's tooltip and the pinned popovers. Visual language: gold headers,
	/// white values, grey secondary text, orange for paused/blocked, green for growth. Rows never wrap: two-column
	/// rows use TMP's "<line-height=0>" overlay so the value is right-aligned on the same line as its label.
	/// </summary>
	internal static class Texts
	{
		const string Gold = "#E8C66A";
		const string Good = "#8FD16A";
		const string Warn = "#E0A040";
		const string Bad = "#E07060";
		const string Dim = "#9A9A9A";

		static string C(string color, string s) => $"<color={color}>{s}</color>";
		static string Small(string s) => $"<size=80%>{C(Dim, s)}</size>";
		static string Header(string s) => $"<b>{C(Gold, s)}</b>\n";
		static string Section(string s) => $"\n<size=90%><b>{C(Gold, s.ToUpperInvariant())}</b></size>\n";

		/// <summary>Label on the left, value right-aligned on the same line.</summary>
		static string Row(string label, string value) =>
			$"<align=left>{label}<line-height=0>\n<align=right>{value}<line-height=100%>\n<align=left>";

		static string Signed(int n) => (n >= 0 ? "+" : "") + n.ToString("N0");
		static string X(float f) => "×" + f.ToString("0.00");
		static bool Neutral(float f) => Math.Abs(f - 1f) < 0.005f;

		// ---------------- Shared pieces ----------------

		static string Hero(string a, string aLabel, string b, string bLabel, string c, string cLabel)
		{
			return $"<size=165%><b><pos=0%>{a}<pos=36%>{b}<pos=70%>{c}</b></size>\n" +
				$"<size=80%>{C(Dim, $"<pos=0%>{aLabel}<pos=36%>{bLabel}<pos=70%>{cLabel}")}</size>\n";
		}

		static string SettlementHero(LocalityGrowth g)
		{
			int tomorrow = g.GainTomorrow;
			string t = C(tomorrow > 0 ? Good : Warn, Signed(tomorrow));
			string perDay = Signed(g.Intake + g.PolicyBonus);
			return Hero(g.Stock.ToString("N0"), "in stock", perDay, "per day", t, "tomorrow");
		}

		static string Status(LocalityGrowth g)
		{
			if (g.Intake <= 0)
				return $"{C(Warn, "<b>No recruits.</b>")} {Reason(g)}\n";
			if (g.GrowsTomorrow)
			{
				string policy = g.PolicyBonus > 0 ? $" {Small($"(incl. {g.PolicyBonus:N0} from recruiting policy)")}" : "";
				return $"{C(Good, "<b>Growing.</b>")} +{g.GainTomorrow:N0} arrives tomorrow.{policy}\n";
			}
			return $"{C(Warn, "<b>Full.</b>")} Growth resumes when stock falls below {g.Intake:N0}.\n";
		}

		static string Reason(LocalityGrowth g)
		{
			if (g.Workforce <= 0 && !(g.UsesSlaves && g.Slaves > 0)) return "The settlement has no workforce.";
			if (!g.DestroyRepaired && g.Progress <= 0f) return "Its main building isn't built yet.";
			if (g.Share <= 0f) return "Region loyalty is too low to recruit.";
			return "The daily amount rounds down to 0.";
		}

		// ---------------- Hover (inside the game's own recruits tooltip) ----------------

		public static string HoverSummary(RegionLocality l)
		{
			var g = RecruitMath.Compute(l);
			if (g.Error != null)
				return null;
			var sb = new StringBuilder();
			sb.Append('\n').Append(Header("Recruit growth"));
			sb.Append(SettlementHero(g));
			sb.Append(Status(g));
			sb.Append(Small("Click for details"));
			return sb.ToString();
		}

		// ---------------- Settlement popover ----------------

		public static string Details(RegionLocality l)
		{
			var g = RecruitMath.Compute(l);
			var sb = new StringBuilder();
			sb.Append(Header($"{g.Name} · Recruits"));
			if (g.Error != null)
				return sb.Append(C(Bad, "Could not read the numbers: " + g.Error)).ToString();

			sb.Append(SettlementHero(g));
			sb.Append(Status(g));

			sb.Append(Section($"Why {g.Intake:N0} a day"));
			sb.Append(Row("Workforce", g.Workforce.ToString("N0")));
			if (!g.DestroyRepaired && g.Progress < 0.995f)
				sb.Append(Row("× Town development", g.Progress.ToString("P0")));
			if (g.UsesSlaves && g.Slaves > 0)
				sb.Append(Row("+ Slaves", g.Slaves.ToString("N0")));
			sb.Append(Row("× Recruit rate", (g.Basis * 100f).ToString("0.##") + "%"));
			int n = Math.Max(1, g.LocalitiesInRegion);
			sb.Append(Row("÷ Settlements in region", n.ToString()));
			float baseValue = g.Manpower * g.Basis / n;
			sb.Append(Row("<b>= Base</b>", $"<b>{baseValue:0.0}</b>"));

			var neutral = new List<string>();
			void Factor(string label, float value, string neutralName)
			{
				if (Neutral(value)) neutral.Add(neutralName);
				else sb.Append(Row(label, X(value)));
			}
			Factor($"Loyalty {g.Loyalty:P0}", g.LoyaltyEffect, "loyalty");
			Factor("Difficulty", g.Difficulty, "difficulty");
			Factor("Loyalty recruiting bonus", g.LoyaltyRecruitMod, "loyalty bonuses");
			Factor("Buildings & events", g.LocalityMod, "buildings, events");
			Factor("Leadership department", g.LeadershipMod, "leadership");
			Factor($"Bounty {Small($"({g.BountyRatio:P0} of expected)")}", g.BountyMod, "bounty");

			string rounded = Math.Abs(g.Estimate - g.Intake) >= 0.05f ? " " + Small($"{g.Estimate:0.0} rounded down") : "";
			sb.Append(Row($"<b>{C(Gold, "Per day")}</b>{rounded}", $"<b>{g.Intake:N0}</b>"));
			if (g.PolicyBonus > 0)
				sb.Append(Row("+ Recruiting policy " + Small("(paid daily)"), $"+{g.PolicyBonus:N0}"));
			if (neutral.Count > 0)
				sb.Append(Small("No effect: " + string.Join(", ", neutral))).Append('\n');
			if (g.WeeklyWorkforce > 0)
			{
				string custom = g.CustomWorkforce > 0 ? $" (+{g.CustomWorkforce:N0} of it from custom buildings)" : "";
				sb.Append(Small($"Workforce grows +{g.WeeklyWorkforce:N0} a week{custom}, slowly raising the daily intake.")).Append('\n');
			}
			foreach (var b in g.CustomBuildings)
				sb.Append(Small($"{C(Gold, "Custom building")} {b}")).Append('\n');

			if (RecruitMath.IsBritishHome(l))
			{
				var p = RecruitMath.Britain();
				if (p != null) sb.Append(Britain(p));
			}
			sb.Append(Footer());
			return sb.ToString();
		}

		static string Footer() => $"\n<align=right>{Small("Click outside to close")}<align=left>";

		// ---------------- Britain pipeline ----------------

		static string Britain(BritishPipeline p)
		{
			var sb = new StringBuilder();
			sb.Append(Section("Shipments from Britain"));
			if (p.Error != null)
				return sb.Append(C(Bad, "Could not read: " + p.Error)).Append('\n').ToString();

			sb.Append(Row("Waiting in Britain", p.HomeReady.ToString("N0")));
			string atSea = p.OnWay.ToString("N0");
			if (p.Convoys.Count > 0)
			{
				var c = p.Convoys[0];
				atSea = $"{Small($"lands {RecruitMath.Today.AddDays(c.DaysLeft):d MMM} ({RecruitMath.Days(c.DaysLeft)})")}  {atSea}";
			}
			sb.Append(Row("At sea", atSea));
			sb.Append(Row("In America", $"{p.Pool:N0} {Small($"/ {p.Cap:N0} cap")}"));

			sb.Append(NextShip(p)).Append('\n');
			string ships = p.ShipsFree >= 0 ? $" · {p.ShipsFree} free ships" : "";
			sb.Append(Small($"Ship load {p.ShipLoad:N0} · voyage {RecruitMath.Days(p.TravelDays)}{ships}")).Append('\n');
			return sb.ToString();
		}

		static string Blocked(string title, string text) =>
			$"<mark=#E0A04030 padding=\"6,6,3,3\">{C(Warn, $"<b>{title}</b>")} {text}</mark>";

		static string NextShip(BritishPipeline p)
		{
			if (p.Cap - (p.Pool + p.OnWay) <= 0f)
				return Blocked("Ships stopped:", $"the America cap of {p.Cap:N0} is reached. Raise it on the Britain screen.");
			if (p.BlockedBy != null)
				return Blocked("Ships stopped:", $"{p.BlockedBy} has an open supply request.");
			if (p.ShipsFree == 0)
				return Blocked("Ships stopped:", "no free transport ship.");
			if (p.HomeReady < p.Threshold)
			{
				string eta = p.HomeDaily > 0
					? $"in about {RecruitMath.Days((int)Math.Ceiling((p.Threshold - p.HomeReady) / (float)p.HomeDaily))}"
					: "once more recruits are ready";
				return $"Next ship sails {eta} (needs {p.Threshold:N0} waiting).";
			}
			int load = Math.Min(p.ShipLoad, p.HomeReady);
			return $"{C(Good, "<b>Next ship sails tomorrow</b>")} ({load:N0} recruits, lands {RecruitMath.Today.AddDays(p.TravelDays + 1):d MMM}).";
		}

		// ---------------- Top bar hover + overview ----------------

		static List<LocalityGrowth> PlayerSettlements()
		{
			var player = RuntimeVars.playerNation;
			var rows = new List<LocalityGrowth>();
			foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
			{
				if (l == null || l.ownerNation != player || RecruitMath.IsBritishHome(l))
					continue;
				rows.Add(RecruitMath.Compute(l));
			}
			rows.Sort((a, b) =>
			{
				int c = string.Compare(a.RegionName, b.RegionName, StringComparison.OrdinalIgnoreCase);
				return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
			});
			return rows;
		}

		static void Totals(List<LocalityGrowth> rows, out int tomorrow, out int paused, out int none)
		{
			tomorrow = paused = none = 0;
			foreach (var g in rows)
			{
				tomorrow += g.GainTomorrow;
				if (g.Intake <= 0) none++;
				else if (!g.GrowsTomorrow) paused++;
			}
		}

		public static string NationalHover()
		{
			var rows = PlayerSettlements();
			Totals(rows, out int tomorrow, out int paused, out _);
			var sb = new StringBuilder();
			sb.Append('\n').Append(Header("Recruit growth"));
			sb.Append(Hero(C(Good, Signed(tomorrow)), "tomorrow", rows.Count.ToString(), "settlements", C(paused > 0 ? Warn : Good, paused.ToString()), "full"));
			var p = RecruitMath.Britain();
			if (p != null && p.Error == null)
				sb.Append("Britain: ").Append(NextShip(p)).Append('\n');
			sb.Append(Small("Click for all settlements"));
			return sb.ToString();
		}

		/// <summary>The colonial recruits top-bar counter's popover: total, then every settlement in America by stock.</summary>
		public static string Colonial()
		{
			var rows = PlayerSettlements();
			int total = 0, daily = 0;
			foreach (var g in rows)
			{
				total += Math.Max(0, g.Stock);
				daily += g.GainTomorrow;
			}
			var sb = new StringBuilder();
			sb.Append(Header("Recruits in the colonies"));
			sb.Append(Hero(total.ToString("N0"), "waiting in towns", C(Good, Signed(daily)), "tomorrow", rows.Count.ToString(), "settlements"));
			sb.Append(Small("Recruits held by your settlements in America (Great Britain's home towns and the regulars' America pool not included).")).Append('\n');

			var p = RecruitMath.Britain();
			if (p != null && p.Error == null)
			{
				sb.Append(Section("Regulars from Britain"));
				sb.Append(Row("America pool", $"{p.Pool:N0} {Small($"of {p.Cap:N0} cap")}"));
				sb.Append(Row("At sea", p.OnWay.ToString("N0")));
				sb.Append(Row("Waiting in Britain", p.HomeReady.ToString("N0")));
			}

			sb.Append(Section("By town"));
			rows.Sort((a, b) => b.Stock != a.Stock ? b.Stock.CompareTo(a.Stock) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			const int max = 30;
			int shown = 0, restStock = 0, restTowns = 0;
			foreach (var g in rows)
			{
				if (shown >= max)
				{
					restStock += Math.Max(0, g.Stock);
					restTowns++;
					continue;
				}
				shown++;
				string perDay = g.Intake <= 0 ? C(Dim, "none") : g.GrowsTomorrow ? C(Good, Signed(g.GainTomorrow)) : C(Warn, "full");
				sb.Append(Row($"{Trunc(g.Name, 22)} {Small(Trunc(g.RegionName, 20))}", $"{g.Stock:N0}  {Small(perDay)}"));
			}
			if (restTowns > 0)
				sb.Append(Row(C(Dim, $"{restTowns} more towns"), restStock.ToString("N0")));
			if (rows.Count == 0)
				sb.Append(C(Dim, "You hold no settlements in America.")).Append('\n');
			sb.Append(Footer());
			return sb.ToString();
		}

		static string Trunc(string s, int n) => s == null ? "?" : s.Length > n ? s.Substring(0, n - 1) + "…" : s;

		/// <summary>Overview: totals, the Britain pipeline, and a table of every settlement split into 1 or 2 columns.</summary>
		public static string Overview(int maxRowsPerColumn, out int tableColumns)
		{
			var rows = PlayerSettlements();
			Totals(rows, out int tomorrow, out int paused, out int none);
			var sb = new StringBuilder();
			sb.Append(Header("Recruits · All settlements"));
			sb.Append(Hero(C(Good, Signed(tomorrow)), "tomorrow", rows.Count.ToString(), "settlements",
				C(paused > 0 ? Warn : Good, paused.ToString()), "full (paused)"));
			if (none > 0)
				sb.Append(Small($"{none} settlements produce no recruits.")).Append('\n');
			int withCustom = 0, customWorkforce = 0;
			foreach (var g in rows)
			{
				if (g.CustomBuildings.Length > 0) withCustom++;
				customWorkforce += g.CustomWorkforce;
			}
			if (withCustom > 0)
				sb.Append(Small($"Custom buildings in {withCustom} settlements add +{customWorkforce:N0} workforce a week (their recruit bonuses are already in the numbers).")).Append('\n');

			var p = RecruitMath.Britain();
			if (p != null)
				sb.Append(Britain(p));

			// Table lines: region headings and settlement rows.
			var lines = new List<(bool heading, string name, LocalityGrowth g)>();
			string region = null;
			foreach (var g in rows)
			{
				if (g.RegionName != region)
				{
					region = g.RegionName;
					lines.Add((true, region, null));
				}
				lines.Add((false, g.Name, g));
			}
			tableColumns = lines.Count > maxRowsPerColumn ? 2 : 1;
			int perCol = (lines.Count + tableColumns - 1) / Math.Max(1, tableColumns);

			sb.Append(Section("Settlements"));
			var head = new StringBuilder();
			for (int c = 0; c < tableColumns; c++)
				head.Append(Cols(c, tableColumns, "Settlement", "Stock", "Per day", "Tomorrow"));
			sb.Append(Small(head.ToString())).Append('\n');
			for (int r = 0; r < perCol; r++)
			{
				for (int c = 0; c < tableColumns; c++)
				{
					int i = c * perCol + r;
					if (i >= lines.Count) continue;
					var line = lines[i];
					if (line.heading)
					{
						sb.Append(Cols(c, tableColumns, $"<size=85%>{C(Gold, Trunc(line.name, 28))}</size>", "", "", ""));
						continue;
					}
					var g = line.g;
					if (g.Error != null)
					{
						sb.Append(Cols(c, tableColumns, Trunc(line.name, 18), C(Bad, "?"), "", ""));
						continue;
					}
					string t = g.Intake <= 0 ? C(Dim, "none")
						: g.GrowsTomorrow ? C(Good, Signed(g.GainTomorrow))
						: C(Warn, "full");
					string name = g.Intake > 0 && !g.GrowsTomorrow ? C(Warn, Trunc(line.name, 18)) : Trunc(line.name, 18);
					sb.Append(Cols(c, tableColumns, name, g.Stock.ToString("N0"), g.Intake.ToString("N0"), t));
				}
				sb.Append('\n');
			}
			sb.Append(Footer());
			return sb.ToString();
		}

		static string Cols(int col, int cols, string a, string b, string c, string d)
		{
			float w = 100f / cols, s = col * w;
			string P(float f) => $"<pos={s + w * f:0.#}%>";
			return $"{P(0f)}{a}{P(0.50f)}{b}{P(0.66f)}{c}{P(0.82f)}{d}";
		}
	}
}
