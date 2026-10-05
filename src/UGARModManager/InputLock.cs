// While the player types in one of the window's text boxes (or presses a key to bind it), the game must not see the
// keys. The campaign map's World.Input.WorldInputController.Update returns before its hotkeys (pause, speed, quick
// save = UserControlsController binding +0xbc, quick load = +0xc0, Esc, unit shortcuts) when
// SceneManager.IsInputLocked is true. The game sets it for a
// focused TMP_InputField, but an IMGUI text field doesn't count, so typing "emigration-ships-stayed" into a setting
// triggered the game's quick load ("Could not find file ...\Save\QuickSave").
using System.Reflection;
using HarmonyLib;

namespace UGARModManager
{
	internal static class InputLock
	{
		/// <summary>Set by the window each frame: a text field has keyboard focus or a key is being captured.</summary>
		public static bool Typing;

		// By name, so the manager keeps no compile-time reference to the game assembly.
		[HarmonyPatch]
		internal static class IsInputLockedPatch
		{
			static MethodBase TargetMethod() =>
				AccessTools.PropertyGetter(AccessTools.TypeByName("World.SceneManager"), "IsInputLocked");

			static void Postfix(ref bool __result)
			{
				if (Typing)
					__result = true;
			}
		}
	}
}
