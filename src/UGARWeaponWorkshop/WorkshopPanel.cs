using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using World;
using World.SceneObject;
using Filters = World.UI.GeneralPage.Production.NewOrder.Filters;

namespace UGARWeaponWorkshop
{
	/// <summary>
	/// The Weapon Workshop window: a framed panel in the production screen's art (see Ui.cs), docked to the right of the
	/// New Order list. Left: the player's design cards (icon, bars, stock, Mk II / Rename / Archive, or Restore / Delete
	/// in the Archived view). Right: the designer (base, part toggles, base-vs-design bars, costs). Opened by the WORKSHOP
	/// tab under MUSKETS/CANNONS/SHIPS/SUPPLY, or the hotkey (F10) anywhere on the campaign map. The frame catches
	/// clicks, so nothing reaches the map; Esc closes.
	/// </summary>
	public class WorkshopPanel : MonoBehaviour
	{
		public WorkshopPanel(IntPtr ptr) : base(ptr) { }

		const float W = 860f, H = 660f, LeftX = 16f, LeftW = 330f, RightX = 366f, RightW = 478f;
		const int CardsPerPage = 3;
		const float CardH = 150f, CardGap = 8f;

		sealed class Hit
		{
			public RectTransform Rt;
			public Action Act;
			public Image Img;
			public Color Normal;
		}

		Kit _kit;
		GameObject _root;
		RectTransform _rootRt;
		RectTransform _content;
		readonly List<Hit> _hits = new List<Hit>();
		Hit _hover;
		bool _broken;
		int _failures;

		// Tab in the production filters
		GameObject _tab;
		IntPtr _tabFilters;
		RectTransform _tabRt;

		// State
		bool _open, _dirty = true, _showArchived, _editing, _nameEdited;
		int _page;
		PlayerDesign _markOf;
		Design _draft;
		PlayerDesign _confirmDelete;
		string _message;
		bool _messageBad;
		float _messageUntil, _nextRefresh, _nextAdopt;

		// Renaming: the draft (target null) or an existing design
		bool _renaming;
		PlayerDesign _renameTarget;
		TMP_InputField _input;
		string _imguiText = "";
		bool _imguiFocus;

		static string N(float v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);

		void OnDestroy()
		{
			try { if (_root != null) Destroy(_root); } catch (Exception) { }
			try { if (_tab != null) Destroy(_tab); } catch (Exception) { }
		}

		// ---------------- Frame loop ----------------

		void Update()
		{
			if (_broken)
				return;
			try
			{
				if (Time.realtimeSinceStartup >= _nextAdopt)
				{
					// Designs made before a live reload of this plugin: give them their names back (Registry.AdoptExisting).
					_nextAdopt = Time.realtimeSinceStartup + 2f;
					var player = Registry.PlayerCountry();
					if (player != null)
						Registry.AdoptExisting(player);
				}
				var country = Plugin.Enabled.Value ? Registry.PlayerCountry() : null;
				var filters = OpenFilters();
				EnsureTab(filters, country != null);
				if (country == null)
				{
					if (_open) Close();
					HideRoot();
					return;
				}
				if (!_renaming && Input.GetKeyDown(Plugin.HotkeyCode()))
					Toggle();
				if (Input.GetKeyDown(KeyCode.Escape))
				{
					if (_renaming) EndRename(false);
					else if (_open) Close();
				}
				if (_renaming && _input != null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
					EndRename(true);

				if (Input.GetMouseButtonDown(0))
					Click(country);
				if (!_open)
				{
					HideRoot();
					return;
				}
				if (!EnsureRoot(filters))
					return;
				if (_dirty || (!_renaming && Time.realtimeSinceStartup >= _nextRefresh))
				{
					_dirty = false;
					_nextRefresh = Time.realtimeSinceStartup + 2f;
					Rebuild(country);
					Place(filters);
				}
				KeepFocus();
				Hover();
				_failures = 0;
			}
			catch (Exception e)
			{
				if (++_failures >= 5)
				{
					_broken = true;
					Plugin.Logger.LogError($"Weapon Workshop window disabled after repeated errors: {e}");
					HideRoot();
				}
				else
				{
					Plugin.Logger.LogWarning($"Weapon Workshop window hiccup: {e}");
					_dirty = true;
				}
			}
		}

		static Filters OpenFilters()
		{
			var p = Patches.OpenProduction;
			var f = Patches.OpenFilters;
			try
			{
				if (p == null || !p.gameObject.activeInHierarchy)
					return null;
				f = Patches.OpenFilters = p.filters;
				return f != null && f.gameObject.activeInHierarchy ? f : null;
			}
			catch (Exception)
			{
				Patches.OpenProduction = null;
				Patches.OpenFilters = null;
				return null;
			}
		}

		void Toggle()
		{
			if (_open) Close();
			else Open();
		}

		void Open()
		{
			_open = true;
			_dirty = true;
		}

		void Close()
		{
			_open = false;
			_editing = false;
			_confirmDelete = null;
			EndRename(false);
			_dirty = true;
		}

		void Say(string msg, bool bad = false, bool log = true)
		{
			_message = msg;
			_messageBad = bad;
			_messageUntil = Time.realtimeSinceStartup + 15f;
			_dirty = true;
			if (log) Plugin.Logger.LogInfo(msg);
		}

		// ---------------- Root panel ----------------

		void HideRoot()
		{
			if (_root == null) return;
			try { if (_root.activeSelf) _root.SetActive(false); }
			catch (Exception) { _root = null; }
		}

		static Canvas RootCanvas(Filters filters)
		{
			Canvas c = null;
			try
			{
				if (filters != null) c = filters.GetComponentInParent<Canvas>();
				if (c == null && Patches.OpenProduction != null) c = Patches.OpenProduction.GetComponentInParent<Canvas>();
			}
			catch (Exception) { }
			if (c == null)
			{
				foreach (var o in UnityEngine.Object.FindObjectsOfType<Canvas>())
					if (o != null && o.isRootCanvas && o.renderMode == RenderMode.ScreenSpaceOverlay && (c == null || o.sortingOrder > c.sortingOrder))
						c = o;
			}
			return c?.rootCanvas;
		}

		bool EnsureRoot(Filters filters)
		{
			if (_kit == null || (!_kit.FromProduction && filters != null))
			{
				_kit = Ui.Discover(filters, _kit);
				Plugin.Logger.LogInfo($"Workshop window look from the {_kit.Source}.");
				if (_root != null) { Destroy(_root); _root = null; }
			}
			var canvas = RootCanvas(filters);
			if (canvas == null)
				return false;
			if (_root == null)
			{
				_rootRt = Ui.Node("WeaponWorkshopPanel", canvas.transform);
				_root = _rootRt.gameObject;
				UnityEngine.Object.DontDestroyOnLoad(_root);
				// A solid dark panel (never a sprite-less white Image): it also catches clicks so they never reach the map.
				var frame = _root.AddComponent<Image>();
				frame.sprite = null;
				frame.color = Ui.PanelColor;
				frame.raycastTarget = true;
				// The game tooltip's dark frame on top for its edge art, then a thin bronze border.
				if (_kit.Frame != null)
				{
					var art = Ui.Img(_root.transform, _kit.Frame, _kit.FrameType, new Color(1f, 1f, 1f, 0.9f), 0f, 0f, W, H);
					art.color = new Color(_kit.FrameColor.r, _kit.FrameColor.g, _kit.FrameColor.b, 0.9f);
				}
				Ui.Border(_root.transform, W, H, 2f, Ui.Bronze);
				var group = _root.AddComponent<CanvasGroup>();
				group.blocksRaycasts = true;
				group.interactable = true;
				group.ignoreParentGroups = true;
				_dirty = true;
			}
			if (_root.transform.parent != canvas.transform)
				_root.transform.SetParent(canvas.transform, false);
			if (!_root.activeSelf)
				_root.SetActive(true);
			_root.transform.SetAsLastSibling();
			float s = Plugin.UiScale.Value;
			_rootRt.sizeDelta = new Vector2(W, H);
			_rootRt.localScale = new Vector3(s, s, 1f);
			return true;
		}

		static Camera CamFor(Transform t)
		{
			var canvas = t.GetComponentInParent<Canvas>();
			if (canvas == null) return null;
			canvas = canvas.rootCanvas;
			return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
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

		/// <summary>Right of the production screen's New Order list (or centred), top aligned, kept on screen.</summary>
		void Place(Filters filters)
		{
			Rect me = ScreenRect(_rootRt);
			float margin = 12f, x, yTop;
			Rect dock = default;
			bool docked = false;
			try
			{
				if (filters != null)
				{
					dock = ScreenRect(filters.GetComponent<RectTransform>());
					var list = filters.itemList;
					if (list != null && list.gameObject.activeInHierarchy)
					{
						var r = ScreenRect(list.GetComponent<RectTransform>());
						dock = Rect.MinMaxRect(Math.Min(dock.xMin, r.xMin), Math.Min(dock.yMin, r.yMin), Math.Max(dock.xMax, r.xMax), Math.Max(dock.yMax, r.yMax));
					}
					docked = dock.width > 0f && dock.width < Screen.width * 0.8f;
				}
			}
			catch (Exception) { }
			if (docked && dock.xMax + margin + me.width <= Screen.width - margin)
			{
				x = dock.xMax + margin;
				yTop = dock.yMax;
			}
			else
			{
				x = (Screen.width - me.width) / 2f;
				yTop = (Screen.height + me.height) / 2f;
			}
			x = Mathf.Clamp(x, margin, Math.Max(margin, Screen.width - me.width - margin));
			yTop = Mathf.Clamp(yTop, Math.Min(Screen.height - margin, me.height + margin), Screen.height - margin);
			var parent = _rootRt.parent.GetComponent<RectTransform>();
			var cam = CamFor(_rootRt);
			if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, new Vector2(x, yTop), cam, out Vector3 want) &&
				RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, new Vector2(me.xMin, me.yMax), cam, out Vector3 have))
				_rootRt.position += want - have;
		}

		// ---------------- The WORKSHOP tab ----------------

		void EnsureTab(Filters filters, bool active)
		{
			try
			{
				if (_tab != null && (filters == null || filters.Pointer != _tabFilters))
				{
					// Belongs to an old filter panel (or the screen closed and was rebuilt): keep it only if still parented.
					if (_tab.transform == null || _tab.transform.parent == null) _tab = null;
				}
				if (filters == null)
					return;
				if (_kit == null || !_kit.FromProduction)
					_kit = Ui.Discover(filters, _kit);
				if (_tab == null && _kit.TabTemplate != null && filters.buttonGroup != null)
					BuildTab(filters);
				if (_tab != null && _tab.activeSelf != active)
					_tab.SetActive(active);
				if (_tab != null)
				{
					var label = _tab.GetComponentInChildren<TextMeshProUGUI>(true);
					if (label != null)
						label.color = _open ? Ui.Gold : _kit.Text;
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Workshop tab not added ({e.Message}); use the hotkey {Plugin.Hotkey.Value}.");
				_tab = null;
			}
		}

		void BuildTab(Filters filters)
		{
			var parent = filters.buttonGroup.transform;
			_tab = UnityEngine.Object.Instantiate(_kit.TabTemplate, parent, false);
			_tab.name = "WorkshopTab";
			_tabFilters = filters.Pointer;
			_tabRt = _tab.GetComponent<RectTransform>();
			var label = _tab.GetComponentInChildren<TextMeshProUGUI>(true);
			if (label != null) label.text = "WORKSHOP";
			// Below the lowest tab when the group isn't a layout group.
			if (parent.GetComponent<LayoutGroup>() == null)
			{
				float lowest = float.MaxValue, height = _tabRt.rect.height;
				for (int i = 0; i < parent.childCount; i++)
				{
					var c = parent.GetChild(i).GetComponent<RectTransform>();
					if (c == null || c == _tabRt) continue;
					lowest = Math.Min(lowest, c.anchoredPosition.y);
				}
				if (lowest < float.MaxValue)
					_tabRt.anchoredPosition = new Vector2(_tabRt.anchoredPosition.x, lowest - height - 10f);
			}
			_tab.transform.SetAsLastSibling();
			_tab.SetActive(true);
			Plugin.Logger.LogInfo("Workshop tab added under the production filters.");
		}

		// ---------------- Clicks ----------------

		void AddHit(RectTransform rt, Action act, Image img = null)
		{
			_hits.Add(new Hit { Rt = rt, Act = act, Img = img, Normal = img != null ? img.color : Color.white });
		}

		void Click(Country country)
		{
			var mouse = (Vector2)Input.mousePosition;
			try
			{
				if (_tab != null && _tab.activeInHierarchy && _tabRt != null &&
					RectTransformUtility.RectangleContainsScreenPoint(_tabRt, mouse, CamFor(_tabRt)))
				{
					Toggle();
					return;
				}
			}
			catch (Exception) { _tab = null; }
			if (!_open || _root == null || !_root.activeInHierarchy)
				return;
			for (int i = _hits.Count - 1; i >= 0; i--)
			{
				var h = _hits[i];
				if (h.Rt == null || !RectTransformUtility.RectangleContainsScreenPoint(h.Rt, mouse, CamFor(h.Rt)))
					continue;
				try
				{
					h.Act?.Invoke();
				}
				catch (Exception e)
				{
					Say("Something went wrong, see BepInEx\\LogOutput.log.", true, false);
					Plugin.Logger.LogError($"Weapon Workshop action failed: {e}");
				}
				_dirty = true;
				return;
			}
		}

		void Hover()
		{
			var mouse = (Vector2)Input.mousePosition;
			Hit over = null;
			for (int i = _hits.Count - 1; i >= 0 && over == null; i--)
			{
				var h = _hits[i];
				if (h.Img != null && h.Rt != null && RectTransformUtility.RectangleContainsScreenPoint(h.Rt, mouse, CamFor(h.Rt)))
					over = h;
			}
			if (over == _hover) return;
			if (_hover?.Img != null) _hover.Img.color = _hover.Normal;
			_hover = over;
			if (_hover?.Img != null)
			{
				var c = _hover.Normal;
				_hover.Img.color = new Color(Math.Min(1f, c.r * 1.3f + 0.05f), Math.Min(1f, c.g * 1.3f + 0.05f), Math.Min(1f, c.b * 1.3f + 0.05f), c.a);
			}
		}

		// ---------------- Building the content ----------------

		void Rebuild(Country country)
		{
			if (_input != null)
			{
				try { _imguiText = _input.text; } catch (Exception) { }
			}
			if (_content != null)
				Destroy(_content.gameObject);
			_hits.Clear();
			_hover = null;
			_input = null;
			_content = Ui.Node("Content", _root.transform);
			Ui.Place(_content, 0f, 0f, W, H);
			var k = _kit;
			var t = _content.transform;

			Ui.Txt(k, t, "WEAPON WORKSHOP", 26f, k.Header, 0f, 12f, W, 36f, TextAlignmentOptions.Center, header: true);
			Button(t, "X", W - 52f, 14f, 34f, 30f, Close);
			Ui.Img(t, k.BarBack, Image.Type.Sliced, new Color(k.Header.r, k.Header.g, k.Header.b, 0.35f), 20f, 52f, W - 40f, 2f);

			var designs = Registry.PlayerDesigns(country);
			LeftColumn(country, designs);
			if (_confirmDelete != null) ConfirmDelete(country);
			else if (_editing && _draft != null) Designer(country);
			else Welcome(country, designs);

			if (_message != null && Time.realtimeSinceStartup < _messageUntil)
				Ui.Txt(k, t, _message, 15f, _messageBad ? Ui.Bad : Ui.Gold, 20f, H - 36f, W - 32f, 30f, TextAlignmentOptions.MidlineLeft, wrap: true);
		}

		RectTransform Button(Transform parent, string label, float x, float y, float w, float h, Action act, bool enabled = true, bool selected = false)
		{
			var k = _kit;
			var color = selected ? new Color(Ui.Gold.r * 0.85f, Ui.Gold.g * 0.75f, Ui.Gold.b * 0.5f, 1f) : Ui.ButtonColor;
			if (!enabled) color = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f, 0.8f);
			var img = Ui.Img(parent, null, Image.Type.Simple, color, x, y, w, h);
			if (!selected)
				Ui.Border(img.transform, w, h, 1f, new Color(Ui.Bronze.r, Ui.Bronze.g, Ui.Bronze.b, enabled ? 0.7f : 0.3f));
			var txt = Ui.Txt(k, img.transform, label, Math.Min(15f, h * 0.5f), enabled ? (selected ? Color.white : k.Text) : Ui.Grey, 2f, 0f, w - 4f, h, TextAlignmentOptions.Center);
			txt.enableAutoSizing = true;
			txt.fontSizeMin = 8f;
			txt.fontSizeMax = Math.Min(15f, h * 0.5f);
			if (enabled && act != null)
				AddHit(img.rectTransform, act, img);
			return img.rectTransform;
		}

		// Fill fractions for bars (fixed ranges covering the vanilla small arms plus workshop headroom).
		static float EffFill(float effTimesTen) => (effTimesTen - 5f) / 13f;
		static float RangeFill(float r) => (r - 400f) / 300f;
		static float ReloadFill(float reload) => (130f - reload) / 105f;
		static float AccFill(float lo, float hi) => ((lo + hi) * 0.5f - 0.5f) / 0.8f;
		static float MeleeFill(float m100) => (m100 - 3f) / 12f;

		// ---- Left: design cards ----

		void LeftColumn(Country country, List<PlayerDesign> all)
		{
			var k = _kit;
			var t = _content.transform;
			int active = 0, archived = 0;
			foreach (var p in all) { if (p.Archived) archived++; else active++; }
			Button(t, $"DESIGNS ({active})", LeftX, 64f, 150f, 30f, () => { _showArchived = false; _page = 0; }, true, !_showArchived);
			Button(t, $"ARCHIVED ({archived})", LeftX + 158f, 64f, 150f, 30f, () => { _showArchived = true; _page = 0; }, true, _showArchived);

			var shown = all.FindAll(p => p.Archived == _showArchived);
			int pages = Math.Max(1, (shown.Count + CardsPerPage - 1) / CardsPerPage);
			_page = Mathf.Clamp(_page, 0, pages - 1);
			float y = 104f;
			if (shown.Count == 0)
			{
				Ui.Txt(k, t, _showArchived
					? "No archived designs. Archiving hides a design and stops its production; its guns keep working."
					: "No designs yet. Use NEW DESIGN to build your own musket or rifle from the guns you have researched or hold in storage.",
					15f, Ui.Grey, LeftX + 6f, y, LeftW - 12f, 80f, TextAlignmentOptions.TopLeft, wrap: true);
			}
			for (int i = _page * CardsPerPage; i < shown.Count && i < (_page + 1) * CardsPerPage; i++)
			{
				Card(country, shown[i], LeftX, y);
				y += CardH + CardGap;
			}
			if (pages > 1)
			{
				float py = 104f + CardsPerPage * (CardH + CardGap);
				Button(t, "<", LeftX, py, 40f, 28f, () => _page--, _page > 0);
				Ui.Txt(k, t, $"{_page + 1} / {pages}", 15f, k.Text, LeftX + 44f, py, 80f, 28f, TextAlignmentOptions.Center);
				Button(t, ">", LeftX + 128f, py, 40f, 28f, () => _page++, _page < pages - 1);
			}
		}

		void Card(Country country, PlayerDesign p, float x, float y)
		{
			var k = _kit;
			var e = p.E;
			var s = e.D.Final;
			var card = Ui.Img(_content.transform, null, Image.Type.Simple, Ui.CardColor, x, y, LeftW, CardH);
			var t = card.transform;

			Sprite icon = null;
			try { icon = e.Weapon.Icon; } catch (Exception) { }
			if (icon != null)
			{
				var img = Ui.Img(t, icon, Image.Type.Simple, Color.white, 6f, 6f, 110f, 40f);
				img.preserveAspect = true;
			}
			Ui.Txt(k, t, $"<b>{e.DisplayName}</b>", 16f, k.Text, 122f, 4f, LeftW - 128f, 22f);
			string baseName = Registry.Vanilla(e.D.Base)?.Name ?? e.D.Base;
			Ui.Txt(k, t, $"{baseName}: {e.D.PartsText()}", 11f, Ui.Grey, 122f, 26f, LeftW - 128f, 28f, TextAlignmentOptions.TopLeft, wrap: true);

			StatBar(t, "EFFICIENCY", N(s.Efficiency * 10f, "0.#"), EffFill(s.Efficiency * 10f), 6f, 56f);
			StatBar(t, "RELOADING", N(s.Reload, "0.#"), ReloadFill(s.Reload), 6f, 72f);
			StatBar(t, "MELEE", N(s.Melee * 100f, "0.#"), MeleeFill(s.Melee * 100f), 6f, 88f);
			float stock = Registry.Stock(country, e.Weapon);
			Ui.Txt(k, t, $"STOCK <b>{N(stock, "#,0")}</b>", 12f, k.Text, 236f, 54f, LeftW - 242f, 16f, TextAlignmentOptions.MidlineRight);
			Ui.Txt(k, t, $"PRICE <b>{N(s.Price, "0")}</b>", 12f, k.Text, 236f, 70f, LeftW - 242f, 16f, TextAlignmentOptions.MidlineRight);
			Ui.Txt(k, t, $"RANGE <b>{N(s.Range, "0")}</b>", 12f, k.Text, 236f, 86f, LeftW - 242f, 16f, TextAlignmentOptions.MidlineRight);

			float by = 112f;
			if (!p.Archived)
			{
				Button(t, "MK II", 6f, by, 92f, 28f, () => StartDraft(country, p));
				Button(t, "RENAME", 104f, by, 104f, 28f, () => { _editing = false; _confirmDelete = null; BeginRename(p); });
				Button(t, "ARCHIVE", 214f, by, 110f, 28f, () => Archive(country, p));
			}
			else
			{
				var u = Registry.UsageOf(country, e);
				Button(t, "RESTORE", 6f, by, 100f, 28f, () => Restore(country, p));
				Button(t, "DELETE", 112f, by, 92f, 28f, () => { _confirmDelete = p; _editing = false; }, !u.InUse);
				Ui.Txt(k, t, u.InUse ? "In use: " + u.Text() : "Not used: can be deleted", 11f, u.InUse ? Ui.Warn : Ui.Grey,
					210f, by - 6f, LeftW - 214f, 38f, TextAlignmentOptions.MidlineLeft, wrap: true);
			}
		}

		void StatBar(Transform t, string label, string value, float fill, float x, float y)
		{
			var k = _kit;
			Ui.Txt(k, t, label, 10f, Ui.Grey, x, y, 70f, 16f);
			Ui.Bar(k, t, x + 72f, y + 5f, 118f, 7f, fill);
			Ui.Txt(k, t, value, 12f, k.Text, x + 192f, y, 38f, 16f, TextAlignmentOptions.MidlineRight);
		}

		// ---- Right: welcome / designer / confirm ----

		void Welcome(Country country, List<PlayerDesign> designs)
		{
			var k = _kit;
			var t = _content.transform;
			Ui.Txt(k, t, "DESIGN A NEW WEAPON", 20f, k.Header, RightX, 64f, RightW, 30f, header: true);
			Ui.Txt(k, t,
				"Start from a musket, carbine or rifle you have researched (or hold in storage), then choose the barrel, rifling, " +
				"bayonet and build quality. Each number is capped near your best weapon, and better guns cost more to make.\n\n" +
				"Factories make a design like any other musket (production screen, MUSKETS). To improve a design, use MK II on its card: " +
				"the new version sits next to the old one, and regiments re-arm as the new guns arrive.",
				15f, k.Text, RightX, 104f, RightW, 210f, TextAlignmentOptions.TopLeft, wrap: true);
			int bases = Registry.UnlockedBases(country).Count;
			Button(t, "NEW DESIGN", RightX, 330f, 200f, 38f, () => StartDraft(country, null), bases > 0);
			if (bases == 0)
				Ui.Txt(k, t, "You have no muskets or rifles researched or in storage to build on.", 14f, Ui.Warn, RightX, 376f, RightW, 40f, TextAlignmentOptions.TopLeft, wrap: true);
			if (_renaming && _renameTarget != null)
				RenameBox(t, RightX, 430f, $"Rename {_renameTarget.E.DisplayName}:");
		}

		void Designer(Country country)
		{
			var k = _kit;
			var t = _content.transform;
			var s = Preview(country, out var b, out var capped, out var bases);
			var bw = Registry.Vanilla(_draft.Base);
			bool baseRifled = Parts.IsRifled(_draft.Category);
			bool haveRifle = Donor(bases) != null;
			float x = RightX, y = 62f;

			Ui.Txt(k, t, _markOf != null ? $"IMPROVING {_markOf.E.DisplayName.ToUpperInvariant()}" : "NEW DESIGN", 20f, k.Header, x, y, RightW, 28f, header: true);
			y += 30f;
			if (_renaming && _renameTarget == null)
				RenameBox(t, x, y, "Name:");
			else
			{
				Ui.Txt(k, t, $"<color=#8A8576>Name</color>  <b>{_draft.Name}</b>", 17f, k.Text, x, y, RightW - 130f, 30f);
				Button(t, "RENAME", x + RightW - 120f, y, 120f, 30f, () => BeginRename(null));
			}
			y += 40f;

			Ui.Txt(k, t, "BASE", 13f, Ui.Grey, x, y, 90f, 40f);
			Button(t, "<", x + 92f, y + 4f, 34f, 32f, () => StepBase(country, -1), bases.Count > 1);
			Sprite icon = null;
			try { icon = bw?.Icon; } catch (Exception) { }
			if (icon != null) { var img = Ui.Img(t, icon, Image.Type.Simple, Color.white, x + 132f, y, 110f, 40f); img.preserveAspect = true; }
			Ui.Txt(k, t, $"<b>{bw?.Name ?? _draft.Base}</b>\n<size=80%><color=#8A8576>{Parts.CategoryName(_draft.Category)}</color></size>", 15f, k.Text, x + 248f, y, RightW - 300f, 40f);
			Button(t, ">", x + RightW - 40f, y + 4f, 34f, 32f, () => StepBase(country, +1), bases.Count > 1);
			y += 40f;

			PartRow(t, "BARREL", x, y, Parts.Barrels, c => Parts.BarrelName(c), _draft.Barrel, c => _draft.Barrel = c, c => true, country);
			y += 34f;
			if (baseRifled)
			{
				Ui.Txt(k, t, "RIFLING", 13f, Ui.Grey, x, y, 90f, 30f);
				Ui.Txt(k, t, "Rifled (built in)", 15f, k.Text, x + 92f, y, RightW - 92f, 30f);
			}
			else
			{
				PartRow(t, "RIFLING", x, y, Parts.Riflings, c => Parts.RiflingName(c), _draft.Rifling, c => _draft.Rifling = c, c => c != 'R' || haveRifle, country);
				if (!haveRifle)
					Ui.Txt(k, t, "research a rifle first", 12f, Ui.Grey, x + 300f, y, RightW - 300f, 30f);
			}
			y += 34f;
			PartRow(t, "BAYONET", x, y, Parts.Bayonets, c => Parts.BayonetName(c), _draft.Bayonet, c => _draft.Bayonet = c, c => true, country);
			y += 34f;
			PartRow(t, "QUALITY", x, y, Parts.Qualities, c => Parts.QualityName(c), _draft.Quality, c => _draft.Quality = c, c => true, country);
			y += 40f;

			// Base vs design
			Ui.Txt(k, t, "BASE", 12f, Ui.Grey, x + 270f, y, 70f, 18f, TextAlignmentOptions.MidlineRight);
			Ui.Txt(k, t, "DESIGN", 12f, Ui.Grey, x + 350f, y, 80f, 18f, TextAlignmentOptions.MidlineRight);
			y += 20f;
			Compare(t, "EFFICIENCY", x, y, EffFill(b.Efficiency * 10f), EffFill(s.Efficiency * 10f), N(b.Efficiency * 10f, "0.#"), N(s.Efficiency * 10f, "0.#"), s.Efficiency - b.Efficiency); y += 22f;
			Compare(t, "RANGE", x, y, RangeFill(b.Range), RangeFill(s.Range), N(b.Range, "0"), N(s.Range, "0"), s.Range - b.Range); y += 22f;
			Compare(t, "RELOADING", x, y, ReloadFill(b.Reload), ReloadFill(s.Reload), N(b.Reload, "0.#"), N(s.Reload, "0.#"), b.Reload - s.Reload); y += 22f;
			Compare(t, "ACCURACY", x, y, AccFill(b.Low, b.High), AccFill(s.Low, s.High), $"{N(b.Low)}-{N(b.High)}", $"{N(s.Low)}-{N(s.High)}", (s.Low + s.High) - (b.Low + b.High)); y += 22f;
			Compare(t, "MELEE", x, y, MeleeFill(b.Melee * 100f), MeleeFill(s.Melee * 100f), N(b.Melee * 100f, "0.#"), N(s.Melee * 100f, "0.#"), s.Melee - b.Melee); y += 26f;

			string Cost(string label, float was, float now, string fmt) =>
				$"<color=#8A8576>{label}</color> {N(was, fmt)} » <color={(now > was + 1e-4f ? "#E0705A" : now < was - 1e-4f ? "#9CCB7A" : "#DBD1B8")}>{N(now, fmt)}</color>";
			Ui.Txt(k, t, Cost("Price", b.Price, s.Price, "0") + "     " + Cost("Factory work", b.Work, s.Work, "0.###"), 14f, k.Text, x, y, RightW, 20f);
			y += 20f;
			Ui.Txt(k, t, Cost("Money per gun", b.Gold, s.Gold, "0.##") + "     " + Cost("Iron per gun", b.Iron, s.Iron, "0.###"), 14f, k.Text, x, y, RightW, 20f);
			y += 24f;
			if (capped.Count > 0)
				Ui.Txt(k, t, "Capped by your best weapons: " + string.Join(", ", capped) + ".", 12f, Ui.Warn, x, y, RightW, 30f, TextAlignmentOptions.TopLeft, wrap: true);
			else if (_draft.AddsRifling && !string.IsNullOrEmpty(_draft.Donor))
				Ui.Txt(k, t, $"Rifling takes the range curve of the {Registry.Vanilla(_draft.Donor)?.Name ?? _draft.Donor}.", 12f, Ui.Grey, x, y, RightW, 30f, TextAlignmentOptions.TopLeft, wrap: true);
			y = Math.Max(y + 34f, H - 118f);

			float cost = Design.DevelopmentCost(s, _markOf != null, Plugin.DevelopmentCost.Value);
			float money = country.inventory.Money;
			bool afford = money >= cost;
			Ui.Txt(k, t, $"Development cost <b>{N(cost, "#,0")}</b> <color={(afford ? "#8A8576" : "#E0705A")}>(treasury {N(money, "#,0")})</color>", 15f, k.Text, x, y, RightW, 24f);
			y += 30f;
			Button(t, _markOf != null ? "CREATE MK" : "CREATE DESIGN", x, y, 200f, 34f, () => CreateDesign(country), afford);
			Button(t, "CANCEL", x + 210f, y, 120f, 34f, () => { _editing = false; _markOf = null; EndRename(false); });
		}

		void PartRow(Transform t, string label, float x, float y, char[] options, Func<char, string> name, char current, Action<char> set, Func<char, bool> allowed, Country country)
		{
			Ui.Txt(_kit, t, label, 13f, Ui.Grey, x, y, 90f, 30f);
			float bw = Math.Min(120f, (RightW - 92f - (options.Length - 1) * 6f) / options.Length);
			for (int i = 0; i < options.Length; i++)
			{
				char c = options[i];
				Button(t, name(c).ToUpperInvariant(), x + 92f + i * (bw + 6f), y, bw, 30f, () =>
				{
					set(c);
					if (!_nameEdited) _draft.Name = DefaultName(country);
				}, allowed(c), current == c);
			}
		}

		void Compare(Transform t, string label, float x, float y, float baseFill, float designFill, string baseText, string designText, float better)
		{
			var k = _kit;
			Ui.Txt(k, t, label, 11f, Ui.Grey, x, y, 88f, 20f);
			Ui.Bar(k, t, x + 90f, y + 3f, 170f, 5f, baseFill, Ui.Grey);
			Color dc = Math.Abs(better) < 1e-4f ? k.BarFillColor : better > 0 ? Ui.Good : Ui.Bad;
			Ui.Bar(k, t, x + 90f, y + 11f, 170f, 5f, designFill, dc);
			Ui.Txt(k, t, baseText, 13f, Ui.Grey, x + 270f, y, 70f, 20f, TextAlignmentOptions.MidlineRight);
			Ui.Txt(k, t, designText, 14f, Math.Abs(better) < 1e-4f ? k.Text : better > 0 ? Ui.Good : Ui.Bad, x + 350f, y, 80f, 20f, TextAlignmentOptions.MidlineRight);
		}

		void ConfirmDelete(Country country)
		{
			var k = _kit;
			var t = _content.transform;
			var p = _confirmDelete;
			Ui.Txt(k, t, "DELETE DESIGN", 20f, k.Header, RightX, 64f, RightW, 30f, header: true);
			Ui.Txt(k, t,
				$"Delete <b>{p.E.DisplayName}</b> permanently?\n\nIt disappears from the workshop and can't be ordered again. " +
				"Nothing in your army or storage uses it now. The development cost is not refunded.\n\n" +
				"If guns of this design turn up again (an old save, loot), they still work while the Weapon Workshop is installed.",
				15f, k.Text, RightX, 104f, RightW, 200f, TextAlignmentOptions.TopLeft, wrap: true);
			Button(t, "DELETE", RightX, 320f, 140f, 36f, () => Delete(country, p));
			Button(t, "KEEP", RightX + 150f, 320f, 140f, 36f, () => _confirmDelete = null);
		}

		// ---------------- Renaming ----------------

		void BeginRename(PlayerDesign target)
		{
			_renaming = true;
			_renameTarget = target;
			_imguiText = target != null ? target.E.DisplayName : _draft?.Name ?? "";
			_imguiFocus = true;
		}

		// The name box is a TMP_InputField built in code, like the game's own name fields (regiment name, save name).
		// The game only silences its campaign-map hotkeys when SceneManager.IsInputLocked is true, i.e. when the
		// EventSystem's selected object has a focused TMP_InputField. The 1.1.0/1.1.1 box
		// was a copy of the production count field (a legacy InputField, non-interactable while "continuous" is on), and a
		// click on our frame cleared the EventSystem selection, so it never took keys. KeepFocus() re-selects it each frame.
		void RenameBox(Transform t, float x, float y, string label)
		{
			var k = _kit;
			Ui.Txt(k, t, label, 14f, Ui.Grey, x, y, 120f, 30f);
			try
			{
				_input = NameField(t, x + 124f, y, RightW - 124f - 170f, 30f, _imguiText);
			}
			catch (Exception e)
			{
				_input = null; // the IMGUI box in OnGUI takes over
				Plugin.Logger.LogWarning($"Workshop name field not built ({e.Message}); using the simple text box.");
			}
			Button(t, "OK", x + RightW - 160f, y, 70f, 30f, () => EndRename(true));
			Button(t, "CANCEL", x + RightW - 84f, y, 84f, 30f, () => EndRename(false));
		}

		TMP_InputField NameField(Transform parent, float x, float y, float w, float h, string text)
		{
			var k = _kit;
			var box = Ui.Img(parent, k.BarBack, Image.Type.Sliced, new Color(0.05f, 0.05f, 0.05f, 0.95f), x, y, w, h, raycast: true);
			var go = box.gameObject;
			go.name = "WorkshopNameInput";
			go.SetActive(false); // wire everything before TMP_InputField.OnEnable runs

			var area = Ui.Node("TextArea", go.transform);
			area.anchorMin = Vector2.zero;
			area.anchorMax = Vector2.one;
			area.pivot = new Vector2(0.5f, 0.5f);
			area.offsetMin = new Vector2(8f, 2f);
			area.offsetMax = new Vector2(-8f, -2f);
			area.gameObject.AddComponent<RectMask2D>();

			var label = Ui.Txt(k, area, "", 16f, k.Text, 0f, 0f, 10f, 10f, TextAlignmentOptions.MidlineLeft);
			var lrt = label.rectTransform;
			lrt.anchorMin = Vector2.zero;
			lrt.anchorMax = Vector2.one;
			lrt.pivot = new Vector2(0.5f, 0.5f);
			lrt.offsetMin = Vector2.zero;
			lrt.offsetMax = Vector2.zero;
			label.richText = false;
			label.enableWordWrapping = false;
			label.overflowMode = TextOverflowModes.Overflow;

			var field = go.AddComponent<TMP_InputField>();
			field.textViewport = area;
			field.textComponent = label;
			field.targetGraphic = box;
			if (k.Font != null) field.fontAsset = k.Font;
			field.pointSize = 16f;
			field.richText = false;
			field.lineType = TMP_InputField.LineType.SingleLine;
			field.contentType = TMP_InputField.ContentType.Standard;
			field.characterLimit = 40;
			field.caretColor = Ui.Gold;
			field.customCaretColor = true;
			field.selectionColor = new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.35f);
			field.resetOnDeActivation = false;
			field.restoreOriginalTextOnEscape = false;
			go.SetActive(true);
			field.text = text ?? "";
			_focusFrames = 10;
			return field;
		}

		int _focusFrames;

		/// <summary>While renaming, keep the name field selected and focused (this is also what locks the game's hotkeys).</summary>
		void KeepFocus()
		{
			if (!_renaming || _input == null)
				return;
			try
			{
				var es = UnityEngine.EventSystems.EventSystem.current;
				if (es != null && es.currentSelectedGameObject != _input.gameObject)
					es.SetSelectedGameObject(_input.gameObject);
				if (!_input.isFocused)
				{
					_input.ActivateInputField();
					if (_focusFrames > 0)
					{
						_focusFrames--;
						if (_focusFrames == 0)
							_input.MoveTextEnd(false); // caret at the end once it's focused
					}
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Workshop name field focus failed: {e.Message}");
			}
		}

		static void ReleaseFocus()
		{
			try
			{
				var es = UnityEngine.EventSystems.EventSystem.current;
				var sel = es?.currentSelectedGameObject;
				if (sel != null && sel.name == "WorkshopNameInput")
					es.SetSelectedGameObject(null);
			}
			catch (Exception) { }
		}

		void EndRename(bool apply)
		{
			if (!_renaming)
				return;
			string text = _imguiText;
			try { if (_input != null) text = _input.text; } catch (Exception) { }
			text = (text ?? "").Trim();
			var target = _renameTarget;
			_renaming = false;
			ReleaseFocus();
			_renameTarget = null;
			_dirty = true;
			if (!apply || text.Length == 0)
				return;
			var country = Registry.PlayerCountry();
			if (target == null)
			{
				if (_draft != null) { _draft.Name = text; _nameEdited = true; }
				return;
			}
			if (country == null)
				return;
			string name = UniqueName(country, text, target.E);
			Registry.SetState(country, target, target.Archived, name);
			Say($"Renamed to {name}.");
		}

		// IMGUI fallback when the game's input field couldn't be borrowed.
		void OnGUI()
		{
			if (!_renaming || _input != null || _root == null || !_open)
				return;
			Rect panel;
			try { panel = ScreenRect(_rootRt); }
			catch (Exception) { return; }
			float w = 420f;
			var r = new Rect(panel.xMin + panel.width * 0.47f, Screen.height - panel.yMax + 96f, w, 64f);
			GUI.Box(r, "Name");
			var e = Event.current;
			if (e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
			{
				EndRename(true);
				e.Use();
				return;
			}
			GUI.SetNextControlName("ugarww-name");
			_imguiText = GUI.TextField(new Rect(r.x + 8f, r.y + 28f, w - 16f, 26f), _imguiText ?? "", 40);
			if (_imguiFocus) { GUI.FocusControl("ugarww-name"); _imguiFocus = false; }
		}

		// ---------------- Actions ----------------

		void Archive(Country country, PlayerDesign p)
		{
			int orders = Registry.CancelOrders(country, p.E);
			Registry.SetState(country, p, true, p.S.Name);
			Say($"{p.E.DisplayName} archived{(orders > 0 ? $" and {orders} factory order{(orders == 1 ? "" : "s")} cancelled" : "")}. " +
				"Guns already made keep working; RESTORE brings it back.");
		}

		void Restore(Country country, PlayerDesign p)
		{
			Registry.SetState(country, p, false, p.S.Name);
			Say($"{p.E.DisplayName} restored: it can be ordered again.");
		}

		void Delete(Country country, PlayerDesign p)
		{
			_confirmDelete = null;
			var u = Registry.UsageOf(country, p.E);
			if (u.InUse)
			{
				Say($"{p.E.DisplayName} can't be deleted: {u.Text()}.", true);
				return;
			}
			Registry.Delete(country, p);
			Say($"{p.E.DisplayName} deleted.");
		}

		// ---------------- Drafts ----------------

		void StartDraft(Country country, PlayerDesign markOf)
		{
			var bases = Registry.UnlockedBases(country);
			_confirmDelete = null;
			EndRename(false);
			_markOf = markOf;
			if (markOf != null)
			{
				_draft = markOf.E.D.CloneParts();
				_draft.Name = UniqueName(country, Design.NextMark(markOf.E.DisplayName), null);
				_nameEdited = true;
			}
			else
			{
				if (bases.Count == 0)
				{
					Say("You have no muskets or rifles researched or in storage to build on.", true);
					return;
				}
				WeaponTemplate best = bases[0];
				foreach (var w in bases)
					if (w.Efficiency > best.Efficiency) best = w;
				_draft = new Design { Base = best.name };
				_nameEdited = false;
				SetBase(best.name, country);
			}
			_editing = true;
		}

		void StepBase(Country country, int dir)
		{
			var bases = Registry.UnlockedBases(country);
			if (bases.Count == 0 || _draft == null) return;
			int i = bases.FindIndex(w => w.name == _draft.Base);
			i = (i + dir + bases.Count) % bases.Count;
			SetBase(bases[i].name, country);
		}

		void SetBase(string name, Country country)
		{
			_draft.Base = name;
			_draft.Rifling = Parts.IsRifled(Parts.CategoryOf(name)) ? 'R' : 'N';
			if (!_nameEdited) _draft.Name = DefaultName(country);
		}

		string DefaultName(Country country)
		{
			int n = Registry.PlayerDesigns(country).Count + 1;
			var cat = _draft.AddsRifling ? Category.Rifle : _draft.Category;
			string kind = cat switch { Category.Carbine => "Carbine", Category.Rifle => "Rifle", Category.BreechLoader => "Breech Rifle", _ => "Musket" };
			if (_draft.Barrel == 'S' && cat == Category.Musket) kind = "Short Musket";
			return UniqueName(country, $"Pattern {n} {kind}", null);
		}

		static string UniqueName(Country country, string name, Entry except)
		{
			var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var p in Registry.PlayerDesigns(country))
				if (p.E != except) taken.Add(p.E.DisplayName);
			if (!taken.Contains(name)) return name;
			for (int i = 2; ; i++)
				if (!taken.Contains($"{name} ({i})")) return $"{name} ({i})";
		}

		static WeaponTemplate Donor(List<WeaponTemplate> bases)
		{
			WeaponTemplate best = null;
			foreach (var w in bases)
				if (Parts.IsRifled(Parts.CategoryOf(w.name)) && (best == null || w.Efficiency > best.Efficiency))
					best = w;
			return best;
		}

		Stats Preview(Country country, out Stats b, out List<string> capped, out List<WeaponTemplate> bases)
		{
			bases = Registry.UnlockedBases(country);
			var bw = Registry.Vanilla(_draft.Base);
			b = Stats.Of(bw);
			var donor = Donor(bases);
			_draft.Donor = _draft.AddsRifling && donor != null ? donor.name : "";
			if (_draft.AddsRifling && donor == null)
				_draft.Rifling = 'N';
			var list = new List<(string, Stats)>();
			foreach (var w in bases) list.Add((w.name, Stats.Of(w)));
			capped = new List<string>();
			return Design.Compute(_draft, b, Caps.From(list), capped);
		}

		void CreateDesign(Country country)
		{
			if (_draft == null)
				return;
			EndRename(true);
			var bases = Registry.UnlockedBases(country);
			if (bases.Find(w => w.name == _draft.Base) == null)
			{
				Say("You no longer have the base weapon unlocked.", true);
				return;
			}
			var s = Preview(country, out _, out _, out _);
			float cost = Design.DevelopmentCost(s, _markOf != null, Plugin.DevelopmentCost.Value);
			var inv = country.inventory;
			if (cost > 0f && inv.Money < cost)
			{
				Say($"Not enough money: development costs {N(cost, "#,0")}, the treasury has {N(inv.Money, "#,0")}.", true, false);
				return;
			}
			var d = _draft.CloneParts();
			d.Id = Design.NewId();
			d.Name = UniqueName(country, string.IsNullOrWhiteSpace(_draft.Name) ? "Workshop Pattern" : _draft.Name.Trim(), null);
			d.Final = s;
			var e = Registry.Create(d);
			Registry.Unlock(e, country);
			if (cost > 0f)
			{
				inv.PostMoneyOperation(EMoneyOperation.OTHER, -cost);
				try { inv.TriggerChanged(); } catch (Exception) { }
			}
			_editing = false;
			_markOf = null;
			_showArchived = false;
			Say($"{d.Name} is ready. Order it under MUSKETS in the production screen; regiments can carry it once it's in storage." +
				(cost > 0f ? $" Development cost {N(cost, "#,0")}." : ""));
		}
	}
}
