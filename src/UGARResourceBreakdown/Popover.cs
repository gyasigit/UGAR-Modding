using System;
using Common.UI;
using Common.UI.Hint;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using World;
using World.UI.GeneralPage.Production.GeneralPanel;

namespace UGARResourceBreakdown
{
	/// <summary>
	/// A pinned popover built from a copy of the game's own tooltip (same frame, font and colours), opened by clicking a
	/// resource or the factory/shipyard numbers in the Production screen's summary. A click anywhere else, Escape, or
	/// closing the Production screen closes it. Same technique as Recruit Growth's Overlay.
	/// </summary>
	public class Popover : MonoBehaviour
	{
		public Popover(IntPtr ptr) : base(ptr) { }

		const float RefreshSeconds = 1f;
		const float Width = 470f;

		internal static bool CheckPending;

		int _target = -1;          // index into Targets, -1 = closed
		float _nextRefresh;
		bool _broken;
		float _lastToggle = -10f;

		GameObject _pop;
		RectTransform _popRt;
		TextMeshProUGUI _popText;
		LayoutElement _popLayout;
		CanvasGroup _popGroup;
		float _baseFontSize;

		AvailableResouces _resources;
		AvailablePlants _plants;

		// Clickable entries: 0-5 resources (Breakdown.PanelTypes order), 6 factories, 7 shipyards.
		const int Factories = 6, Shipyards = 7;

		void OnDestroy()
		{
			try { if (_pop != null) Destroy(_pop); } catch (Exception) { }
		}

		void Update()
		{
			if (CheckPending)
			{
				CheckPending = false;
				try { Patches.LogCheck(); } catch (Exception e) { Plugin.Logger.LogWarning($"Resource breakdown check failed: {e.Message}"); }
			}
			if (_broken)
				return;
			try
			{
				if (!Plugin.Enabled.Value)
				{
					Close();
					return;
				}
				if (_target >= 0 && Input.GetKeyDown(KeyCode.Escape))
					Close();
				if (Input.GetMouseButtonDown(0))
					OnClick(Input.mousePosition);
				if (_target >= 0)
					Refresh();
			}
			catch (Exception e)
			{
				_broken = true;
				Plugin.Logger.LogError($"Resource breakdown popover disabled after an error: {e}");
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

		static bool Over(RectTransform rt, Vector2 screen) =>
			rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, CamFor(rt));

		static bool Alive(Component c)
		{
			try { return c != null && c.gameObject != null && c.gameObject.activeInHierarchy; } catch (Exception) { return false; }
		}

		bool FindPanels()
		{
			if (!Alive(_resources))
				_resources = UnityEngine.Object.FindObjectOfType<AvailableResouces>();
			if (!Alive(_plants))
				_plants = UnityEngine.Object.FindObjectOfType<AvailablePlants>();
			return Alive(_resources) || Alive(_plants);
		}

		TextMeshProUGUI Field(int target)
		{
			try
			{
				switch (target)
				{
					case 0: return _resources?.coal;
					case 1: return _resources?.copper;
					case 2: return _resources?.iron;
					case 3: return _resources?.saltpeter;
					case 4: return _resources?.wood;
					case 5: return _resources?.cloth;
					case Factories: return _plants?.factoryTotal;
					case Shipyards: return _plants?.shipyardTotal;
				}
			}
			catch (Exception) { }
			return null;
		}

		TextMeshProUGUI ExtraField(int target)
		{
			try
			{
				if (target == Factories) return _plants?.factoryUsed;
				if (target == Shipyards) return _plants?.shipyardUsed;
			}
			catch (Exception) { }
			return null;
		}

		/// <summary>
		/// The clickable area of an entry: the number's row (icon + number) when the number sits in its own small
		/// container, otherwise the number itself.
		/// </summary>
		RectTransform Row(TextMeshProUGUI t, Component panel)
		{
			if (t == null)
				return null;
			var rt = t.rectTransform;
			var parent = rt.parent as RectTransform;
			if (parent != null && panel != null && parent != panel.transform && parent.childCount <= 4 && !SharedRow(parent, t))
				return parent;
			return rt;
		}

		// True when another entry's number sits in the same container (then only the number itself is clickable).
		bool SharedRow(Transform parent, TextMeshProUGUI mine)
		{
			for (int i = 0; i <= Shipyards; i++)
			{
				foreach (var f in new[] { Field(i), ExtraField(i) })
				{
					if (f == null || f.Pointer == mine.Pointer)
						continue;
					if (f.transform.parent != null && f.transform.parent.Pointer == parent.Pointer &&
						!(i >= Factories && (Field(i)?.Pointer == mine.Pointer || ExtraField(i)?.Pointer == mine.Pointer)))
						return true;
				}
			}
			return false;
		}

		bool HitsEntry(int target, Vector2 screen)
		{
			Component panel = target < Factories ? _resources : _plants;
			if (!Alive(panel))
				return false;
			if (Over(Row(Field(target), panel), screen))
				return true;
			if (Over(Row(ExtraField(target), panel), screen))
				return true;
			// The icon sits just right of the number ("20 (1) [coal]"): accept clicks up to two line heights to its right.
			return target < Factories && NearRight(Field(target), screen);
		}

		static bool NearRight(TextMeshProUGUI t, Vector2 screen)
		{
			if (t == null || !t.gameObject.activeInHierarchy)
				return false;
			Rect r = ScreenRect(t.rectTransform);
			float extra = r.height * 2.2f;
			return screen.y >= r.yMin - r.height * 0.3f && screen.y <= r.yMax + r.height * 0.3f &&
				screen.x >= r.xMin && screen.x <= r.xMax + extra;
		}

		void OnClick(Vector3 mouse)
		{
			var screen = new Vector2(mouse.x, mouse.y);
			if (_pop != null && _target >= 0 && _popGroup.alpha > 0f && Over(_popRt, screen))
				return;
			if (FindPanels())
			{
				for (int i = 0; i <= Shipyards; i++)
				{
					if (HitsEntry(i, screen))
					{
						Toggle(i);
						return;
					}
				}
			}
			Close();
		}

		void Toggle(int target)
		{
			float now = Time.realtimeSinceStartup;
			if (now - _lastToggle < 0.25f)
				return;
			_lastToggle = now;
			if (_target == target)
			{
				Close();
				return;
			}
			if (Breakdown.PlayerCountry == null || !EnsurePopover())
				return;
			var anchor = Field(target);
			var canvas = anchor != null ? anchor.GetComponentInParent<Canvas>() : null;
			if (canvas == null)
			{
				Plugin.Logger.LogWarning("Resource breakdown: no UI canvas found for the popover.");
				return;
			}
			canvas = canvas.rootCanvas;
			_target = target;
			_popText.fontSize = _baseFontSize;
			_pop.transform.SetParent(canvas.transform, false);
			_pop.transform.localScale = Vector3.one;
			_pop.transform.localRotation = Quaternion.identity;
			_pop.SetActive(true);
			for (var t = _popText.transform; t != null && t != canvas.transform; t = t.parent)
				t.gameObject.SetActive(true);
			_pop.transform.SetAsLastSibling();
			_nextRefresh = 0f;
			Refresh();
		}

		void Close()
		{
			_target = -1;
			if (_pop != null)
			{
				try { if (_pop.activeSelf) _pop.SetActive(false); }
				catch (Exception) { _pop = null; }
			}
		}

		// ---------------- Popover built from the game's tooltip ----------------

		bool EnsurePopover()
		{
			if (_pop != null)
			{
				try { if (_pop.transform != null) return true; } catch (Exception) { }
				_pop = null;
			}
			var template = SingletonPrefab<TextHint>.Instance;
			if (template == null)
			{
				Plugin.Logger.LogWarning("Resource breakdown: the game's tooltip isn't ready yet; hover any stat once and click again.");
				return false;
			}

			// Clone under an inactive holder so the copy never runs the tooltip's singleton Awake.
			var holder = new GameObject("ResourceBreakdownHolder");
			holder.SetActive(false);
			var clone = UnityEngine.Object.Instantiate(template.gameObject, holder.transform, false);
			clone.name = "ResourceBreakdownPopover";
			var hint = clone.GetComponent<TextHint>();
			_popText = hint != null ? hint.text : clone.GetComponentInChildren<TextMeshProUGUI>(true);
			_popLayout = hint != null ? hint.textLayout : null;
			if (hint != null)
				UnityEngine.Object.DestroyImmediate(hint);
			if (_popText == null)
			{
				UnityEngine.Object.Destroy(holder);
				UnityEngine.Object.Destroy(clone);
				Plugin.Logger.LogWarning("Resource breakdown: could not find the text in the game's tooltip.");
				return false;
			}
			_baseFontSize = _popText.fontSize;
			_popText.richText = true;
			_popText.enableWordWrapping = true;

			clone.transform.SetParent(null, false);
			UnityEngine.Object.Destroy(holder);
			_pop = clone;
			_popRt = clone.GetComponent<RectTransform>();
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
			return true;
		}

		void Refresh()
		{
			if (_pop == null)
			{
				_target = -1;
				return;
			}
			Component panel = _target < Factories ? _resources : _plants;
			if (!Alive(panel))
			{
				Close(); // the Production screen was closed
				return;
			}
			if (Time.realtimeSinceStartup < _nextRefresh)
				return;
			_nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;

			var root = _popRt.GetComponentInParent<Canvas>()?.rootCanvas;
			var rootRt = root != null ? root.GetComponent<RectTransform>() : null;
			float canvasW = rootRt != null ? rootRt.rect.width : 1920f;
			float canvasH = rootRt != null ? rootRt.rect.height : 1080f;

			string text = _target == Factories ? Texts.Plants(Breakdown.Plants(false))
				: _target == Shipyards ? Texts.Plants(Breakdown.Plants(true))
				: Texts.Resource(Breakdown.Resource(Breakdown.PanelTypes[_target]));
			float width = Math.Min(Width * Plugin.UiScale.Value, canvasW * 0.45f);
			if (_popLayout != null)
			{
				_popLayout.preferredWidth = width;
				_popLayout.minWidth = Math.Min(width, 200f);
			}
			_popText.text = text;
			try
			{
				Fit(canvasH);
				Place(panel);
			}
			catch (Exception e)
			{
				if (!_placeErrorLogged)
				{
					_placeErrorLogged = true;
					Plugin.Logger.LogWarning($"Resource breakdown: could not position the popover: {e}");
				}
			}
		}

		bool _placeErrorLogged;

		void Fit(float canvasH)
		{
			// Shrink the font until the popover fits 90% of the screen height.
			_popText.fontSize = _baseFontSize;
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

		void Place(Component panel)
		{
			var parent = _popRt.parent != null ? _popRt.parent.GetComponent<RectTransform>() : null;
			if (parent == null)
				parent = _popRt;
			var cam = CamFor(_popRt);
			Rect me = ScreenRect(_popRt);
			float margin = 12f;

			// Beside the clicked entry's panel, on whichever side has room; top aligned with the clicked row.
			var prt = panel.GetComponent<RectTransform>();
			Rect anchor = ScreenRect(prt);
			var row = Row(Field(_target), panel);
			float rowTop = row != null ? ScreenRect(row).yMax : anchor.yMax;
			float x = anchor.xMax + margin;
			if (x + me.width > Screen.width - margin)
				x = anchor.xMin - margin - me.width;
			float yTop = rowTop + 8f;
			x = Mathf.Clamp(x, margin, Math.Max(margin, Screen.width - me.width - margin));
			if (yTop - me.height < margin)
				yTop = margin + me.height;
			if (yTop > Screen.height - margin)
				yTop = Screen.height - margin;

			var want = new Vector2(x, yTop);
			var have = new Vector2(me.xMin, me.yMax);
			if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, want, cam, out Vector3 wWant) &&
				RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, have, cam, out Vector3 wHave))
				_popRt.position += wWant - wHave;
		}
	}
}
