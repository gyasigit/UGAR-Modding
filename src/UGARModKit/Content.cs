// What other UGAR mods are installed, and read-only bridges to their data. Bridges use reflection so the kit loads
// whether or not those mods are installed.
//
// Adding content needs no code: UGAR Custom Buildings and UGAR British Events read every *.building.json and
// *.event.json anywhere under BepInEx\plugins, so a mod ships its files in its own plugin folder.
using System;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using World.SceneObject;

namespace UGAR.ModKit
{
	/// <summary>Other installed mods and their content.</summary>
	public static class Content
	{
		/// <summary>GUID of UGAR Custom Buildings (reads *.building.json).</summary>
		public const string CustomBuildings = "ugar.custombuildings";
		/// <summary>GUID of UGAR British Events (reads *.event.json).</summary>
		public const string BritishEvents = "ugar.britishevents";
		/// <summary>GUID of UGAR Weapon Workshop.</summary>
		public const string WeaponWorkshop = "ugar.weaponworkshop";

		/// <summary>True if a BepInEx plugin with this GUID is loaded.</summary>
		public static bool IsLoaded(string guid) =>
			IL2CPPChainloader.Instance?.Plugins != null && IL2CPPChainloader.Instance.Plugins.ContainsKey(guid);

		/// <summary>Version of a loaded plugin, or null.</summary>
		public static Version VersionOf(string guid) =>
			IL2CPPChainloader.Instance?.Plugins != null && IL2CPPChainloader.Instance.Plugins.TryGetValue(guid, out var info)
				? info.Metadata.Version is { } v ? new Version(v.Major, v.Minor, v.Patch) : null
				: null;

		/// <summary>Finished custom buildings in a settlement, each as "Name: effect, effect". Empty without Custom Buildings.</summary>
		public static string[] CustomBuildingsIn(RegionLocality settlement) =>
			settlement == null ? Array.Empty<string>() : Call<string[]>("BuildingsIn", settlement.Pointer) ?? Array.Empty<string>();

		/// <summary>Extra workforce custom buildings add to a settlement at the next weekly growth. 0 without Custom Buildings.</summary>
		public static int CustomBuildingWorkforce(RegionLocality settlement) =>
			settlement == null ? 0 : Call<int?>("WeeklyWorkforceBonus", settlement.Pointer) ?? 0;

		// Looked up on every call (not cached): the mod manager's live reload can swap Custom Buildings' assembly.
		static T Call<T>(string method, IntPtr arg)
		{
			try
			{
				if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(CustomBuildings, out var info) || info.Instance == null)
					return default;
				var api = info.Instance.GetType().Assembly.GetType("UGARCustomBuildings.Api");
				var m = api?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
				return m == null ? default : (T)m.Invoke(null, new object[] { arg });
			}
			catch (Exception e)
			{
				ModKit.Log.LogWarning($"Custom Buildings bridge {method} failed: {e.Message}");
				return default;
			}
		}
	}
}
