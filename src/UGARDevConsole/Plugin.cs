using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using GameConsole = UltimateAdmiral.Console;
using ConsoleBar = UltimateAdmiral.ConsoleBar;

namespace UGARDevConsole;

// Turns on the developer console that ships in the campaign map scenes (GlobalMap_AR / _Britain).
// The game creates it in Console.Start only when Debug.isDebugBuild is true, so in the retail build the bar is
// never made. We call Console.Init() ourselves (it registers the ~90 ConsoleMethods commands).
// 1.0.0 relied on the game's Console.OnGUI to react to `/~ (KeyCode.BackQuote); that never fired in the retail
// build, so 1.1.0 draws its own window (ConsoleWindow) and only uses the game's ConsoleBar to run commands.
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "ugar.devconsole";
    public const string Name = "UGAR Developer Console";
    public const string Version = "1.2.0";

    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<bool> LogCommands;
    // The game's Console component is gone by the time the player can press a key (retail build destroys it after
    // Start; its OnGUI never runs). The ConsoleBar it created is a plain object, so keep our own reference to it.
    internal static ConsoleBar Bar;

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("General", "Enabled", true,
            "Developer console on the campaign map. Off hides it immediately.");
        ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.BackQuote,
            "Key that opens/closes the console. BackQuote is the `/~ key on US keyboards; Escape also closes it.");
        LogCommands = Config.Bind("General", "LogCommands", true,
            new ConfigDescription("Write every console command and its output to the BepInEx log.", null, "Advanced"));

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.FullName}.{m.Name}");

        ClassInjector.RegisterTypeInIl2Cpp<ConsoleWindow>();
        AddComponent<ConsoleWindow>();
        Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value} on the campaign map to open the console.");
    }

    // Creates the bar on first use. Returns false when the console can't be used right now.
    internal static bool EnsureInit(GameConsole console)
    {
        if (console.bar != null) { Bar = console.bar; return true; }
        if (!Enabled.Value) return false;
        try
        {
            console.Init();
            if (console.bar != null) Bar = console.bar;
            Log.LogInfo($"Console initialized on '{console.gameObject.scene.name}' (bar {(console.bar != null ? "created" : "still null")})");
        }
        catch (Exception e)
        {
            Log.LogError($"Console.Init failed: {e}");
        }
        return console.bar != null;
    }

    internal static void Output(string text)
    {
        ConsoleWindow.Append(text);
        if (LogCommands.Value) Log.LogInfo($"Console: {text}");
    }
}

[HarmonyPatch(typeof(GameConsole), nameof(GameConsole.Start))]
internal static class ConsoleStartPatch
{
    static void Postfix(GameConsole __instance)
    {
        Plugin.Log.LogInfo($"Game console Start in scene '{__instance.gameObject.scene.name}'");
        Plugin.EnsureInit(__instance);
    }
}

// Update runs the bar's queued commands. Without a bar it would throw every frame, and when switched off we skip it.
[HarmonyPatch(typeof(GameConsole), nameof(GameConsole.Update))]
internal static class ConsoleUpdatePatch
{
    static bool Prefix(GameConsole __instance) => Plugin.Enabled.Value && Plugin.EnsureInit(__instance);
}

// The game's own IMGUI bar (FPS strip + log) is replaced by ConsoleWindow; never draw it.
[HarmonyPatch(typeof(GameConsole), nameof(GameConsole.OnGUI))]
internal static class ConsoleOnGUIPatch
{
    static bool _logged;

    static bool Prefix()
    {
        if (!_logged) { _logged = true; Plugin.Log.LogInfo("Game console OnGUI is being called (suppressed; our window draws instead)"); }
        return false;
    }
}

[HarmonyPatch(typeof(ConsoleBar), nameof(ConsoleBar.ConsoleMessage))]
internal static class ConsoleMessagePatch
{
    static void Prefix(string __0) => Plugin.Output(__0);
}

[HarmonyPatch(typeof(GameConsole), nameof(GameConsole.Log))]
internal static class ConsoleLogPatch
{
    static void Prefix(string __0) => Plugin.Output(__0);
}

[HarmonyPatch(typeof(GameConsole), nameof(GameConsole.LogWarning))]
internal static class ConsoleLogWarningPatch
{
    static void Prefix(string __0) => Plugin.Output("Warning: " + __0);
}

[HarmonyPatch(typeof(GameConsole), nameof(GameConsole.LogError))]
internal static class ConsoleLogErrorPatch
{
    static void Prefix(string __0) => Plugin.Output("Error: " + __0);
}

[HarmonyPatch(typeof(ConsoleBar), nameof(ConsoleBar.ProcessConsoleCommand))]
internal static class CommandLogPatch
{
    static void Prefix(string __0)
    {
        if (Plugin.LogCommands.Value) Plugin.Log.LogInfo($"Console command: {__0}");
    }
}
