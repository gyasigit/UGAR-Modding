// Read-only queries for other plugins (UGAR Recruit Growth calls these through reflection, so neither mod needs the
// other to load). Arguments are il2cpp object pointers (Il2CppObjectBase.Pointer).
using System;
using System.Collections.Generic;
using World.SceneObject;

namespace UGARCustomBuildings
{
	public static class Api
	{
		/// <summary>Extra workforce custom buildings add to this settlement at the next weekly population growth.</summary>
		public static int WeeklyWorkforceBonus(IntPtr locality)
		{
			try
			{
				return locality == IntPtr.Zero ? 0 : Growth.Weekly(new RegionLocality(locality)).Workforce;
			}
			catch
			{
				return 0;
			}
		}

		/// <summary>Finished custom buildings in this settlement, each as "Name: effect, effect".</summary>
		public static string[] BuildingsIn(IntPtr locality)
		{
			var list = new List<string>();
			try
			{
				if (locality == IntPtr.Zero || !Plugin.Enabled.Value)
					return list.ToArray();
				foreach (var (e, strength) in Growth.Built(new RegionLocality(locality)))
				{
					var fx = Texts.EffectList(e.Def, strength);
					list.Add(fx.Count > 0 ? $"{e.Def.Name}: {string.Join(", ", fx)}" : e.Def.Name);
				}
			}
			catch
			{
			}
			return list.ToArray();
		}

		/// <summary>True if the settings pointer is a custom building.</summary>
		public static bool IsCustomBuilding(IntPtr settings) => Registry.Find(settings) != null;
	}
}
