// UGAR ModKit: a shared library for UGAR mods. It does nothing on its own; other mods reference UGARModKit.dll and
// call its static classes (Game, SaveData, Treasury, Popups, Modifiers, Images, Content). See docs/modkit.md.
//
// API stability: 0.x is a preview. Within 0.x a minor version may change the API; from 1.0 on, only a major version
// will remove or change public members. Check ModKit.ApiLevel if you need a newer feature.
using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace UGAR.ModKit
{
	/// <summary>Version information. Mods that need the kit declare <c>[BepInDependency(ModKit.Guid)]</c>.</summary>
	public static class ModKit
	{
		/// <summary>BepInEx plugin GUID of the kit.</summary>
		public const string Guid = "ugar.modkit";

		/// <summary>Kit version (SemVer).</summary>
		public const string Version = "0.1.0";

		/// <summary>Goes up by one whenever public API is added. 1 = the 0.1.0 API.</summary>
		public const int ApiLevel = 1;

		internal static ManualLogSource Log;
	}

	/// <summary>The BepInEx plugin that hosts the kit. Mods don't use this class directly.</summary>
	[BepInPlugin(ModKit.Guid, "UGAR ModKit", ModKit.Version)]
	public class Plugin : BasePlugin
	{
		/// <inheritdoc />
		public override void Load()
		{
			ModKit.Log = Log;
			ClassInjector.RegisterTypeInIl2Cpp<Runner>();
			AddComponent<Runner>();
			var harmony = new Harmony(ModKit.Guid);
			harmony.CreateClassProcessor(typeof(Popups.CanBeExecutedPatch)).Patch();
			harmony.CreateClassProcessor(typeof(Popups.ExecutePatch)).Patch();
			Log.LogInfo($"UGAR ModKit {ModKit.Version} (API level {ModKit.ApiLevel}) loaded.");
		}
	}

	/// <summary>Drives the Game events every frame. Internal plumbing; public only because il2cpp injection needs it.</summary>
	public class Runner : MonoBehaviour
	{
		/// <summary>il2cpp constructor.</summary>
		public Runner(IntPtr ptr) : base(ptr) { }

		float _nextError;

		void Update()
		{
			try
			{
				Game.Tick();
			}
			catch (Exception e)
			{
				if (Time.realtimeSinceStartup >= _nextError)
				{
					_nextError = Time.realtimeSinceStartup + 30f;
					ModKit.Log.LogError($"ModKit update failed: {e}");
				}
			}
		}
	}
}
