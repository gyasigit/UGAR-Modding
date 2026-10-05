// UGAR Bulk Recruit: recruit several identical regiments from one customization, and re-use the
// last design you built for a unit type.
//
// How the game creates a regiment:
//   LocalityCreatePanel -> RegimentManagementPanel.Show(template, locality, PanelMode.Create)
//   Show keeps the blank template as `initialRegiment` and lets the player edit a clone in `regiment`.
//   Create button -> RegimentManagementInfo.OnApplyClick -> RegimentManagementPanel.ApplyChanges:
//     UpdateChanges() checks money/officers/recruits/weapons against the player's stock (false = can't afford),
//     then reserves IDs, runs CreateCompany() for every company flagged `dirty` (this is where the cost is paid)
//     and adds the regiment to the locality garrison.
// This plugin re-runs that same ApplyChanges path for each extra copy, so copies cost exactly what the first
// one did and stop as soon as the player can no longer afford one.
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace UGARBulkRecruit
{
	[BepInPlugin(Guid, Name, Version)]
	public class Plugin : BasePlugin
	{
		public const string Guid = "ugar.bulkrecruit";
		public const string Name = "UGAR Bulk Recruit";
		public const string Version = "1.2.2";

		internal static ManualLogSource Logger;
		internal static ConfigEntry<bool> Enabled;
		internal static ConfigEntry<int> MaxCopies;
		internal static ConfigEntry<bool> ResetCopiesOnOpen;
		internal static ConfigEntry<bool> AutoBestWeapon;
		internal static ConfigEntry<bool> VerboseLog;
		internal static ConfigEntry<bool> AutoCommanders;
		internal static ConfigEntry<bool> LocalReinforcement;
		internal static ConfigEntry<int> LocalReinforcementPerDay;
		internal static ConfigEntry<int> LocalReinforcementMinRecruits;
		internal static ConfigEntry<bool> RememberDesigns;
		internal static ConfigEntry<float> WindowX;
		internal static ConfigEntry<float> WindowY;
		internal static ConfigEntry<float> UiScale;

		public override void Load()
		{
			Logger = Log;
			Enabled = Config.Bind("General", "Enabled", true, "Turn the bulk recruit window and patches on or off.");
			MaxCopies = Config.Bind("General", "MaxCopies", 20, new ConfigDescription("Highest number of identical regiments one Create click can make.", new AcceptableValueRange<int>(1, 100)));
			ResetCopiesOnOpen = Config.Bind("General", "ResetCopiesOnOpen", true, "Set the copy count back to 1 every time the recruit screen opens, so you never bulk-buy by accident.");
			AutoBestWeapon = Config.Bind("General", "AutoBestWeapon", true, "When you add a company, equip it with the best weapon you have enough of in storage instead of the template default. You can still change it.");
			VerboseLog = Config.Bind("General", "VerboseLog", true, "Write extra detail to BepInEx\\LogOutput.log (costs, stock, weapon choices) to help track down problems.");
			AutoCommanders = Config.Bind("General", "AutoCommanders", true, "The game won't create a regiment without a commander, and each officer leads one regiment. When on, each extra copy gets the best free officer from your reserve (highest level). When off, copies stop once your chosen officer is used.");
			LocalReinforcement = Config.Bind("Reinforcement", "LocalReinforcement", true, "In the base game a colonial town's regiments only get new men by supply wagon from nearby towns whose own garrisons are full, so a town full of understrength regiments can't use its own recruits. When on, such a town refills its garrison from its own recruits each day (like the game already does for garrisons in England), but only when no supply wagon is on its way. Companies still need weapons/horses in stock.");
			LocalReinforcementPerDay = Config.Bind("Reinforcement", "MaxPerDay", 50, new ConfigDescription("Most recruits one town can turn into reinforcements per day.", new AcceptableValueRange<int>(1, 1000)));
			LocalReinforcementMinRecruits = Config.Bind("Reinforcement", "MinTownRecruits", 10, new ConfigDescription("A town needs at least this many recruits before it reinforces locally.", new AcceptableValueRange<int>(1, 10000)));
			RememberDesigns = Config.Bind("General", "RememberDesigns", true, "Remember the last regiment you created for each unit type and offer a 'Use last design' button.");
			WindowX = Config.Bind("Window", "X", -1f, "Left edge of the bulk recruit box in pixels at 1080p. -1 centres it horizontally.");
			WindowY = Config.Bind("Window", "Y", 90f, "Top edge of the bulk recruit box in pixels at 1080p.");
			UiScale = Config.Bind("Window", "Scale", 1f, new ConfigDescription("Extra size multiplier for the box.", new AcceptableValueRange<float>(0.5f, 3f)));

			ClassInjector.RegisterTypeInIl2Cpp<BulkRecruitOverlay>();
			ClassInjector.RegisterTypeInIl2Cpp<BulkPanel>();
			AddComponent<BulkRecruitOverlay>();
			AddComponent<BulkPanel>();
			var harmony = new Harmony(Guid);
			harmony.PatchAll(typeof(Patches));
			harmony.PatchAll(typeof(AutoEquipPatches));
			harmony.PatchAll(typeof(ReinforceLogPatches));
			harmony.PatchAll(typeof(LocalReinforcePatches));
			harmony.PatchAll(typeof(LossLogPatches));
			Log.LogInfo($"{Name} {Version} loaded");
		}
	}

	/// <summary>A regiment as the player designed it, captured just before the game paid for it.</summary>
	internal sealed class Design
	{
		public World.RegimentInitData Snapshot;
		public bool[] DirtyCompanies;
		public string Label;
		// Recruit-screen state at the moment the player pressed Create. The game's create step can clear the
		// panel's town/template after the first regiment, so copies restore them (1.1.5).
		public World.SceneObject.RegionLocality Locality;
		public World.RegimentInitData Initial;
	}

	internal static class BulkRecruit
	{
		public static int Copies = 1;
		public static bool Busy;
		public static string Message;
		public static float MessageUntil;

		static bool _capturing;
		static bool? _captured;
		static readonly Dictionary<string, Design> _designs = new Dictionary<string, Design>();

		public static void BeginCapture()
		{
			_capturing = true;
			_captured = null;
		}

		public static void OnUpdateChanges(bool result)
		{
			if (_capturing && _captured == null)
				_captured = result;
		}

		public static bool EndCapture()
		{
			_capturing = false;
			return _captured == true;
		}

		public static string LastCaptureText => _captured == null ? "UpdateChanges never ran" : _captured.Value ? "passed" : "failed";

		/// <summary>Logs what the game's affordability check looks at, so a failed copy can be explained.</summary>
		public static void LogCostDiagnostics(World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel panel, World.RegimentInitData reg, string when)
		{
			var sb = new System.Text.StringBuilder();
			sb.Append($"Bulk recruit diagnostics ({when}): check {LastCaptureText}");
			try
			{
				var scene = MonoBehaviourSingleton<World.SceneManager>.instance;
				var country = scene?.PlayerCountry;
				var inv = country?.inventory;
				sb.Append($"; money {inv?.money:0}, officers {inv?.officers:0}");
				sb.Append($"; commander selected in panel: {panel.regimentOfficerPanel?.officer != null}");
				sb.Append($"; panel.regiment is copy: {panel.regiment?.Pointer == reg.Pointer}");
				var storage = inv?.itemStorage;
				var comps = reg.companies;
				for (int i = 0; comps != null && i < comps.Length; i++)
				{
					var c = comps[i];
					if (c == null)
						continue;
					var w = c.Weapon;
					sb.Append($"\n  company {i}: dirty {c.dirty}, hp {c.IntHP}, id {c.id}, weapon {(w == null ? "none" : w.Name)}");
					if (w != null && storage != null)
						sb.Append($" need {w.CountForHP(c.IntHP)} have {storage.GetItemCount(w):0}");
				}
			}
			catch (Exception e)
			{
				sb.Append($"\n  (diagnostics failed: {e.Message})");
			}
			Plugin.Logger.LogInfo(sb.ToString());
		}

		public static void Say(string msg)
		{
			Message = msg;
			MessageUntil = UnityEngine.Time.realtimeSinceStartup + 8f;
			Plugin.Logger.LogInfo(msg);
		}

		static string DesignKey(World.RegimentInitData r)
		{
			// Class name keeps infantry/militia/cavalry/supply apart even if they share a kind.
			return r.GetIl2CppType().FullName + "/" + r.kind;
		}

		public static Design Capture(World.RegimentInitData reg)
		{
			var comps = reg.companies;
			var dirty = new bool[comps == null ? 0 : comps.Length];
			int used = 0;
			for (int i = 0; i < dirty.Length; i++)
			{
				var c = comps[i];
				if (c == null)
					continue;
				used++;
				dirty[i] = c.dirty;
			}
			return new Design
			{
				Snapshot = reg.CloneRegiment(),
				DirtyCompanies = dirty,
				Label = $"{used} {(used == 1 ? "company" : "companies")}",
			};
		}

		public static void Remember(World.RegimentInitData template, Design d)
		{
			if (Plugin.RememberDesigns.Value && template != null)
				_designs[DesignKey(template)] = d;
		}

		public static Design LastDesignFor(World.RegimentInitData template)
		{
			if (template == null || !Plugin.RememberDesigns.Value)
				return null;
			return _designs.TryGetValue(DesignKey(template), out var d) ? d : null;
		}

		/// <summary>
		/// Fresh copy of a design that the game will treat as brand new: every company that was new in the
		/// design is flagged dirty again (the game's clone drops that flag), so ApplyChanges charges for it.
		/// The regiment commander is cleared because one officer can't lead two regiments.
		/// </summary>
		public static World.RegimentInitData MakeCopy(Design d)
		{
			var c = d.Snapshot.CloneRegiment();
			var comps = c.companies;
			if (comps != null)
			{
				for (int i = 0; i < comps.Length && i < d.DirtyCompanies.Length; i++)
				{
					var company = comps[i];
					if (company != null)
						company.dirty = d.DirtyCompanies[i];
				}
			}
			c.officer = null;
			return c;
		}

		/// <summary>Runs after the game created the first regiment; makes the extra copies the same way.</summary>
		public static void CreateCopies(World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel panel, Design d, int copies)
		{
			var original = panel.regiment;
			string baseName = null;
			if (!d.Snapshot.isNameAutomatic && !string.IsNullOrEmpty(d.Snapshot.name))
				baseName = d.Snapshot.name;

			int made = 1, swaps = 0;
			string error = null, stop = null;
			var country = Game.PlayerCountry();
			var commanders = Plugin.AutoCommanders.Value ? Game.FreeCommanders() : new List<BaseOfficer>();
			int nextCommander = 0;
			Busy = true;
			try
			{
				for (int i = 2; i <= copies; i++)
				{
					var copy = MakeCopy(d);
					if (baseName != null)
						copy.name = $"{baseName} {i}";
					if (!CostPlanner.ApplySubstitutes(copy, out var swap))
					{
						Plugin.Logger.LogInfo($"Bulk recruit: no usable weapon left for regiment {i}.");
						break;
					}
					if (swap != null)
					{
						swaps++;
						Plugin.Logger.LogInfo($"Bulk recruit: regiment {i} uses {swap}.");
					}
					RestorePanel(panel, d, i);
					panel.regiment = new World.IRegimentDataProvider(copy.Pointer);

					// The game requires a commander per regiment (UpdateChanges checks the officer picker), and the
					// first regiment used the one the player chose. Assign the next best free officer the same way the
					// picker does: OfficerSelectPanel.officer + RegimentManagementPanel.ChangeOfficer, which also takes
					// the officer out of the reserve.
					BaseOfficer commander = null;
					if (panel.regimentOfficerPanel?.officer == null)
					{
						if (nextCommander >= commanders.Count)
						{
							stop = Plugin.AutoCommanders.Value ? "no free officers left to command" : "each regiment needs its own commander";
							break;
						}
						commander = commanders[nextCommander++];
						panel.regimentOfficerPanel.Officer = commander;
						panel.ChangeOfficer(commander);
					}

					int before = GarrisonCount(d);
					BeginCapture();
					panel.ApplyChanges();
					bool passed = EndCapture();
					int after = GarrisonCount(d);
					bool inGarrison = InGarrison(d, copy);
					Plugin.Logger.LogInfo($"Bulk recruit: regiment {i}: check {(passed ? "passed" : "failed")}, id {copy.id}, commander {(commander != null ? commander.OfficerName : (copy.officer != null ? "kept" : "none"))}, garrison {before} -> {after}, in garrison {inGarrison}");
					if (passed && !inGarrison)
					{
						// The game's create step stopped part-way (e.g. threw); nothing was added.
						Plugin.Logger.LogWarning($"Bulk recruit: regiment {i} passed the game's check but did not appear in the garrison.");
						if (commander != null)
						{
							copy.officer = null;
							panel.regimentOfficerPanel.Officer = null;
							country?.AddReserveSpecialist(commander);
						}
						stop = "the game didn't add the extra regiment (see BepInEx log)";
						break;
					}
					if (!passed)
					{
						LogCostDiagnostics(panel, copy, $"copy {i} refused");
						if (commander != null)
						{
							// Not created: give the officer back.
							copy.officer = null;
							panel.regimentOfficerPanel.Officer = null;
							country?.AddReserveSpecialist(commander);
						}
						stop = "the game refused the next regiment (see BepInEx log)";
						break;
					}
					made++;
				}
			}
			catch (Exception e)
			{
				error = e.Message;
				Plugin.Logger.LogError($"Bulk recruit stopped after {made} regiment(s): {e}");
			}
			finally
			{
				Busy = false;
				panel.regiment = original;
			}

			if (error != null)
				Say($"Recruited {made} of {copies}. Stopped by an error, see BepInEx log.");
			else if (made < copies)
				Say($"Recruited {made} of {copies} regiments: {stop ?? "not enough resources"}.");
			else
				Say($"Recruited {made} regiments{(swaps > 0 ? $" ({swaps} with replacement weapons)" : "")}.");
		}

		static int GarrisonCount(Design d)
		{
			try
			{
				var units = d.Locality?.garrison?.units;
				return units == null ? -1 : units.Count;
			}
			catch (Exception)
			{
				return -1;
			}
		}

		static bool InGarrison(Design d, World.RegimentInitData reg)
		{
			try
			{
				var units = d.Locality?.garrison?.units;
				if (units == null)
					return false;
				for (int i = 0; i < units.Count; i++)
					if (units[i] != null && units[i].Pointer == reg.Pointer)
						return true;
			}
			catch (Exception) { }
			return false;
		}

		/// <summary>Puts back the recruit screen's town, blank template and Create mode if the first regiment cleared them.</summary>
		static void RestorePanel(World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel panel, Design d, int i)
		{
			var sb = new System.Text.StringBuilder();
			if (panel.Locality == null && d.Locality != null)
			{
				panel.Locality = d.Locality;
				sb.Append(" town");
			}
			if (panel.initialRegiment == null && d.Initial != null)
			{
				panel.initialRegiment = d.Initial;
				sb.Append(" template");
			}
			if (panel.mode != World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel.PanelMode.Create)
			{
				panel.mode = World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel.PanelMode.Create;
				sb.Append(" mode");
			}
			if (sb.Length > 0)
				Plugin.Logger.LogInfo($"Bulk recruit: restored the recruit screen's{sb} before regiment {i}.");
		}

		/// <summary>Loads a remembered design into the recruit screen that is open right now.</summary>
		public static void LoadDesign(World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel panel, Design d)
		{
			var template = panel.initialRegiment;
			var copy = MakeCopy(d);
			copy.name = template.name;
			copy.isNameAutomatic = template.isNameAutomatic;
			copy.position = template.position;
			copy.rotation = template.rotation;
			copy.ownerNation = template.ownerNation;
			copy.startNation = template.startNation;
			panel.Regiment = new World.IRegimentDataProvider(copy.Pointer);
			panel.changed = true;
			panel.UpdateChanges();
			Say($"Loaded your last design ({d.Label}). Check the cost, then press Create.");
		}
	}
}


