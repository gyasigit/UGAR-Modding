// Writes the game's own settlement buildings (costs, build points, effects) to a text file, so building authors can
// pick templates and balance their numbers against the real ones.
using System;
using System.IO;
using System.Text;
using BepInEx;
using World;
using World.Configuration;

namespace UGARCustomBuildings
{
	internal static class Dump
	{
		public static string FilePath => Path.Combine(Paths.ConfigPath, "ugar.custombuildings.game-buildings.txt");

		public static void Write(ConstructionConfig cc)
		{
			var sb = new StringBuilder();
			sb.AppendLine("# UGAR settlement buildings, written by UGAR Custom Buildings at game start.");
			sb.AppendLine("# Use the names in the first column as \"template\" or \"requires\" in a *.building.json file.");
			var rc = Config.game?.region;
			if (rc != null)
				sb.AppendLine($"# Weekly growth: population x {rc.populationGrowthPercent:0.#####}, of which x {rc.workforcePercent:0.###} becomes workforce. Recruiting basis {rc.recruitingBasis:0.#####}.");
			for (int t = 0; t < cc.localityBuildings.Length; t++)
			{
				var list = cc.localityBuildings[t]?.constuctions;
				sb.AppendLine();
				sb.AppendLine($"## {(ERegionLocality)t} ({list?.Count ?? 0} buildings)");
				if (list == null) continue;
				for (int i = 0; i < list.Count; i++)
				{
					var s = list[i];
					if (s == null) continue;
					sb.AppendLine($"{Registry.GameName(s),-28} \"{Safe(() => s.Header)}\"  asset {s.name}");
					sb.AppendLine($"    cost {Registry.Costs(s.cost)}; {s.pointsToConstruct:0.#} construction points; upkeep {Registry.Costs(s.upkeep)}");
					var fx = s.effects;
					if (fx != null && fx.Length > 0)
					{
						var parts = new StringBuilder();
						foreach (var m in fx)
							if (m != null) parts.Append($"{m.type} {m.effect:+0.####;-0.####}{(m.percent ? " (percent)" : "")}; ");
						sb.AppendLine($"    effects {parts}scaling {s.effectsModifier}{(s.roundToInt ? ", rounded" : "")}");
					}
					if (s.upgrades != null && s.upgrades.Length > 0)
					{
						var ups = new StringBuilder();
						foreach (var u in s.upgrades)
							if (u != null) ups.Append(Registry.GameName(u)).Append("; ");
						sb.AppendLine($"    upgrades to {ups}");
					}
					sb.AppendLine($"    nations {s.availableNations}{(s.hasResourceCondition ? $", needs {s.requiredResource} in region" : "")}");
				}
			}
			File.WriteAllText(FilePath, sb.ToString());
			Plugin.Logger.LogInfo($"Wrote the game's settlement buildings to {FilePath}");
		}

		static string Safe(Func<string> f)
		{
			try { return f(); } catch { return "?"; }
		}
	}
}
