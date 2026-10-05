using System;
using Common.UI;
using Common.UI.Hint;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using World.SceneObject;
using World.UI.GeneralPage.ResourcePanel;

namespace UGARRecruitGrowth
{
	/// <summary>
	/// Pinned popovers built from a copy of the game's own tooltip (same frame, font and colours).
	/// Click the recruits number on a settlement card for that settlement, or the population number in the top bar
	/// for every settlement plus the Britain pipeline. A click anywhere else, or Escape, closes it.
	/// </summary>
	public class Overlay : MonoBehaviour
	{
		public Overlay(IntPtr ptr) : base(ptr) { }

		enum Mode { None, Settlement, Overview, Colonial }

		const float RefreshSeconds = 1f;
		const float SettlementWidth = 440f;

		Mode _mode;
		IntPtr _locality;
		float _nextRefresh;
		bool _broken;

		GameObject _pop;
		RectTransform _popRt;
		TextMeshProUGUI _popText;
		LayoutElement _popLayout;
		CanvasGroup _popGroup;
		float _baseFontSize;
		GeneralResourcePanel _topBar;

		public static void Invalidate() { }

		/// <summary>Which popover is pinned, so the matching game tooltip can be suppressed (see Patches).</summary>
		internal static bool PinnedOverview;
		internal static IntPtr PinnedLocality;

		void LateUpdate()
		{
			PinnedOverview = _mode == Mode.Overview;
			PinnedLocality = _mode == Mode.Settlement ? _locality : IntPtr.Zero;
		}

		// The popover is a DontDestroyOnLoad clone: remove it with us (the mod manager's live reload destroys this component).
		void OnDestroy()
		{
			try { if (_pop != null) Destroy(_pop); } catch (Exception) { }
		}

		void Update()
		{
			if (_broken)
				return;
			try
			{
				if (!Plugin.Enabled.Value)
				{
					Close();
					return;
				}
				if (Plugin.OverviewKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.OverviewKey.Value))
					Toggle(Mode.Overview, null);
				if (_mode != Mode.None && Input.GetKeyDown(KeyCode.Escape))
					Close();
				if (Input.GetMouseButtonDown(0))
					OnClick(Input.mousePosition);
				if (_mode != Mode.None)
					Refresh();
			}
			catch (Exception e)
			{
				_broken = true;
				Plugin.Logger.LogError($"Recruit growth popover disabled after an error: {e}");
				Close();
			}
		}

		// ---------------- Clicks ----------------

		static Camera CamFor(Transform t)
		{
			var canvas = t.GetComponentInParent<Canvas>();
			if (canvas == null)
				return null;
			canvas = canvas.rootCanvas;
			return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
		}

		static bool Over(Component c, Vector2 screen)
		{
			if (c == null || !c.gameObject.activeInHierarchy)
				return false;
			var rt = c.GetComponent<RectTransform>();
			return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, CamFor(rt));
		}

		Component SettlementRecruitsStat()
		{
			var panel = Patches.OpenPanel;
			try
			{
				if (panel == null || !panel.gameObject.activeInHierarchy)
					return null;
				return panel.resourcePanel?.recruits;
			}
			catch (Exception)
			{
				Patches.OpenPanel = null;
				return null;
			}
		}

		Component TopBarPopulationStat()
		{
			try
			{
				if (_topBar == null)
					_topBar = UnityEngine.Object.FindObjectOfType<GeneralResourcePanel>();
				return _topBar?.populationElement;
			}
			catch (Exception)
			{
				_topBar = null;
				return null;
			}
		}

		static Component ColonialStat()
		{
			try { return TopBar.Element; } catch (Exception) { return null; }
		}

		void OnClick(Vector3 mouse)
		{
			var screen = new Vector2(mouse.x, mouse.y);
			if (_pop != null && _mode != Mode.None && _popGroup.alpha > 0f && Over(_popRt, screen))
				return;
			var stat = SettlementRecruitsStat();
			if (Over(stat, screen))
			{
				var loc = Patches.OpenPanel.Locality;
				Toggle(Mode.Settlement, loc);
				return;
			}
			if (Over(TopBarPopulationStat(), screen))
			{
				Toggle(Mode.Overview, null);
				return;
			}
			if (Over(ColonialStat(), screen))
			{
				Toggle(Mode.Colonial, null);
				return;
			}
			Close();
		}

		float _lastToggle = -10f;
		bool _inToggle;

		void Toggle(Mode mode, RegionLocality loc)
		{
			// Ignore click bursts: one toggle per 0.3 s, and never re-enter while building the popover.
			float now = Time.realtimeSinceStartup;
			if (_inToggle || now - _lastToggle < 0.3f)
			{
				Diag.Write($"click ignored ({(_inToggle ? "busy" : "debounce")})");
				return;
			}
			_lastToggle = now;
			_inToggle = true;
			var sw = System.Diagnostics.Stopwatch.StartNew();
			Diag.Write($"toggle {mode} begin (was {_mode})");
			try
			{
				ToggleInner(mode, loc);
			}
			finally
			{
				_inToggle = false;
				Diag.Write($"toggle {mode} end, now {_mode}, {sw.ElapsedMilliseconds} ms");
			}
		}

		void ToggleInner(Mode mode, RegionLocality loc)
		{
			IntPtr id = loc != null ? loc.Pointer : IntPtr.Zero;
			if (_mode == mode && _locality == id)
			{
				Close();
				return;
			}
			if (RecruitMath.PlayerCountry == null)
				return;
			_mode = mode;
			_locality = id;
			_nextRefresh = 0f;
			if (!EnsurePopover())
			{
				_mode = Mode.None;
				return;
			}
			_popText.fontSize = _baseFontSize;
			var anchor = mode == Mode.Settlement ? SettlementRecruitsStat() : mode == Mode.Colonial ? ColonialStat() : TopBarPopulationStat();
			var canvas = anchor != null ? anchor.GetComponentInParent<Canvas>() : null;
			if (canvas == null)
				canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
			if (canvas == null)
			{
				Plugin.Logger.LogWarning("Recruit growth: no UI canvas found for the popover.");
				_mode = Mode.None;
				return;
			}
			canvas = canvas.rootCanvas;
			_pop.transform.SetParent(canvas.transform, false);
			_pop.transform.localScale = Vector3.one;
			_pop.transform.localRotation = Quaternion.identity;
			_pop.SetActive(true);
			// Everything between the root and the text may have been hidden when the tooltip was copied.
			for (var t = _popText.transform; t != null && t != canvas.transform; t = t.parent)
				t.gameObject.SetActive(true);
			_pop.transform.SetAsLastSibling();
			_nextRefresh = 0f;
			Refresh();
			var r = ScreenRect(_popRt);
			Plugin.Logger.LogInfo($"Recruit growth popover opened ({mode}) on canvas '{canvas.name}' ({canvas.renderMode}), screen rect {r}, alpha {_popGroup.alpha}.");
		}

		void Close()
		{
			_mode = Mode.None;
			_locality = IntPtr.Zero;
			if (_pop != null)
			{
				try
				{
					if (_pop.activeSelf)
					{
						_pop.SetActive(false);
						Diag.Write("popover closed");
					}
				}
				catch (Exception) { _pop = null; }
			}
		}

		// ---------------- Popover built from the game's tooltip ----------------

		bool EnsurePopover()
		{
			if (_pop != null)
			{
				try
				{
					if (_pop.transform != null)
						return true;
				}
				catch (Exception) { }
				_pop = null;
			}
			var template = SingletonPrefab<TextHint>.Instance;
			if (template == null)
			{
				Plugin.Logger.LogWarning("Recruit growth: the game's tooltip isn't ready yet; hover any stat once and click again.");
				return false;
			}

			// Clone under an inactive holder so the copy never runs the tooltip's singleton Awake.
			var holder = new GameObject("RecruitGrowthHolder");
			holder.SetActive(false);
			var clone = UnityEngine.Object.Instantiate(template.gameObject, holder.transform, false);
			clone.name = "RecruitGrowthPopover";
			var hint = clone.GetComponent<TextHint>();
			_popText = hint != null ? hint.text : clone.GetComponentInChildren<TextMeshProUGUI>(true);
			_popLayout = hint != null ? hint.textLayout : null;
			var cloneGroup = hint != null ? hint.group : null;
			LogHierarchy(template.transform, 0);
			if (hint != null)
				UnityEngine.Object.DestroyImmediate(hint);
			if (_popText == null)
			{
				UnityEngine.Object.Destroy(holder);
				UnityEngine.Object.Destroy(clone);
				Plugin.Logger.LogWarning("Recruit growth: could not find the text in the game's tooltip.");
				return false;
			}
			_baseFontSize = _popText.fontSize;
			_popText.richText = true;
			_popText.enableWordWrapping = true;

			// The tooltip root (TextHint(Clone)) is the frame itself: Image + VerticalLayoutGroup + ContentSizeFitter,
			// with a fade Animator and CanvasGroup. The game only parents it under a canvas while it's shown, so the
			// copy is parented under the canvas of whatever was clicked (see Attach).
			clone.transform.SetParent(null, false);
			UnityEngine.Object.Destroy(holder);
			_pop = clone;
			_popRt = clone.GetComponent<RectTransform>();
			_templateFrame = template.GetComponent<RectTransform>();
			var anim = clone.GetComponent<Animator>();
			if (anim != null)
				anim.enabled = false; // the fade animation would drive the alpha back to 0
			UnityEngine.Object.DontDestroyOnLoad(clone);

			_popGroup = clone.GetComponent<CanvasGroup>();
			if (_popGroup == null)
				_popGroup = clone.AddComponent<CanvasGroup>();
			_popGroup.alpha = 1f;
			_popGroup.interactable = true;
			_popGroup.blocksRaycasts = true;
			_popGroup.ignoreParentGroups = true;
			// The frame's Image catches clicks so they don't fall through to the campaign map.
			var frameImage = clone.GetComponent<Image>();
			if (frameImage != null)
				frameImage.raycastTarget = true;
			Plugin.Logger.LogInfo("Recruit growth popover built from the game's tooltip.");
			return true;
		}

		RectTransform _templateFrame;
		bool _loggedHierarchy;

		static RectTransform FindTemplateFrame(TextHint template, bool isRootCanvas)
		{
			if (!isRootCanvas)
				return template.GetComponent<RectTransform>();
			var g = template.group;
			return g != null ? g.GetComponent<RectTransform>() : template.GetComponent<RectTransform>();
		}

		/// <summary>True while the game's own tooltip is on screen.</summary>
		bool GameHintVisible()
		{
			var t = SingletonPrefab<TextHint>.Instance;
			if (t == null || t.transform.parent == null || !t.gameObject.activeInHierarchy)
				return false;
			var frame = _templateFrame != null ? _templateFrame : t.GetComponent<RectTransform>();
			if (frame == null || !frame.gameObject.activeInHierarchy)
				return false;
			foreach (var cg in frame.GetComponentsInParent<CanvasGroup>())
				if (cg.alpha < 0.01f)
					return false;
			return true;
		}

		void LogHierarchy(Transform t, int depth)
		{
			if (_loggedHierarchy && depth == 0)
				return;
			if (depth == 0)
				_loggedHierarchy = true;
			var names = new System.Text.StringBuilder();
			foreach (var c in t.GetComponents<Component>())
				names.Append(c.GetIl2CppType().Name).Append(' ');
			Plugin.Logger.LogInfo($"Tooltip layout: {new string(' ', depth * 2)}{t.name} [{names}] active={t.gameObject.activeSelf}");
			if (depth < 3)
				for (int i = 0; i < t.childCount; i++)
					LogHierarchy(t.GetChild(i), depth + 1);
		}

		RegionLocality CurrentLocality()
		{
			var panel = Patches.OpenPanel;
			try
			{
				var l = panel != null && panel.gameObject.activeInHierarchy ? panel.Locality : null;
				return l != null && l.Pointer == _locality ? l : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		void Refresh()
		{
			if (_pop == null)
			{
				_mode = Mode.None;
				return;
			}

			// Step aside while the game shows a tooltip for some other element. The tooltip of the stat that opened
			// the popover doesn't count: the popover already shows everything in it, so it stays on top instead.
			var anchor = _mode == Mode.Settlement ? SettlementRecruitsStat() : _mode == Mode.Colonial ? ColonialStat() : TopBarPopulationStat();
			var mouse = Input.mousePosition;
			bool overAnchor = Over(anchor, new Vector2(mouse.x, mouse.y));
			bool hintOpen = !overAnchor && GameHintVisible();
			_popGroup.alpha = hintOpen ? 0f : 1f;
			_popGroup.blocksRaycasts = !hintOpen;
			// Stay on top of the game's tooltip, but only reorder when needed (reordering rebuilds the canvas).
			var tr = _pop.transform;
			if (overAnchor && tr.parent != null && tr.GetSiblingIndex() != tr.parent.childCount - 1)
				tr.SetAsLastSibling();

			if (Time.realtimeSinceStartup < _nextRefresh)
				return;
			_nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;

			var root = _popRt.GetComponentInParent<Canvas>()?.rootCanvas;
			var rootRt = root != null ? root.GetComponent<RectTransform>() : null;
			float canvasW = rootRt != null ? rootRt.rect.width : 1920f;
			float canvasH = rootRt != null ? rootRt.rect.height : 1080f;

			string text;
			float width;
			if (_mode == Mode.Settlement)
			{
				var l = CurrentLocality();
				if (l == null)
				{
					Close();
					return;
				}
				text = Texts.Details(l);
				width = Math.Min(SettlementWidth * Plugin.UiScale.Value, canvasW * 0.45f);
			}
			else if (_mode == Mode.Colonial)
			{
				text = Texts.Colonial();
				width = Math.Min(SettlementWidth * Plugin.UiScale.Value, canvasW * 0.45f);
			}
			else
			{
				float lineH = Math.Max(10f, _popText.fontSize * 1.35f);
				int rows = Math.Max(10, (int)(canvasH * 0.55f / lineH));
				text = Texts.Overview(rows, out int cols);
				width = Math.Min((cols > 1 ? 960f : 520f) * Plugin.UiScale.Value, canvasW * 0.85f);
			}

			if (_popLayout != null)
			{
				_popLayout.preferredWidth = width;
				_popLayout.minWidth = Math.Min(width, 200f);
			}
			var sw = System.Diagnostics.Stopwatch.StartNew();
			_popText.text = text;
			try
			{
				Fit(canvasH);
				Place();
				if (sw.ElapsedMilliseconds > 100)
					Diag.Write($"slow layout: {sw.ElapsedMilliseconds} ms for {text.Length} chars ({_mode})");
			}
			catch (Exception e)
			{
				if (!_placeErrorLogged)
				{
					_placeErrorLogged = true;
					Plugin.Logger.LogWarning($"Recruit growth: could not position the popover, leaving it where it is: {e}");
				}
			}
		}

		void Fit(float canvasH)
		{
			// Shrink the font until the popover fits 90% of the screen height.
			for (int i = 0; i < 8; i++)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(_popRt);
				if (_popRt.rect.height <= canvasH * 0.9f || _popText.fontSize <= 9f)
					break;
				_popText.fontSize = Math.Max(9f, _popText.fontSize * 0.9f);
			}
		}

		static Rect ScreenRect(RectTransform rt)
		{
			var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
			rt.GetWorldCorners(c);
			var cam = CamFor(rt);
			Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
			Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
			return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
		}

		bool _placeErrorLogged;

		void Place()
		{
			// Convert screen points on the plane of the frame's parent (or the frame itself at the root).
			var parent = _popRt.parent != null ? _popRt.parent.GetComponent<RectTransform>() : null;
			if (parent == null)
				parent = _popRt;
			var cam = CamFor(_popRt);
			Rect me = ScreenRect(_popRt);
			float margin = 12f;
			float x, yTop;

			Rect anchor = default;
			bool haveAnchor = false;
			if (_mode == Mode.Settlement && Patches.OpenPanel != null)
			{
				// The settlement panel, unless it is a full-screen canvas; then the recruits stat's resource strip.
				var prt = Patches.OpenPanel.GetComponent<RectTransform>();
				if (prt != null)
				{
					anchor = ScreenRect(prt);
					haveAnchor = anchor.width > 0f && anchor.width < Screen.width * 0.9f;
				}
				if (!haveAnchor)
				{
					var strip = Patches.OpenPanel.resourcePanel;
					var srt = strip != null ? strip.GetComponent<RectTransform>() : null;
					if (srt != null)
					{
						anchor = ScreenRect(srt);
						haveAnchor = anchor.width > 0f && anchor.width < Screen.width * 0.9f;
					}
				}
			}
			if (haveAnchor)
			{
				// Beside the settlement panel, preferring the side with room.
				x = anchor.xMax + margin;
				if (x + me.width > Screen.width - margin)
					x = anchor.xMin - margin - me.width;
				yTop = anchor.yMax;
			}
			else
			{
				x = (Screen.width - me.width) / 2f;
				yTop = Screen.height - Screen.height * 0.08f;
			}
			// Clamp to the screen.
			x = Mathf.Clamp(x, margin, Math.Max(margin, Screen.width - me.width - margin));
			float yBottom = yTop - me.height;
			if (yBottom < margin)
				yTop += margin - yBottom;
			if (yTop > Screen.height - margin)
				yTop = Screen.height - margin;

			// Move by the screen-space difference between where it is and where it should be.
			var want = new Vector2(x, yTop);
			var have = new Vector2(me.xMin, me.yMax);
			if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, want, cam, out Vector3 wWant) &&
				RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, have, cam, out Vector3 wHave))
				_popRt.position += wWant - wHave;
		}
	}
}
