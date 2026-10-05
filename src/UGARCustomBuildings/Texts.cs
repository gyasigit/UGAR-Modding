using System.Collections.Generic;

namespace UGARCustomBuildings
{
	internal static class Texts
	{
		/// <summary>Effects this plugin applies, for the game's effect text (native modifiers are listed by the game).</summary>
		public static List<string> CustomEffectLines(BuildingDefinition d)
		{
			var lines = new List<string>();
			if (d.PopulationGrowthBonus != 0f)
				lines.Add($"Weekly population growth {d.PopulationGrowthBonus:+0%;-0%}");
			if (d.WeeklyWorkforce != 0)
				lines.Add($"Workforce {d.WeeklyWorkforce:+0;-0} every week");
			foreach (var m in d.CountryModifiers)
				lines.Add($"{Cap(Nice(m.Modifier))} {Amount(m, 1f)} (whole nation)");
			if (d.HasWeeklyConversion)
				lines.Add($"Every week: uses {Conversion.Describe(d)} (colonies or England storage) for +{d.WeeklyRenown:0.#} renown; skipped if any are missing");
			if (d.MaxPerNation > 0)
				lines.Add($"At most {d.MaxPerNation} in your nation");
			return lines;
		}

		/// <summary>All effects in short form, for other mods' displays.</summary>
		public static List<string> EffectList(BuildingDefinition d, float strength)
		{
			var list = new List<string>();
			if (d.PopulationGrowthBonus != 0f)
				list.Add($"{d.PopulationGrowthBonus * strength:+0%;-0%} population growth");
			if (d.WeeklyWorkforce != 0)
				list.Add($"{(int)(d.WeeklyWorkforce * strength):+0;-0} workforce/week");
			foreach (var m in d.Modifiers)
				list.Add($"{Amount(m, strength)} {Nice(m.Modifier)}");
			foreach (var m in d.CountryModifiers)
				list.Add($"{Amount(m, strength)} {Nice(m.Modifier)} (nation)");
			if (d.HasWeeklyConversion)
				list.Add($"{d.WeeklyRenown * strength:+0.#} renown/week for {Conversion.Describe(d)}");
			return list;
		}

		static string Amount(ModifierEntry m, float strength) =>
			m.Percent ? (m.Value * strength).ToString("+0%;-0%") : (m.Value * strength).ToString("+0.##;-0.##");

		static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

		static string Nice(string modifier)
		{
			switch (modifier.ToUpperInvariant())
			{
				case "BLOCK_MILITA": return "militia regiment limit";
				case "RECRUITS_PERCENT": return "recruits";
				case "RECRUITS_LOYALTY_PERCENT": return "loyalty recruiting";
				case "LOYALTY_INCREMENT": return "loyalty";
				default: return modifier.ToLowerInvariant().Replace('_', ' ');
			}
		}
	}
}
