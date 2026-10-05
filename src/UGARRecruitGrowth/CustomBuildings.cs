using System;
using System.Reflection;
using World.SceneObject;

namespace UGARRecruitGrowth
{
	/// <summary>
	/// Optional link to UGAR Custom Buildings (UGARCustomBuildings.Api), found by reflection so either mod works alone.
	/// Its buildings add weekly workforce (applied after Region.CalculatePopulationGrows) and may carry RECRUITS_PERCENT,
	/// which the game already folds into the settlement recruits modifier.
	/// </summary>
	internal static class CustomBuildings
	{
		static bool _looked;
		static Func<IntPtr, int> _workforce;
		static Func<IntPtr, string[]> _list;

		static void Lookup()
		{
			if (_looked)
				return;
			_looked = true;
			try
			{
				// The mod manager's live reload loads new copies as "UGARCustomBuildings.Reload<n>": use the newest.
				Assembly found = null;
				int foundGen = -1;
				foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
				{
					string name = a.GetName().Name;
					int gen = name == "UGARCustomBuildings" ? 0
						: name.StartsWith("UGARCustomBuildings.Reload", StringComparison.Ordinal) && int.TryParse(name.Substring("UGARCustomBuildings.Reload".Length), out var g) ? g
						: -1;
					if (gen > foundGen)
					{
						found = a;
						foundGen = gen;
					}
				}
				foreach (var asm in found == null ? Array.Empty<Assembly>() : new[] { found })
				{
					var api = asm.GetType("UGARCustomBuildings.Api");
					var w = api?.GetMethod("WeeklyWorkforceBonus", BindingFlags.Public | BindingFlags.Static);
					var b = api?.GetMethod("BuildingsIn", BindingFlags.Public | BindingFlags.Static);
					if (w != null) _workforce = (Func<IntPtr, int>)Delegate.CreateDelegate(typeof(Func<IntPtr, int>), w);
					if (b != null) _list = (Func<IntPtr, string[]>)Delegate.CreateDelegate(typeof(Func<IntPtr, string[]>), b);
					Plugin.Logger.LogInfo("Recruit growth includes UGAR Custom Buildings.");
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"UGAR Custom Buildings link failed: {e.Message}");
			}
		}

		public static int WeeklyWorkforce(RegionLocality l)
		{
			Lookup();
			return _workforce != null && l != null ? _workforce(l.Pointer) : 0;
		}

		public static string[] In(RegionLocality l)
		{
			Lookup();
			return _list != null && l != null ? _list(l.Pointer) ?? Array.Empty<string>() : Array.Empty<string>();
		}
	}
}
