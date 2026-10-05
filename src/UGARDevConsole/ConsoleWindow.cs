using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using GameConsole = UltimateAdmiral.Console;
using ConsoleBar = UltimateAdmiral.ConsoleBar;

namespace UGARDevConsole;

// Our own console window. The game's Console.OnGUI key handling never fires, so this
// component owns the key and the drawing, and runs commands through the game's ConsoleBar.ProcessConsoleCommand.
// The bar is the one the game's Console.Init() made (remembered in Plugin.Bar), or our own if that never happened.
public class ConsoleWindow : MonoBehaviour
{
    public ConsoleWindow(IntPtr ptr) : base(ptr) { }

    const int MaxLines = 400;
    static readonly List<string> Lines = new();
    static bool _scrollToEnd;

    bool _open;
    bool _broken;
    string _input = "";
    Vector2 _scroll;
    int _toggledFrame = -1;
    int _historyPos = -1;
    readonly List<string> _history = new();
    float _nextSearch;
    EventSystem _blockedEventSystem;
    GUIStyle _line, _field, _hint;
    Texture2D _bg;

    float Scale => Screen.height / 1080f;

    internal static void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var l in text.Replace("\r", "").Split('\n'))
            Lines.Add(l);
        if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
        _scrollToEnd = true;
    }

    // 1.1.0/1.1.1 looked up the game's Console component at key time and found nothing (log: "game console not
    // found" right after "Console initialized ... (bar created)"). Commands only need the ConsoleBar, a plain
    // object, so use the one remembered at Init (Plugin.Bar). Diagnostics say which step was used.
    ConsoleBar FindBar(bool log)
    {
        if (Plugin.Bar != null) return Plugin.Bar;
        if (Time.realtimeSinceStartup < _nextSearch) return null;
        _nextSearch = Time.realtimeSinceStartup + 1f;
        int total = 0;
        foreach (var c in Resources.FindObjectsOfTypeAll<GameConsole>())
        {
            total++;
            if (c == null) continue;
            if (log)
                Plugin.Log.LogInfo($"Console lookup: found Console in scene '{c.gameObject.scene.name}' (active={c.gameObject.activeInHierarchy}, enabled={c.enabled}, bar={(c.bar != null ? "set" : "null")})");
            if (Plugin.EnsureInit(c)) return Plugin.Bar;
        }
        if (log) Plugin.Log.LogInfo($"Console lookup: no remembered bar, {total} Console object(s) in memory; will create a bar when the window draws");
        return null;
    }

    // Last resort, from OnGUI (the ConsoleBar constructor builds IMGUI styles): our own bar. Commands are static
    // methods on ConsoleMethods, so they don't depend on the game's Console object.
    ConsoleBar CreateBar()
    {
        if (_createFailed || Plugin.Bar != null) return Plugin.Bar;
        try
        {
            Plugin.Bar = new ConsoleBar(5f, 21f, 200f); // same arguments Console.Init uses
            Plugin.Log.LogInfo($"Console lookup: created our own ConsoleBar (consoleMethods={(Plugin.Bar.consoleMethods != null ? "set" : "null")})");
        }
        catch (Exception e)
        {
            _createFailed = true;
            Plugin.Log.LogError($"Console lookup: could not create a ConsoleBar: {e}");
        }
        return Plugin.Bar;
    }

    bool _createFailed;

    void Toggle(string via)
    {
        if (_toggledFrame == Time.frameCount) return; // Input and Event can both report the same press
        _toggledFrame = Time.frameCount;
        _open = !_open;
        var bar = _open ? FindBar(true) : null;
        Plugin.Log.LogInfo($"Console window {(_open ? "opened" : "closed")} ({via}); command bar {(_open ? (bar != null ? "ready" : "not yet") : "-")}");
        if (_open && Lines.Count == 0)
            Append("Developer console. Type 'list' for all commands, 'help <command>' for its arguments. Esc or ~ closes.");
        if (!_open) ReleaseClicks();
        _input = "";
    }

    void Update()
    {
        if (_broken) return;
        try
        {
            if (!Plugin.Enabled.Value)
            {
                if (_open) Toggle("mod switched off");
                return;
            }
            var key = Plugin.ToggleKey.Value;
            if (key != KeyCode.None && Input.GetKeyDown(key)) Toggle($"Input.GetKeyDown({key})");
            else if (_open && Input.GetKeyDown(KeyCode.Escape)) Toggle("Escape");

            if (_open)
            {
                var es = EventSystem.current; // keep the game's UI from taking clicks under the window
                if (es != null && es.enabled) { es.enabled = false; _blockedEventSystem = es; }
            }
        }
        catch (Exception e)
        {
            _broken = true;
            Plugin.Log.LogError($"Console window disabled after an error in Update: {e}");
        }
    }

    void ReleaseClicks()
    {
        if (_blockedEventSystem == null) return;
        try { _blockedEventSystem.enabled = true; } catch (Exception) { }
        _blockedEventSystem = null;
    }

    void OnDestroy() => ReleaseClicks();

    void OnGUI()
    {
        if (_broken || !Plugin.Enabled.Value) return;
        try
        {
            var e = Event.current;
            var key = Plugin.ToggleKey.Value;
            if (e.type == EventType.KeyDown && key != KeyCode.None && e.keyCode == key)
            {
                Toggle($"Event {key}");
                e.Use();
                return;
            }
            if (!_open) return;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Toggle("Escape");
                e.Use();
                return;
            }
            GUI.depth = -900;
            var saved = GUI.matrix;
            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            Draw(e, Screen.width / s);
            GUI.matrix = saved;
        }
        catch (Exception ex)
        {
            _broken = true;
            ReleaseClicks();
            Plugin.Log.LogError($"Console window disabled after an error in OnGUI: {ex}");
        }
    }

    void InitStyles()
    {
        if (_line != null) return;
        _bg = new Texture2D(1, 1);
        _bg.SetPixel(0, 0, new Color(0.08f, 0.07f, 0.05f, 0.92f));
        _bg.Apply();
        _line = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15, richText = false };
        _line.normal.textColor = new Color(0.92f, 0.88f, 0.78f);
        _field = new GUIStyle(GUI.skin.textField) { fontSize = 16 };
        _hint = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        _hint.normal.textColor = new Color(0.8f, 0.67f, 0.42f);
    }

    void Draw(Event e, float width)
    {
        InitStyles();
        const float h = 520f;
        var area = new Rect(0f, 0f, width, h);
        GUI.DrawTexture(area, _bg);
        GUILayout.BeginArea(new Rect(10f, 6f, width - 20f, h - 12f));

        var bar = FindBar(false) ?? CreateBar();
        GUILayout.Label(bar != null
            ? "Developer console — Enter runs a command, Up/Down for history, Esc or ~ closes"
            : "Developer console — command system unavailable (see BepInEx log). Esc or ~ closes", _hint);

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        foreach (var l in Lines) GUILayout.Label(l, _line);
        GUILayout.EndScrollView();
        if (_scrollToEnd && e.type == EventType.Repaint) { _scroll.y = float.MaxValue; _scrollToEnd = false; }

        bool moveCaretToEnd = false;
        if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "ugarConsoleInput")
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Run(bar, _input); _input = ""; ResetTab(); e.Use(); }
            else if (e.keyCode == KeyCode.UpArrow) { StepHistory(-1); moveCaretToEnd = true; e.Use(); }
            else if (e.keyCode == KeyCode.DownArrow) { StepHistory(1); moveCaretToEnd = true; e.Use(); }
            else if (e.keyCode == KeyCode.Tab || e.character == '\t')
            {
                // Eat Tab (and its character event) so it neither moves IMGUI focus nor reaches the game.
                if (e.keyCode == KeyCode.Tab && bar != null) { TabComplete(bar); moveCaretToEnd = true; }
                e.Use();
            }
        }
        GUI.SetNextControlName("ugarConsoleInput");
        var typed = GUILayout.TextField(_input ?? "", _field).Replace("`", "").Replace("~", "").Replace("\t", "");
        if (typed != _input) { _input = typed; if (_input != _tabResult) ResetTab(); }
        GUI.FocusControl("ugarConsoleInput");
        if (moveCaretToEnd) MoveCaretToEnd();
        if (bar != null) DrawSuggestions(bar);
        GUILayout.EndArea();
    }

    // ---- Tab completion and inline help ----

    readonly List<string> _candidates = new();
    string _tabBase, _tabResult;
    int _tabCycle;

    void ResetTab() { _tabBase = null; _tabResult = null; _tabCycle = 0; _candidates.Clear(); }

    void TabComplete(ConsoleBar bar)
    {
        if (_tabBase == null || _input != _tabResult) { _tabBase = _input; _tabCycle = 0; }
        else _tabCycle++;
        _input = Commands.Complete(bar, _tabBase, _tabCycle, _candidates);
        _tabResult = _input;
    }

    void MoveCaretToEnd()
    {
        try
        {
            var te = GUIUtility.GetStateObject(Il2CppInterop.Runtime.Il2CppType.Of<TextEditor>(), GUIUtility.keyboardControl).TryCast<TextEditor>();
            if (te == null) return;
            te.text = _input;
            te.MoveTextEnd();
        }
        catch (Exception) { /* caret stays where it was */ }
    }

    // Under the input: the Tab choices, or the command being typed with its description.
    void DrawSuggestions(ConsoleBar bar)
    {
        var first = (_input ?? "").TrimStart().Split(' ')[0];
        if (first.Length == 0) { GUILayout.Label("Type 'list' for every command with what it does. Tab completes names.", _hint); return; }
        if (_candidates.Count > 1)
        {
            GUILayout.Label("Tab again to cycle: " + string.Join("  ", _candidates.Take(30)) + (_candidates.Count > 30 ? "  ..." : ""), _hint);
            return;
        }
        var exact = Commands.Find(bar, first);
        if (exact != null) { GUILayout.Label(Commands.Line(exact), _hint); return; }
        var matches = Commands.All(bar).Where(c => c.Name.StartsWith(first, StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        foreach (var c in matches) GUILayout.Label(Commands.Line(c), _hint);
    }

    static string[] _commandNames;

    // The game looks commands up with Type.GetMethod (case-sensitive): "money 500" -> "Money 500".
    static string FixCase(string cmd)
    {
        _commandNames ??= Array.ConvertAll(
            typeof(UltimateAdmiral.ConsoleMethods).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            m => m.Name);
        int sp = cmd.IndexOf(' ');
        var name = sp < 0 ? cmd : cmd.Substring(0, sp);
        foreach (var n in _commandNames)
            if (n != name && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return n + (sp < 0 ? "" : cmd.Substring(sp));
        return cmd;
    }

    void StepHistory(int dir)
    {
        if (_history.Count == 0) return;
        _historyPos = _historyPos < 0 ? _history.Count : _historyPos;
        _historyPos = Mathf.Clamp(_historyPos + dir, 0, _history.Count);
        _input = _historyPos < _history.Count ? _history[_historyPos] : "";
    }

    void Run(ConsoleBar bar, string cmd)
    {
        cmd = (cmd ?? "").Trim();
        if (cmd.Length == 0) return;
        _history.Add(cmd);
        _historyPos = -1;
        Append("> " + cmd);
        if (bar == null)
        {
            Append("The command system could not be started; see BepInEx\\LogOutput.log (lines starting with Console lookup).");
            return;
        }
        try
        {
            if (cmd == "clear") { Lines.Clear(); return; }
            var parts = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var l in Commands.ListText(bar)) Append(l);
                return;
            }
            if (parts[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length == 1) { Append("help <command> explains a command. 'list' shows all of them. Tab completes."); return; }
                var c = Commands.Find(bar, parts[1]);
                if (c == null) { Append($"No command called {parts[1]}. Try 'list' or Tab."); return; }
                Append(Commands.Line(c));
                if (c.TakesNation) Append("  Nations: " + string.Join(", ", Commands.Nations));
                return;
            }
            cmd = FixCase(cmd);
            bar.ProcessConsoleCommand(cmd); // the game's parser: reflection on ConsoleMethods, output via ConsoleMessage
        }
        catch (Exception ex)
        {
            Append($"Error: {ex.Message}");
            Plugin.Log.LogWarning($"Console command '{cmd}' failed: {ex}");
        }
    }
}
