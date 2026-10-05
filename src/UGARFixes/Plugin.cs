using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace UGARFixes;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "ugar.community.fixes";
    public const string Name = "UGAR Community Fixes";
    public const string Version = "0.1.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.FullName}.{m.Name}");
        AddComponent<DiagnosticsHotkeys>();
        Log.LogInfo($"{Name} {Version} loaded");
    }
}
