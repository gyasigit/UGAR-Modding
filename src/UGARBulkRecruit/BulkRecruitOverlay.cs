using System;
using UnityEngine;
using World.UI.GeneralPage.RegimentManagement;
using PanelMode = World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel.PanelMode;

namespace UGARBulkRecruit
{
	/// <summary>Small IMGUI box shown while the regiment recruit screen is open in Create mode.</summary>
	public class BulkRecruitOverlay : MonoBehaviour
	{
		public BulkRecruitOverlay(IntPtr ptr) : base(ptr) { }

		const float W = 360f;
		bool _guiBroken;

		// IMGUI clicks also reach the game's uGUI and map, which closed the recruit screen. An invisible
		// raycast-target Image on a top-most canvas sits under the box so the game sees the pointer as
		// over UI and ignores the click.
		GameObject _blockerRoot;
		RectTransform _blocker;
		Rect _blockRect;
		int _blockFrame = -10;

		// The blocker canvas is DontDestroyOnLoad: remove it with us (the mod manager's live reload destroys this component).
		void OnDestroy()
		{
			try { if (_blockerRoot != null) Destroy(_blockerRoot); } catch (Exception) { }
		}

		void EnsureBlocker()
		{
			if (_blockerRoot != null)
				return;
			_blockerRoot = new GameObject("UGARBulkRecruitClickBlocker");
			DontDestroyOnLoad(_blockerRoot);
			var canvas = _blockerRoot.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;
			_blockerRoot.AddComponent<UnityEngine.UI.GraphicRaycaster>();
			var child = new GameObject("Blocker");
			child.transform.SetParent(_blockerRoot.transform, false);
			var img = child.AddComponent<UnityEngine.UI.Image>();
			img.color = new Color(0f, 0f, 0f, 0f);
			img.raycastTarget = true;
			_blocker = child.GetComponent<RectTransform>();
			_blocker.anchorMin = Vector2.zero;
			_blocker.anchorMax = Vector2.zero;
			_blocker.pivot = Vector2.zero;
		}

		void LateUpdate()
		{
			try
			{
				bool show = !_guiBroken && !BulkPanel.NativeOk && Time.frameCount - _blockFrame <= 2;
				if (!show)
				{
					if (_blockerRoot != null && _blockerRoot.activeSelf)
						_blockerRoot.SetActive(false);
					return;
				}
				EnsureBlocker();
				if (!_blockerRoot.activeSelf)
					_blockerRoot.SetActive(true);
				// IMGUI y runs down from the top; uGUI from the bottom.
				_blocker.anchoredPosition = new Vector2(_blockRect.x, Screen.height - _blockRect.yMax);
				_blocker.sizeDelta = new Vector2(_blockRect.width, _blockRect.height);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Bulk recruit click blocker failed: {e}");
				_blockFrame = -10;
			}
		}

		static RegimentManagementPanel ActiveCreatePanel()
		{
			var p = Patches.OpenPanel;
			try
			{
				if (p == null || !p.gameObject.activeInHierarchy)
					return null;
				return p.mode == PanelMode.Create ? p : null;
			}
			catch (Exception)
			{
				// The panel was destroyed (e.g. returning to the main menu); wait for the next Show.
				Patches.OpenPanel = null;
				return null;
			}
		}

		void OnGUI()
		{
			if (_guiBroken || !Plugin.Enabled.Value || BulkPanel.NativeOk)
				return;
			try
			{
				Draw();
			}
			catch (Exception e)
			{
				_guiBroken = true;
				Plugin.Logger.LogError($"Bulk recruit window disabled after an error: {e}");
			}
		}

		void Draw()
		{
			bool showMessage = BulkRecruit.Message != null && Time.realtimeSinceStartup < BulkRecruit.MessageUntil;
			var panel = ActiveCreatePanel();
			if (panel == null && !showMessage)
				return;

			float scale = Screen.height / 1080f * Plugin.UiScale.Value;
			var saved = GUI.matrix;
			GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
			float x = Plugin.WindowX.Value < 0 ? (Screen.width / scale - W) / 2f : Plugin.WindowX.Value;
			float y = Plugin.WindowY.Value;

			Design design = panel != null ? BulkRecruit.LastDesignFor(panel.initialRegiment) : null;
			float h = 8f;
			if (panel != null)
				h += 34f + (design != null ? 32f : 0f);
			if (showMessage)
				h += 44f;

			GUI.Box(new Rect(x, y, W, h), "");
			_blockRect = new Rect(x * scale, y * scale, W * scale, h * scale);
			_blockFrame = Time.frameCount;
			float cy = y + 6f;

			if (panel != null)
			{
				HandleKeys();
				GUI.Label(new Rect(x + 10f, cy + 4f, 150f, 24f), "Regiments to create:");
				if (GUI.Button(new Rect(x + 160f, cy, 32f, 26f), "-"))
					BulkRecruit.Copies = Math.Max(1, BulkRecruit.Copies - 1);
				GUI.Label(new Rect(x + 200f, cy + 4f, 40f, 24f), BulkRecruit.Copies.ToString());
				if (GUI.Button(new Rect(x + 236f, cy, 32f, 26f), "+"))
					BulkRecruit.Copies = Math.Min(Plugin.MaxCopies.Value, BulkRecruit.Copies + 1);
				if (GUI.Button(new Rect(x + 276f, cy, 32f, 26f), "x5"))
					BulkRecruit.Copies = Math.Min(Plugin.MaxCopies.Value, 5);
				if (GUI.Button(new Rect(x + 314f, cy, 36f, 26f), "1"))
					BulkRecruit.Copies = 1;
				cy += 34f;

				if (design != null)
				{
					if (GUI.Button(new Rect(x + 10f, cy, W - 20f, 26f), $"Use last design ({design.Label})"))
					{
						try
						{
							BulkRecruit.LoadDesign(panel, design);
						}
						catch (Exception e)
						{
							BulkRecruit.Say("Could not load the last design, see BepInEx log.");
							Plugin.Logger.LogError(e);
						}
					}
					cy += 32f;
				}
			}

			if (showMessage)
				GUI.Label(new Rect(x + 10f, cy, W - 20f, 40f), BulkRecruit.Message);

			GUI.matrix = saved;
		}

		static void HandleKeys()
		{
			var e = Event.current;
			if (e == null || e.type != EventType.KeyDown || !e.control)
				return;
			if (e.keyCode == KeyCode.Equals || e.keyCode == KeyCode.KeypadPlus)
			{
				BulkRecruit.Copies = Math.Min(Plugin.MaxCopies.Value, BulkRecruit.Copies + 1);
				e.Use();
			}
			else if (e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus)
			{
				BulkRecruit.Copies = Math.Max(1, BulkRecruit.Copies - 1);
				e.Use();
			}
		}
	}
}
