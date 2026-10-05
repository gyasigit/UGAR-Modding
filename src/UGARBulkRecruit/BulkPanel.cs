using System;
using System.Globalization;
using System.Text;
using Common.UI;
using Common.UI.Hint;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using World.UI.GeneralPage.RegimentManagement;
using PanelMode = World.UI.GeneralPage.RegimentManagement.RegimentManagementPanel.PanelMode;

namespace UGARBulkRecruit
{
	/// <summary>
	/// The bulk recruit card, built from a copy of the game's own tooltip frame (same frame, font and colours,
	/// like the Recruit Growth popover). It sits beside the recruit screen while a regiment is being created,
	/// shows the total cost of N regiments, caps N at what the player can afford, and lists weapon swaps.
	/// Buttons are TextMeshPro links inside the text, so no game button prefab or event wiring is needed.
	/// </summary>
	public class BulkPanel : MonoBehaviour
	{
		public BulkPanel(IntPtr ptr) : base(ptr) { }

		/// <summary>True while the game-styled card is working; the IMGUI box is only a fallback.</summary>
		public static bool NativeOk = true;

		const string Gold = "#E8C887";
		const string Grey = "#8A8576";
		const string Bad = "#E0705A";
		const string Warn = "#E8A65A";
		const float Width = 380f;
		const float RefreshSeconds = 0.5f;

		GameObject _card;
		RectTransform _cardRt;
		TextMeshProUGUI _text;
		LayoutElement _layout;
		CanvasGroup _group;
		Plan _plan;
		float _nextRefresh;
		int _lastCopies = -1;
		IntPtr _lastPanel;
		int _failures;

		static RegimentManagementPanel CreatePanel()
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
				Patches.OpenPanel = null;
				return null;
			}
		}

		// The card is a DontDestroyOnLoad clone: remove it with us (the mod manager's live reload destroys this component).
		void OnDestroy()
		{
			try { if (_card != null) Destroy(_card); } catch (Exception) { }
		}

		void Update()
		{
			if (!NativeOk)
				return;
			try
			{
				var panel = Plugin.Enabled.Value ? CreatePanel() : null;
				bool showMessage = BulkRecruit.Message != null && Time.realtimeSinceStartup < BulkRecruit.MessageUntil;
				if (panel == null && !showMessage)
				{
					Hide();
					return;
				}
				if (!EnsureCard())
					return;
				Attach(panel);

				if (panel != null)
					HandleInput(panel);

				bool dirty = Time.realtimeSinceStartup >= _nextRefresh || _lastCopies != BulkRecruit.Copies
					|| (panel != null ? panel.Pointer : IntPtr.Zero) != _lastPanel;
				if (dirty)
				{
					_nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;
					_lastPanel = panel != null ? panel.Pointer : IntPtr.Zero;
					Refresh(panel, showMessage);
					_lastCopies = BulkRecruit.Copies;
				}
				_failures = 0;
			}
			catch (Exception e)
			{
				if (++_failures >= 3)
				{
					NativeOk = false;
					Plugin.Logger.LogError($"Bulk recruit card disabled, falling back to the simple box: {e}");
					Hide();
				}
				else
				{
					Plugin.Logger.LogWarning($"Bulk recruit card hiccup: {e.Message}");
				}
			}
		}

		void Hide()
		{
			if (_card == null)
				return;
			try
			{
				if (_card.activeSelf)
					_card.SetActive(false);
			}
			catch (Exception)
			{
				_card = null;
			}
		}

		// ---------------- Building the card from the game's tooltip ----------------

		bool EnsureCard()
		{
			if (_card != null)
			{
				try
				{
					if (_card.transform != null)
						return true;
				}
				catch (Exception) { }
				_card = null;
			}
			var template = SingletonPrefab<TextHint>.Instance;
			if (template == null)
				return false; // the game creates its tooltip on first use; try again next frame

			// Clone under an inactive holder so the copy never runs the tooltip's singleton Awake.
			var holder = new GameObject("BulkRecruitHolder");
			holder.SetActive(false);
			var clone = UnityEngine.Object.Instantiate(template.gameObject, holder.transform, false);
			clone.name = "BulkRecruitCard";
			var hint = clone.GetComponent<TextHint>();
			_text = hint != null ? hint.text : clone.GetComponentInChildren<TextMeshProUGUI>(true);
			_layout = hint != null ? hint.textLayout : null;
			if (hint != null)
				UnityEngine.Object.DestroyImmediate(hint);
			if (_text == null)
			{
				UnityEngine.Object.Destroy(holder);
				NativeOk = false;
				Plugin.Logger.LogWarning("Bulk recruit: could not find the text in the game's tooltip; using the simple box.");
				return false;
			}
			_text.richText = true;
			_text.enableWordWrapping = true;
			_text.raycastTarget = true;

			clone.transform.SetParent(null, false);
			UnityEngine.Object.Destroy(holder);
			_card = clone;
			_cardRt = clone.GetComponent<RectTransform>();
			var anim = clone.GetComponent<Animator>();
			if (anim != null)
				anim.enabled = false; // the tooltip's fade animation would drive the alpha back to 0
			UnityEngine.Object.DontDestroyOnLoad(clone);
			_group = clone.GetComponent<CanvasGroup>() ?? clone.AddComponent<CanvasGroup>();
			_group.alpha = 1f;
			_group.interactable = true;
			_group.blocksRaycasts = true;
			_group.ignoreParentGroups = true;
			// The frame's Image catches clicks so they don't fall through to the campaign map.
			var frame = clone.GetComponent<Image>();
			if (frame != null)
				frame.raycastTarget = true;
			if (_layout != null)
			{
				_layout.preferredWidth = Width * Plugin.UiScale.Value;
				_layout.minWidth = 200f;
			}
			Plugin.Logger.LogInfo("Bulk recruit card built from the game's tooltip.");
			return true;
		}

		void Attach(RegimentManagementPanel panel)
		{
			Canvas canvas = null;
			if (panel != null)
				canvas = panel.GetComponentInParent<Canvas>();
			if (canvas == null && _card.transform.parent != null)
				canvas = _card.transform.parent.GetComponentInParent<Canvas>();
			if (canvas == null)
				canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
			if (canvas == null)
				return;
			canvas = canvas.rootCanvas;
			if (_card.transform.parent != canvas.transform)
			{
				_card.transform.SetParent(canvas.transform, false);
				_card.transform.localScale = Vector3.one;
				_card.transform.localRotation = Quaternion.identity;
			}
			if (!_card.activeSelf)
				_card.SetActive(true);
			for (var t = _text.transform; t != null && t != canvas.transform; t = t.parent)
				if (!t.gameObject.activeSelf)
					t.gameObject.SetActive(true);
			_card.transform.SetAsLastSibling();
		}

		// ---------------- Input ----------------

		static Camera CamFor(Transform t)
		{
			var canvas = t.GetComponentInParent<Canvas>();
			if (canvas == null)
				return null;
			canvas = canvas.rootCanvas;
			return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
		}

		void HandleInput(RegimentManagementPanel panel)
		{
			if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
			{
				if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
					Step(+1);
				else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
					Step(-1);
			}
			if (!Input.GetMouseButtonDown(0))
				return;
			var cam = CamFor(_text.transform);
			int idx = TMP_TextUtilities.FindIntersectingLink(_text, Input.mousePosition, cam);
			if (idx < 0)
				return;
			string id = _text.textInfo.linkInfo[idx].GetLinkID();
			switch (id)
			{
				case "minus": Step(-1); break;
				case "plus": Step(+1); break;
				case "one": BulkRecruit.Copies = 1; break;
				case "max": BulkRecruit.Copies = Math.Max(1, Cap()); break;
				case "design":
					var design = BulkRecruit.LastDesignFor(panel.initialRegiment);
					if (design != null)
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
					break;
			}
			_nextRefresh = 0f;
		}

		int Cap()
		{
			int cap = Plugin.MaxCopies.Value;
			if (_plan != null && _plan.LimitedBy != null)
				cap = Math.Min(cap, Math.Max(1, _plan.MaxAffordable));
			return cap;
		}

		void Step(int d)
		{
			BulkRecruit.Copies = Math.Max(1, Math.Min(Cap(), BulkRecruit.Copies + d));
		}

		// ---------------- Content ----------------

		static string N(float v)
		{
			if (v >= 1_000_000f)
				return (v / 1_000_000f).ToString("0.#", CultureInfo.InvariantCulture) + "M";
			if (v >= 100_000f)
				return (v / 1000f).ToString("0", CultureInfo.InvariantCulture) + "K";
			return Mathf.RoundToInt(v).ToString("N0", CultureInfo.InvariantCulture);
		}

		static string Link(string id, string label, bool enabled)
		{
			return enabled
				? $"<link=\"{id}\"><color={Gold}><b>[ {label} ]</b></color></link>"
				: $"<color={Grey}>[ {label} ]</color>";
		}

		static string Line(string label, float need, float have)
		{
			bool ok = need <= have;
			return $"\n  {label}: <b>{N(need)}</b> <color={(ok ? Grey : Bad)}>(have {N(have)})</color>";
		}

		void Refresh(RegimentManagementPanel panel, bool showMessage)
		{
			var sb = new StringBuilder();
			if (panel != null)
			{
				var reg = panel.regiment?.TryCast<World.RegimentInitData>();
				bool hasCommander = panel.regimentOfficerPanel?.officer != null;
				_plan = CostPlanner.Build(reg, panel.Locality, Math.Max(1, BulkRecruit.Copies), Plugin.MaxCopies.Value, hasCommander);
				int cap = Cap();
				if (BulkRecruit.Copies > cap)
					BulkRecruit.Copies = cap; // never leave an out-of-range choice selected
				int copies = BulkRecruit.Copies;

				sb.Append("<b>BULK RECRUIT</b>\n");
				sb.Append(Link("minus", "-", copies > 1)).Append("   <size=130%><b>").Append(copies).Append("</b></size>   ");
				sb.Append(Link("plus", "+", copies < cap)).Append("  ");
				sb.Append(Link("max", $"Max {cap}", copies != cap)).Append("  ");
				sb.Append(Link("one", "1", copies != 1));

				var p = _plan;
				if (p.Affordable == 0)
				{
					sb.Append($"\n<color={Bad}>Can't create this regiment: {p.LimitedBy}.</color>");
				}
				else
				{
					sb.Append($"\nTotal for {p.Affordable} regiment{(p.Affordable == 1 ? "" : "s")}:");
					sb.Append(Line("Money", p.Money, p.HaveMoney));
					if (p.Officers > 0)
						sb.Append(Line("Officers", p.Officers, p.HaveOfficers));
					if (p.Renown > 0)
						sb.Append(Line("Renown", p.Renown, p.HaveRenown));
					foreach (var (name, need, have, _) in p.Items)
						sb.Append(Line(name, need, have));
					if (p.Affordable > 1)
						sb.Append($"\n  Commanders: <b>{p.Affordable - 1}</b> more <color={Grey}>(best free officers in reserve: {p.HaveCommanders})</color>");
					if (p.HaveWorkforce >= 0)
					{
						bool ok = p.Workforce <= p.HaveWorkforce;
						sb.Append($"\n  Soldiers: <b>{N(p.Workforce)}</b> <color={(ok ? Grey : Warn)}>(from {p.WorkforceSource}: {N(p.HaveWorkforce)})</color>");
					}
					foreach (var note in p.SubstitutionNotes)
						sb.Append($"\n<color={Warn}>Swap: {note}.</color>");
					if (p.LimitedBy != null)
						sb.Append($"\n<color={Grey}>Max {p.MaxAffordable}: {p.LimitedBy} for more.</color>");
				}
				sb.Append($"\n<color={Grey}>{(Plugin.AutoCommanders.Value ? "Extra regiments get your best free officers as commanders. " : "")}Press the game's Create button to recruit.</color>");

				var design = BulkRecruit.LastDesignFor(panel.initialRegiment);
				if (design != null)
					sb.Append("\n").Append(Link("design", $"Use last design ({design.Label})", true));
			}
			if (showMessage)
			{
				if (sb.Length > 0)
					sb.Append("\n");
				sb.Append($"<color={Gold}>{BulkRecruit.Message}</color>");
			}

			_text.text = sb.ToString();
			if (_layout != null)
				_layout.preferredWidth = Width * Plugin.UiScale.Value;
			LayoutRebuilder.ForceRebuildLayoutImmediate(_cardRt);
			Place(panel);
		}

		// ---------------- Placement ----------------

		static Rect ScreenRect(RectTransform rt)
		{
			var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
			rt.GetWorldCorners(c);
			var cam = CamFor(rt);
			Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
			Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
			return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
		}

		static void Grow(ref Rect u, ref bool any, Component c)
		{
			if (c == null || !c.gameObject.activeInHierarchy)
				return;
			var rt = c.GetComponent<RectTransform>();
			if (rt == null)
				return;
			var r = ScreenRect(rt);
			if (r.width <= 0f || r.width > Screen.width * 0.9f)
				return;
			u = any ? Rect.MinMaxRect(Math.Min(u.xMin, r.xMin), Math.Min(u.yMin, r.yMin), Math.Max(u.xMax, r.xMax), Math.Max(u.yMax, r.yMax)) : r;
			any = true;
		}

		void Place(RegimentManagementPanel panel)
		{
			var parent = _cardRt.parent != null ? _cardRt.parent.GetComponent<RectTransform>() : _cardRt;
			var cam = CamFor(_cardRt);
			Rect me = ScreenRect(_cardRt);
			float margin = 12f;
			float x, yTop;

			// To the right of the recruit screen's visible parts (regiment card + company pickers).
			Rect u = default;
			bool any = false;
			if (panel != null)
			{
				Grow(ref u, ref any, panel.regimentInfoPanel);
				Grow(ref u, ref any, panel.companySelectPanel);
				Grow(ref u, ref any, panel.companyCreatePanel);
				Grow(ref u, ref any, panel.companyWeaponPanel);
			}
			if (any)
			{
				x = u.xMax + margin;
				if (x + me.width > Screen.width - margin)
					x = u.xMin - margin - me.width;
				yTop = u.yMax;
			}
			else
			{
				x = (Screen.width - me.width) / 2f;
				yTop = Screen.height * 0.88f;
			}
			x = Mathf.Clamp(x, margin, Math.Max(margin, Screen.width - me.width - margin));
			if (yTop - me.height < margin)
				yTop = me.height + margin;
			if (yTop > Screen.height - margin)
				yTop = Screen.height - margin;

			var want = new Vector2(x, yTop);
			var have = new Vector2(me.xMin, me.yMax);
			if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, want, cam, out Vector3 wWant) &&
				RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, have, cam, out Vector3 wHave))
				_cardRt.position += wWant - wHave;
		}
	}
}
