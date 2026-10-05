using System;
using System.Diagnostics;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UGARModManager
{
	/// <summary>The IMGUI mod manager window. Sizes are in pixels at 1080p and scaled to the screen.</summary>
	public class ManagerWindow : MonoBehaviour
	{
		public ManagerWindow(IntPtr ptr) : base(ptr) { }

		const float W = 760f, H = 800f, TitleH = 30f;
		const float ButtonW = 64f, ButtonH = 24f;

		bool _open;
		bool _broken;
		Rect _rect = new Rect(-1f, 80f, W, H);
		Vector2 _scroll;
		bool _dragging;
		Vector2 _dragOffset;
		string _status;
		float _statusUntil;
		SettingInfo _capturingKey;

		EventSystem _blockedEventSystem;

		GUIStyle _wrap, _small, _header, _title, _warn, _section;

		float Scale => Screen.height / 1080f * Plugin.UiScale.Value;

		void Open()
		{
			_open = true;
			try
			{
				ModCatalog.Refresh();
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Could not list mods: {e}");
				Say("Could not list mods, see BepInEx log.");
			}
		}

		void Close()
		{
			_open = false;
			_capturingKey = null;
			_dragging = false;
			GUIUtility.keyboardControl = 0;
			InputLock.Typing = false;
			ReleaseClicks();
		}

		void Say(string msg)
		{
			_status = msg;
			_statusUntil = Time.realtimeSinceStartup + 8f;
		}

		// ---- Click blocking: while the cursor is over our window the game's uGUI shouldn't react. ----

		bool _hooked;
		string _reloadRequest;

		void Update()
		{
			try
			{
				if (!_hooked)
				{
					_hooked = true;
					HotReload.Message += msg => Say(msg);
				}
				HotReload.Tick();
				if (_reloadRequest != null)
				{
					string guid = _reloadRequest;
					_reloadRequest = null;
					HotReload.Reload(guid, "Reload clicked");
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Live reload: {e}");
			}

			if (_broken)
				return;
			try
			{
				bool over = _open && Plugin.BlockGameClicks.Value && MouseOverWindow();
				if (over)
				{
					var es = EventSystem.current;
					if (es != null && es.enabled)
					{
						es.enabled = false;
						_blockedEventSystem = es;
					}
				}
				else
				{
					ReleaseClicks();
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Click blocking turned off after an error: {e.Message}");
				Plugin.BlockGameClicks.Value = false;
				ReleaseClicks();
			}
		}

		bool MouseOverWindow()
		{
			float s = Scale;
			var m = Input.mousePosition;
			var p = new Vector2(m.x / s, (Screen.height - m.y) / s);
			return _rect.Contains(p);
		}

		void ReleaseClicks()
		{
			if (_blockedEventSystem == null)
				return;
			try
			{
				_blockedEventSystem.enabled = true;
			}
			catch (Exception)
			{
				// Destroyed with its scene; nothing to restore.
			}
			_blockedEventSystem = null;
		}

		void OnDestroy()
		{
			InputLock.Typing = false;
			ReleaseClicks();
		}

		// ---- Drawing ----

		void OnGUI()
		{
			if (_broken)
				return;
			try
			{
				GUI.depth = -1000;
				Draw();
			}
			catch (Exception e)
			{
				_broken = true;
				ReleaseClicks();
				Plugin.Logger.LogError($"Mod manager window disabled after an error: {e}");
			}
		}

		void InitStyles()
		{
			if (_wrap != null)
				return;
			_wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
			_small = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
			_small.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
			_header = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 15 };
			_title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 16 };
			_warn = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold };
			_warn.normal.textColor = new Color(1f, 0.8f, 0.3f);
			_section = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
			_section.normal.textColor = new Color(0.6f, 0.85f, 1f);
		}

		void Draw()
		{
			var e = Event.current;
			if (_capturingKey != null && e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
			{
				if (e.keyCode != KeyCode.Escape)
					SetValue(FindMod(_capturingKey), _capturingKey, e.keyCode);
				_capturingKey = null;
				e.Use();
				return;
			}
			if (e.type == EventType.KeyDown && e.keyCode == Plugin.ToggleKey.Value && e.keyCode != KeyCode.None)
			{
				if (_open) Close(); else Open();
				e.Use();
				return;
			}

			InitStyles();
			float scale = Scale;
			var saved = GUI.matrix;
			GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
			float sw = Screen.width / scale, sh = Screen.height / scale;

			if (!_open && Plugin.ShowButton.Value)
			{
				var corner = Plugin.ButtonPosition.Value;
				float bx = corner == ButtonCorner.BottomLeft || corner == ButtonCorner.TopLeft ? 4f : sw - ButtonW - 4f;
				float by = corner == ButtonCorner.TopLeft || corner == ButtonCorner.TopRight ? 4f : sh - ButtonH - 4f;
				if (GUI.Button(new Rect(bx, by, ButtonW, ButtonH), UpdateCheck.Available != null ? "Mods !" : "Mods"))
					Open();
			}

			if (_open)
				DrawWindow(sw, sh);

			GUI.matrix = saved;
			// Keep the game's hotkeys (quick save/load...) away from text being typed here (InputLock.cs).
			InputLock.Typing = _open && (GUIUtility.keyboardControl != 0 || _capturingKey != null);
		}

		void DrawWindow(float sw, float sh)
		{
			_rect.width = Mathf.Min(W, sw - 8f);
			_rect.height = Mathf.Min(H, sh - 8f);
			if (_rect.x < 0f)
				_rect.x = (sw - _rect.width) / 2f;
			_rect.x = Mathf.Clamp(_rect.x, 0f, sw - _rect.width);
			_rect.y = Mathf.Clamp(_rect.y, 0f, sh - _rect.height);

			HandleDrag();

			// Two boxes for a darker, easier to read background.
			GUI.Box(_rect, "");
			GUI.Box(_rect, "");
			GUI.Box(new Rect(_rect.x, _rect.y, _rect.width, TitleH), "");
			GUI.Label(new Rect(_rect.x + 10f, _rect.y + 5f, 400f, 24f), $"{Plugin.Name} {Plugin.Version}", _title);
			if (GUI.Button(new Rect(_rect.xMax - 34f, _rect.y + 3f, 30f, 24f), "X"))
			{
				Close();
				return;
			}

			GUILayout.BeginArea(new Rect(_rect.x + 8f, _rect.y + TitleH + 6f, _rect.width - 16f, _rect.height - TitleH - 12f));

			GUILayout.BeginHorizontal();
			bool adv = GUILayout.Toggle(Plugin.ShowAdvanced.Value, " Show advanced settings");
			if (adv != Plugin.ShowAdvanced.Value)
				Plugin.ShowAdvanced.Value = adv;
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
				Open();
			if (GUILayout.Button("Open mods folder", GUILayout.Width(140f)))
				OpenFolder(Paths.PluginPath);
			if (GUILayout.Button("Open config folder", GUILayout.Width(150f)))
				OpenFolder(Paths.ConfigPath);
			GUILayout.EndHorizontal();

			GUILayout.Label($"Press {Plugin.ToggleKey.Value} to open or close this window. Changes are saved straight away.", _small);

			var update = UpdateCheck.Available;
			if (update != null)
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label($"UGAR Mod Pack {update} is available (you have {UpdateCheck.Installed}). Download it, close the game and run Install.bat.", _warn);
				if (GUILayout.Button("Get update", GUILayout.Width(100f)))
					Application.OpenURL(UpdateCheck.AvailableUrl ?? UpdateCheck.ReleasesUrl);
				GUILayout.EndHorizontal();
			}

			GUILayout.BeginHorizontal();
			bool live = GUILayout.Toggle(Plugin.WatchModFiles.Value, " Live reload: apply updated mods and data files without restarting");
			if (live != Plugin.WatchModFiles.Value)
				Plugin.WatchModFiles.Value = live;
			GUILayout.EndHorizontal();
			if (HotReload.History.Count > 0)
				GUILayout.Label("Last: " + HotReload.History[HotReload.History.Count - 1], _small);

			var pendingMods = ModCatalog.Mods.Where(m => m.PendingRestart).Select(m => m.Name).ToList();
			if (pendingMods.Count > 0 || ModCatalog.RestartReasons.Count > 0)
			{
				var all = pendingMods.Select(n => n + " (on/off)").Concat(ModCatalog.RestartReasons);
				GUILayout.Label("Restart the game to apply: " + string.Join(", ", all) + ". Settings marked * also apply when you click the mod's Reload.", _warn);
			}
			if (_status != null && Time.realtimeSinceStartup < _statusUntil)
				GUILayout.Label(_status, _warn);

			_scroll = GUILayout.BeginScrollView(_scroll);
			string category = null;
			foreach (var mod in ModCatalog.Mods)
			{
				if (mod.Category != category)
				{
					category = mod.Category;
					GUILayout.Space(6f);
					GUILayout.Label(category.ToUpperInvariant(), _section);
				}
				DrawMod(mod);
			}
			if (ModCatalog.Mods.Count == 0)
				GUILayout.Label("No mods found.", _wrap);
			GUILayout.EndScrollView();

			GUILayout.EndArea();
		}

		void HandleDrag()
		{
			var e = Event.current;
			var title = new Rect(_rect.x, _rect.y, _rect.width - 40f, TitleH);
			if (e.type == EventType.MouseDown && e.button == 0 && title.Contains(e.mousePosition))
			{
				_dragging = true;
				_dragOffset = e.mousePosition - new Vector2(_rect.x, _rect.y);
				e.Use();
			}
			else if (_dragging && e.type == EventType.MouseDrag)
			{
				_rect.x = e.mousePosition.x - _dragOffset.x;
				_rect.y = e.mousePosition.y - _dragOffset.y;
				e.Use();
			}
			else if (_dragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
			{
				_dragging = false;
			}
		}

		void DrawMod(ModInfo mod)
		{
			GUILayout.BeginVertical(GUI.skin.box);

			GUILayout.BeginHorizontal();
			string ver = string.IsNullOrEmpty(mod.Version) ? "" : " v" + mod.Version;
			if (GUILayout.Button($"{(mod.Expanded ? "-" : "+")}  {mod.Name}{ver}", _header, GUILayout.ExpandWidth(true), GUILayout.Height(26f)))
				mod.Expanded = !mod.Expanded;

			string state;
			if (mod.Loaded && mod.EnabledOnDisk) state = "Running";
			else if (mod.Loaded) state = "Off after restart";
			else if (mod.EnabledOnDisk) state = "On after restart";
			else state = "Off";
			GUILayout.Label(state, GUILayout.Width(120f));

			if (mod.Loaded && mod.CanReload && mod.EnabledOnDisk)
			{
				// Done in Update: reloading refreshes the mod list this loop is drawing.
				if (GUILayout.Button("Reload", GUILayout.Width(70f)))
					_reloadRequest = mod.Guid;
			}
			else
			{
				GUILayout.Label("", GUILayout.Width(70f));
			}

			if (mod.CanDisable)
			{
				bool on = GUILayout.Toggle(mod.EnabledOnDisk, " Enabled", GUILayout.Width(90f));
				if (on != mod.EnabledOnDisk)
				{
					string err = ModCatalog.SetEnabledOnDisk(mod, on);
					Say(err ?? $"{mod.Name} will be {(on ? "on" : "off")} the next time you start the game.");
				}
			}
			else
			{
				GUILayout.Label("", GUILayout.Width(90f));
			}
			GUILayout.EndHorizontal();

			if (mod.Expanded)
				DrawModDetails(mod);

			GUILayout.EndVertical();
		}

		void DrawModDetails(ModInfo mod)
		{
			if (!string.IsNullOrEmpty(mod.Description))
				GUILayout.Label(mod.Description, _wrap);
			var meta = new System.Collections.Generic.List<string>();
			if (!string.IsNullOrEmpty(mod.Manifest?.Author)) meta.Add("by " + mod.Manifest.Author);
			if (!string.IsNullOrEmpty(mod.Guid)) meta.Add(mod.Guid);
			if (!string.IsNullOrEmpty(mod.DllPath)) meta.Add(System.IO.Path.GetFileName(mod.DllPath));
			if (meta.Count > 0)
				GUILayout.Label(string.Join("  |  ", meta), _small);

			if (!mod.Loaded)
			{
				GUILayout.Label("This mod is switched off. Tick Enabled and restart the game to use it and change its settings.", _small);
				return;
			}
			if (mod.Config == null || mod.Settings.Count == 0)
			{
				GUILayout.Label("No settings.", _small);
				return;
			}

			bool showAdv = Plugin.ShowAdvanced.Value;
			string section = null;
			int hidden = 0;
			foreach (var s in mod.Settings)
			{
				if (s.Advanced && !showAdv)
				{
					hidden++;
					continue;
				}
				if (s.Entry.Definition.Section != section)
				{
					section = s.Entry.Definition.Section;
					GUILayout.Space(4f);
					GUILayout.Label(section, _section);
				}
				DrawSetting(mod, s);
			}
			if (hidden > 0)
				GUILayout.Label($"{hidden} advanced setting(s) hidden. Tick 'Show advanced settings' to see them.", _small);
		}

		void DrawSetting(ModInfo mod, SettingInfo s)
		{
			var entry = s.Entry;
			GUILayout.BeginHorizontal();
			string label = entry.Definition.Key + (s.RestartRequired ? " *" : "");
			GUILayout.Label(label, GUILayout.Width(220f));
			try
			{
				DrawValueEditor(mod, s);
			}
			catch (Exception e)
			{
				GUILayout.Label("(can't edit here) " + entry.GetSerializedValue(), _small);
				Plugin.Logger.LogDebug($"Editor for {s.Key}: {e.Message}");
			}
			bool isDefault = Equals(entry.BoxedValue, entry.DefaultValue);
			if (!isDefault && GUILayout.Button("Default", GUILayout.Width(70f)))
				SetValue(mod, s, entry.DefaultValue);
			else if (isDefault)
				GUILayout.Label("", GUILayout.Width(70f));
			GUILayout.EndHorizontal();

			string desc = entry.Description?.Description;
			if (s.RestartRequired)
				desc = (string.IsNullOrEmpty(desc) ? "" : desc + " ") + "(* needs a game restart)";
			if (!string.IsNullOrEmpty(desc))
				GUILayout.Label(desc, _small);
		}

		void DrawValueEditor(ModInfo mod, SettingInfo s)
		{
			var entry = s.Entry;
			var type = entry.SettingType;
			var value = entry.BoxedValue;
			var acceptable = entry.Description?.AcceptableValues;

			if (type == typeof(bool))
			{
				bool v = (bool)value;
				bool nv = GUILayout.Toggle(v, v ? " On" : " Off", GUILayout.ExpandWidth(true));
				if (nv != v)
					SetValue(mod, s, nv);
				return;
			}

			if (type == typeof(KeyCode))
			{
				string text = _capturingKey == s ? "Press a key (Esc cancels)" : value.ToString();
				if (GUILayout.Button(text, GUILayout.ExpandWidth(true)))
					_capturingKey = s;
				return;
			}

			if (acceptable is AcceptableValueRange<int> ri)
			{
				int v = (int)value;
				int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, ri.MinValue, ri.MaxValue, GUILayout.ExpandWidth(true)));
				GUILayout.Label(nv.ToString(), GUILayout.Width(50f));
				if (nv != v)
					SetValue(mod, s, nv);
				return;
			}

			if (acceptable is AcceptableValueRange<float> rf)
			{
				float v = (float)value;
				float nv = GUILayout.HorizontalSlider(v, rf.MinValue, rf.MaxValue, GUILayout.ExpandWidth(true));
				nv = (float)Math.Round(nv, 2);
				GUILayout.Label(nv.ToString("0.##"), GUILayout.Width(50f));
				if (Math.Abs(nv - v) > 0.0001f)
					SetValue(mod, s, nv);
				return;
			}

			object[] choices = null;
			if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false))
				choices = Enum.GetValues(type).Cast<object>().ToArray();
			else if (acceptable != null && acceptable.GetType().IsGenericType && acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueList<>))
				choices = ((System.Collections.IEnumerable)acceptable.GetType().GetProperty("AcceptableValues").GetValue(acceptable)).Cast<object>().ToArray();
			if (choices != null && choices.Length > 0)
			{
				int i = Array.FindIndex(choices, c => Equals(c, value));
				if (GUILayout.Button("<", GUILayout.Width(28f)))
					SetValue(mod, s, choices[(i - 1 + choices.Length) % choices.Length]);
				GUILayout.Label(value?.ToString() ?? "", GUILayout.ExpandWidth(true));
				if (GUILayout.Button(">", GUILayout.Width(28f)))
					SetValue(mod, s, choices[(i + 1) % choices.Length]);
				return;
			}

			// Anything else (numbers without a range, text, flags, shortcuts) is edited as its config-file text.
			string current = entry.GetSerializedValue();
			if (s.EditBuffer == null)
				s.EditBuffer = current;
			s.EditBuffer = GUILayout.TextField(s.EditBuffer, GUILayout.ExpandWidth(true));
			if (s.EditBuffer != current)
			{
				if (GUILayout.Button("Apply", GUILayout.Width(60f)))
				{
					try
					{
						entry.SetSerializedValue(s.EditBuffer);
						AfterChange(mod, s);
					}
					catch (Exception e)
					{
						Say($"'{s.EditBuffer}' is not a valid value for {entry.Definition.Key}: {e.Message}");
					}
					s.EditBuffer = entry.GetSerializedValue();
				}
			}
		}

		void SetValue(ModInfo mod, SettingInfo s, object value)
		{
			if (mod == null)
				return;
			try
			{
				s.Entry.BoxedValue = value;
				s.EditBuffer = null;
				AfterChange(mod, s);
			}
			catch (Exception e)
			{
				Say($"Could not change {s.Entry.Definition.Key}: {e.Message}");
				Plugin.Logger.LogError(e);
			}
		}

		void AfterChange(ModInfo mod, SettingInfo s)
		{
			// Some mods turn off auto-save; make sure the change reaches the file.
			if (mod.Config != null && !mod.Config.SaveOnConfigSet)
				mod.Config.Save();
			if (s.RestartRequired)
				ModCatalog.NoteRestart(mod, s);
		}

		static ModInfo FindMod(SettingInfo s) => ModCatalog.Mods.FirstOrDefault(m => m.Settings.Contains(s));

		void OpenFolder(string path)
		{
			try
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
			}
			catch (Exception e)
			{
				Say($"Could not open {path}: {e.Message}");
			}
		}
	}
}
