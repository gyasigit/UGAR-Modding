using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using World;
using World.SceneObject;
using MarketStoragePanel = World.UI.GeneralPage.MarketStorage.MarketStoragePanel;

namespace UGARTradePartners
{
	/// <summary>
	/// The Trade Partners window (read-only). Opened by the PARTNERS tab we add to the market screen's page tabs, or the
	/// hotkey (F6) anywhere on the campaign map; Esc or X closes. Two views: BY PARTNER (8 nations, each with its route,
	/// tension, delivery factor, warnings and goods per day) and BY GOOD (every traded good: in/out per day, stock and
	/// storage limit, buy/sell price, price multiplier, who supplies and who buys). Britain switches between the colony
	/// and England markets. The frame catches clicks so nothing reaches the map.
	/// </summary>
	public class TradePanel : MonoBehaviour
	{
		public TradePanel(IntPtr ptr) : base(ptr) { }

		const float W = 1000f, H = 660f, BodyY = 84f, FooterH = 80f;
		const int ViewPartners = 0, ViewGoods = 1, ViewHistory = 2;

		sealed class Hit
		{
			public RectTransform Rt;
			public Action Act;
			public Image Img;
			public Color Normal;
		}

		Kit _kit;
		GameObject _root;
		RectTransform _rootRt, _content;
		readonly List<Hit> _hits = new List<Hit>();
		Hit _hover;
		bool _broken;
		int _failures;

		// Market screen + our tab on it
		MarketStoragePanel _marketPanel;
		float _nextMarketSearch;
		GameObject _tab;
		IntPtr _tabOwner;
		RectTransform _tabRt;

		// State
		bool _open, _dirty = true, _england;
		int _view = ViewPartners;
		string _historyGood; // History.Key of the good shown in PRICE HISTORY
		int _range = 1;      // 30 days / 90 days / 1 year / all
		int _histPage;
		int _partner = -1, _page;
		float _nextRefresh;

		static string N(float v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);

		void OnDestroy()
		{
			try { if (_root != null) Destroy(_root); } catch (Exception) { }
			try { if (_tab != null) Destroy(_tab); } catch (Exception) { }
			Ui.DestroyTemplates();
		}

		// ---------------- Frame loop ----------------

		void Update()
		{
			if (_broken)
			{
				// Disabled after errors: the hotkey tries again (the log has the reason).
				try
				{
					if (Plugin.Enabled.Value && Input.GetKeyDown(Plugin.HotkeyCode()))
					{
						_broken = false;
						_failures = 0;
						_open = false;
						Plugin.Logger.LogInfo("Trade Partners window re-enabled by the hotkey.");
					}
				}
				catch (Exception) { }
				if (_broken) return;
			}
			try
			{
				var country = Plugin.Enabled.Value ? Trade.Player() : null;
				var market = OpenMarketPanel();
				EnsureTab(market, country != null);
				if (country == null)
				{
					_open = false;
					HideRoot();
					return;
				}
				if (Input.GetKeyDown(Plugin.HotkeyCode()))
					Toggle(country, market);
				if (_open && Input.GetKeyDown(KeyCode.Escape))
					Close();
				if (Input.GetMouseButtonDown(0))
					Click(country, market);
				if (!_open)
				{
					HideRoot();
					return;
				}
				if (!EnsureRoot(market))
					return;
				if (_dirty || Time.realtimeSinceStartup >= _nextRefresh)
				{
					_dirty = false;
					_nextRefresh = Time.realtimeSinceStartup + 2f;
					Rebuild(country);
					Place(market);
				}
				Hover();
				_failures = 0;
			}
			catch (Exception e)
			{
				if (++_failures >= 5)
				{
					_broken = true;
					Plugin.Logger.LogError($"Trade Partners window disabled after repeated errors: {e}");
					HideRoot();
				}
				else
				{
					Plugin.Logger.LogWarning($"Trade Partners window hiccup: {e}");
					_dirty = true;
					HideRoot(); // never leave a half-built window on screen
				}
			}
		}

		/// <summary>The market screen when it's open (polled twice a second; no game method is patched for this).</summary>
		MarketStoragePanel OpenMarketPanel()
		{
			try
			{
				if (_marketPanel != null && !_marketPanel.gameObject.activeInHierarchy)
					_marketPanel = null;
			}
			catch (Exception) { _marketPanel = null; }
			if (_marketPanel == null && Time.realtimeSinceStartup >= _nextMarketSearch)
			{
				_nextMarketSearch = Time.realtimeSinceStartup + 0.5f;
				try { _marketPanel = UnityEngine.Object.FindObjectOfType<MarketStoragePanel>(); }
				catch (Exception) { _marketPanel = null; }
			}
			return _marketPanel;
		}

		bool MarketIsEngland(Country country, MarketStoragePanel market)
		{
			try
			{
				var mm = market?.marketManager;
				return mm != null && Trade.HasEngland(country) && mm.Pointer == country.europeanManager.marketManager.Pointer;
			}
			catch (Exception) { return false; }
		}

		void Toggle(Country country, MarketStoragePanel market)
		{
			if (_open) { Close(); return; }
			_open = true;
			_dirty = true;
			_page = 0;
			if (market != null) _england = MarketIsEngland(country, market);
			if (_england && !Trade.HasEngland(country)) _england = false;
			Plugin.Logger.LogInfo($"Trade Partners window opened ({(_england ? "England" : "colony")} market).");
		}

		void Close()
		{
			_open = false;
			_dirty = true;
		}

		// ---------------- Root panel ----------------

		void HideRoot()
		{
			if (_root == null) return;
			try { if (_root.activeSelf) _root.SetActive(false); }
			catch (Exception) { _root = null; }
		}

		static Canvas RootCanvas(Component market)
		{
			Canvas c = null;
			try { if (market != null) c = market.GetComponentInParent<Canvas>(); } catch (Exception) { }
			if (c == null)
			{
				foreach (var o in UnityEngine.Object.FindObjectsOfType<Canvas>())
					if (o != null && o.isRootCanvas && o.renderMode == RenderMode.ScreenSpaceOverlay && (c == null || o.sortingOrder > c.sortingOrder))
						c = o;
			}
			return c?.rootCanvas;
		}

		bool EnsureRoot(MarketStoragePanel market)
		{
			if (_kit == null || (!_kit.FromMarket && market != null))
			{
				_kit = Ui.Discover(market, _kit);
				Plugin.Logger.LogInfo($"Trade Partners window look from the {_kit.Source}.");
				if (_root != null) { Destroy(_root); _root = null; }
			}
			var canvas = RootCanvas(market);
			if (canvas == null)
				return false;
			if (_root == null)
			{
				_rootRt = Ui.Node("TradePartnersPanel", canvas.transform);
				_root = _rootRt.gameObject;
				UnityEngine.Object.DontDestroyOnLoad(_root);
				var frame = _root.AddComponent<Image>();
				frame.sprite = null;
				frame.color = Ui.PanelColor;
				frame.raycastTarget = true; // catches clicks so they never reach the map
				if (_kit.Frame != null)
				{
					var art = Ui.Img(_root.transform, _kit.Frame, _kit.FrameType, Color.white, 0f, 0f, W, H);
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
			// Shrink to fit small screens (e.g. 1366x768): never larger than 96% of the canvas.
			float s = Plugin.UiScale.Value;
			try
			{
				var area = canvas.GetComponent<RectTransform>().rect;
				if (area.width > 0f && area.height > 0f)
					s = Math.Min(s, Math.Min(area.width * 0.96f / W, area.height * 0.96f / H));
			}
			catch (Exception) { }
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

		/// <summary>Right of the market screen when there's room, else centred; always kept on screen.</summary>
		void Place(MarketStoragePanel market)
		{
			Rect me = ScreenRect(_rootRt);
			float margin = 12f, x, yTop;
			Rect dock = default;
			bool docked = false;
			try
			{
				if (market != null)
				{
					dock = ScreenRect(market.GetComponent<RectTransform>());
					docked = dock.width > 0f && dock.width < Screen.width * 0.7f;
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

		// ---------------- The PARTNERS tab on the market screen ----------------

		void EnsureTab(MarketStoragePanel market, bool active)
		{
			try
			{
				if (_tab != null && (market == null || market.Pointer != _tabOwner))
				{
					if (_tab.transform == null || _tab.transform.parent == null) _tab = null;
				}
				if (market == null)
					return;
				if (_kit == null || !_kit.FromMarket)
					_kit = Ui.Discover(market, _kit);
				var group = market.buttonGroup;
				if (_tab == null && group != null && market.Pointer != _tabOwner) // one try per market screen
					BuildTab(market, group.transform);
				if (_tab != null && _tab.activeSelf != active)
					_tab.SetActive(active);
				if (_tab != null)
				{
					var label = _tab.GetComponentInChildren<TextMeshProUGUI>(true);
					if (label != null) label.color = _open ? Ui.Gold : Ui.TextColor;
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Trade Partners tab not added ({e.Message}); use the hotkey {Plugin.Hotkey.Value}.");
				_tab = null;
				_tabOwner = market != null ? market.Pointer : IntPtr.Zero; // don't retry every frame
			}
		}

		void BuildTab(MarketStoragePanel market, Transform parent)
		{
			if (_kit.TabTemplate == null)
			{
				for (int i = 0; i < parent.childCount; i++)
				{
					var c = parent.GetChild(i);
					if (c.GetComponent<Image>() != null && c.GetComponentInChildren<TextMeshProUGUI>(true) != null)
					{
						_kit.TabTemplate = Ui.Sanitize(c.gameObject, "TradePartnersTabTemplate");
						break;
					}
				}
			}
			_tabOwner = market.Pointer;
			if (_kit.TabTemplate == null)
			{
				Plugin.Logger.LogInfo($"Market screen tabs have nothing to copy; use the hotkey {Plugin.Hotkey.Value}.");
				return;
			}
			_tab = UnityEngine.Object.Instantiate(_kit.TabTemplate, parent, false);
			_tab.name = "TradePartnersTab";
			_tabRt = _tab.GetComponent<RectTransform>();
			var label = _tab.GetComponentInChildren<TextMeshProUGUI>(true);
			if (label != null) label.text = "PARTNERS";
			if (parent.GetComponent<LayoutGroup>() == null)
			{
				// Next to the last tab, in the direction the tabs run.
				float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
				RectTransform last = null;
				for (int i = 0; i < parent.childCount; i++)
				{
					var c = parent.GetChild(i).GetComponent<RectTransform>();
					if (c == null || c == _tabRt || !c.gameObject.activeSelf) continue;
					var p = c.anchoredPosition;
					minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
					if (last == null || p.x > last.anchoredPosition.x || p.y < last.anchoredPosition.y) last = c;
				}
				if (last != null)
				{
					bool horizontal = maxX - minX >= maxY - minY;
					var lp = last.anchoredPosition;
					_tabRt.anchoredPosition = horizontal
						? new Vector2(maxX + last.rect.width + 6f, lp.y)
						: new Vector2(lp.x, minY - _tabRt.rect.height - 6f);
				}
			}
			_tab.transform.SetAsLastSibling();
			_tab.SetActive(true);
			Plugin.Logger.LogInfo("Trade Partners tab added to the market screen.");
		}

		// ---------------- Clicks ----------------

		void AddHit(RectTransform rt, Action act, Image img = null)
		{
			_hits.Add(new Hit { Rt = rt, Act = act, Img = img, Normal = img != null ? img.color : Color.white });
		}

		void Click(Country country, MarketStoragePanel market)
		{
			var mouse = (Vector2)Input.mousePosition;
			try
			{
				if (_tab != null && _tab.activeInHierarchy && _tabRt != null &&
					RectTransformUtility.RectangleContainsScreenPoint(_tabRt, mouse, CamFor(_tabRt)))
				{
					bool wantEngland = MarketIsEngland(country, market);
					if (_open && wantEngland != _england) { _england = wantEngland; _dirty = true; }
					else Toggle(country, market);
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
				try { h.Act?.Invoke(); }
				catch (Exception e) { Plugin.Logger.LogError($"Trade Partners action failed: {e}"); }
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

		Image Button(Transform parent, string text, float x, float y, float w, float h, Action act, bool selected = false, float size = 14f)
		{
			var img = Ui.Img(parent, null, Image.Type.Simple, selected ? Ui.SelectedColor : Ui.ButtonColor, x, y, w, h, raycast: true);
			Ui.Border(img.transform, w, h, 1f, selected ? Ui.Gold : Ui.Bronze);
			Ui.Txt(_kit, img.transform, text, size, selected ? Ui.Gold : Ui.TextColor, 0f, 0f, w, h, TextAlignmentOptions.Center);
			AddHit(img.rectTransform, act, img);
			return img;
		}

		// ---------------- Content ----------------

		void Rebuild(Country country)
		{
			if (_content != null)
				Destroy(_content.gameObject);
			_hits.Clear();
			_hover = null;
			_content = Ui.Node("Content", _root.transform);
			Ui.Place(_content, 0f, 0f, W, H);
			var t = _content.transform;
			var k = _kit;

			bool hasEngland = Trade.HasEngland(country);
			if (!hasEngland) _england = false;
			var v = Trade.Build(country, _england);

			// Title row
			Ui.Txt(k, t, "TRADE PARTNERS", 24f, Ui.Gold, 18f, 10f, 260f, 34f, header: true);
			float bx = 280f;
			if (hasEngland)
			{
				Button(t, "COLONIES", bx, 12f, 116f, 30f, () => { _england = false; _page = 0; _histPage = 0; }, !_england);
				Button(t, "ENGLAND", bx + 122f, 12f, 116f, 30f, () => { _england = true; _page = 0; _histPage = 0; }, _england);
			}
			Button(t, "BY PARTNER", 536f, 12f, 120f, 30f, () => { _view = ViewPartners; _page = 0; }, _view == ViewPartners);
			Button(t, "BY GOOD", 662f, 12f, 100f, 30f, () => { _view = ViewGoods; _page = 0; }, _view == ViewGoods);
			Button(t, "PRICE HISTORY", 768f, 12f, 160f, 30f, () => { _view = ViewHistory; }, _view == ViewHistory);
			Button(t, "X", W - 50f, 12f, 34f, 30f, Close, false, 16f);

			// What this market is and what affects it
			string where = v.England ? "England market (buy here on the England screen)" : (hasEngland ? "Colonies market" : "Your market");
			string sea = v.Delivery < 0.999f
				? $"<color=#E6A659>sea trade arriving {N(v.Delivery * 100f, "0")}%</color> (trade ships lost last week)"
				: "sea trade arriving 100%";
			Ui.Txt(k, t, $"{where} · {sea} · your ports: {v.Ports} · per-day numbers, moved at the end of each day",
				14f, Ui.Grey, 18f, 48f, W - 36f, 24f);
			Ui.Img(t, null, Image.Type.Simple, Ui.Bronze, 16f, 76f, W - 32f, 1f);

			if (v.Error != null)
			{
				Ui.Txt(k, t, v.Error, 16f, Ui.Warn, 18f, BodyY + 10f, W - 36f, 30f);
				return;
			}
			if (_view == ViewGoods) BuildGoods(t, v);
			else if (_view == ViewHistory) BuildHistory(t, v);
			else BuildPartners(t, v);

			// Footer: price multipliers of this market (wraps to two lines), then one line of explanation
			float fy = H - FooterH;
			Ui.Img(t, null, Image.Type.Simple, Ui.Bronze, 16f, fy - 4f, W - 32f, 1f);
			string mods = v.Modifiers.Count == 0 ? "none (every good at its base price)" : string.Join(" · ", v.Modifiers.ConvertAll(m => $"{m.name} ×{N(m.mod)}"));
			Ui.Txt(k, t, $"<color=#E8C787>Price multipliers here:</color> {mods}", 13f, Ui.TextColor, 18f, fy, W - 36f, 38f, wrap: true);
			Ui.Txt(k, t, "Buy = one unit at ×1.2, sell = one unit at ×0.8; prices fall as the market's stock grows. " +
				"Sea partners deliver (1 - tension) × sea trade arriving.",
				12.5f, Ui.Grey, 18f, fy + 42f, W - 36f, 26f);
		}

		static Color TensionColor(float t) => t > 0.6f ? Ui.Bad : t > 0.3f ? Ui.Warn : Ui.Good;

		void BuildPartners(Transform t, MarketView v)
		{
			var k = _kit;
			if (_partner < 0 || _partner >= v.Partners.Count || !v.Partners[_partner].Trades)
				_partner = v.Partners.FindIndex(p => p.Trades);
			float lx = 16f, lw = 300f, rowH = 54f, y = BodyY + 4f;
			for (int i = 0; i < v.Partners.Count; i++)
			{
				var p = v.Partners[i];
				int idx = i;
				bool sel = i == _partner;
				var card = Ui.Img(t, null, Image.Type.Simple, sel ? Ui.SelectedColor : Ui.CardColor, lx, y, lw, rowH - 4f, raycast: true);
				if (sel) Ui.Border(card.transform, lw, rowH - 4f, 1f, Ui.Gold);
				var ct = card.transform;
				Ui.Txt(k, ct, p.Name, 16f, sel ? Ui.Gold : Ui.TextColor, 10f, 3f, lw - 110f, 22f);
				string route = p.Sea ? $"By sea · tension <color=#{Hex(TensionColor(p.Tension))}>{N(p.Tension * 100f, "0")}%</color> · ×{N(p.Factor)}" : "Overland · tension doesn't matter";
				Ui.Txt(k, ct, route, 13f, Ui.Grey, 10f, 25f, lw - 20f, 20f);
				string tag; Color tc;
				if (!p.Trades) { tag = "no trade here"; tc = Ui.Grey; }
				else if (p.War) { tag = "AT WAR"; tc = Ui.Bad; }
				else if (p.Sea && p.NoPorts) { tag = "NO PORTS"; tc = Ui.Bad; }
				else if (p.Sea && p.HighTension) { tag = "HIGH TENSION"; tc = Ui.Warn; }
				else if (p.Factor < 0.999f) { tag = "reduced"; tc = Ui.Warn; }
				else { tag = "trading"; tc = Ui.Good; }
				Ui.Txt(k, ct, tag, 13f, tc, lw - 110f, 3f, 100f, 22f, TextAlignmentOptions.MidlineRight);
				if (p.Trades) AddHit(card.rectTransform, () => _partner = idx, card);
				y += rowH;
			}

			float rx = 334f, rw = W - rx - 16f;
			if (_partner < 0)
			{
				Ui.Txt(k, t, "No partner trades with this market.", 16f, Ui.Grey, rx, BodyY + 8f, rw, 26f);
				return;
			}
			var sp = v.Partners[_partner];
			Ui.Txt(k, t, sp.Name, 20f, Ui.Gold, rx, BodyY, rw, 28f, header: true);
			string why = sp.Sea
				? $"Arrives by sea: amounts × (1 - tension {N(sp.Tension * 100f, "0")}%) × sea trade arriving {N(v.Delivery * 100f, "0")}% = ×{N(sp.Factor)}."
				: "Trades overland: always at full amounts.";
			if (sp.War) why += " <color=#E07059>At war with you</color> (the game warns, but the amounts follow the tension).";
			else if (sp.Sea && sp.NoPorts) why += " <color=#E07059>You hold no ports</color> (the game warns about this weekly).";
			Ui.Txt(k, t, why, 13f, Ui.Grey, rx, BodyY + 28f, rw, 20f);

			// Columns share the space right of the partner list (1.0.1 used fixed widths 80 px wider than that).
			float[] share = { 0.31f, 0.15f, 0.15f, 0.15f, 0.12f, 0.12f };
			var cw = new float[share.Length];
			for (int c = 0; c < share.Length; c++) cw[c] = (float)Math.Floor(rw * share[c]);
			string[] head = { "Good", "Brings /day", "Takes /day", "Stock", "Buy", "Sell" };
			float hy = BodyY + 54f;
			Row(t, rx, hy, cw, head, Ui.Gold, null, 14f);
			float ry = hy + 24f;
			var goods = new Dictionary<IntPtr, Good>();
			foreach (var g in v.Goods) goods[g.Asset.Pointer] = g;
			int n = 0;
			foreach (var f in sp.Flows)
			{
				goods.TryGetValue(f.Asset.Pointer, out var g);
				string full = g != null && f.Offer > 0f && g.Stock >= g.Cap ? " <color=#8F8F80>(full)</color>" : "";
				var cells = new[]
				{
					f.Name,
					f.Offer > 0f ? $"<color=#9CCC7A>+{N(f.Offer)}</color>{full}" : "",
					f.Demand > 0f ? $"<color=#E09A59>-{N(f.Demand)}</color>" : "",
					g != null ? N(g.Stock, "0.#") : "",
					g != null && g.Buy > 0f ? N(g.Buy, "0") : "",
					g != null && g.Sell > 0f ? N(g.Sell, "0") : "",
				};
				var asset = f.Asset;
				var row = Row(t, rx, ry, cw, cells, Ui.TextColor, (n++ % 2 == 0) ? Ui.RowColor : Ui.PanelColor, 14f);
				if (row != null) AddHit(row.rectTransform, () => ShowHistory(asset), row);
				ry += 22f;
				if (ry > H - FooterH - 26f) break;
			}
			if (sp.Flows.Count == 0)
				Ui.Txt(k, t, "Nothing traded with this market.", 14f, Ui.Grey, rx, ry, rw, 22f);
		}

		void BuildGoods(Transform t, MarketView v)
		{
			var k = _kit;
			float x = 16f;
			float[] cw = { 170f, 80f, 80f, 80f, 120f, 70f, 70f, 70f, 228f };
			string[] head = { "Good", "In /day", "Out /day", "Net /day", "Stock (limit)", "Buy", "Sell", "Price ×", "Supplied by / bought by" };
			float hy = BodyY + 2f;
			Row(t, x, hy, cw, head, Ui.Gold, null, 14f);
			const float rowH = 21f;
			int perPage = (int)((H - FooterH - 12f - (hy + 24f)) / rowH);
			int pages = Math.Max(1, (v.Goods.Count + perPage - 1) / perPage);
			_page = Mathf.Clamp(_page, 0, pages - 1);
			float ry = hy + 24f;
			for (int i = _page * perPage, n = 0; i < v.Goods.Count && n < perPage; i++, n++)
			{
				var g = v.Goods[i];
				string limit = g.Cap < 1e6f ? $"{N(g.Stock, "0.#")} ({N(g.Cap, "0")})" : N(g.Stock, "0.#");
				string who = (g.Suppliers.Count > 0 ? string.Join(", ", g.Suppliers) : "nobody") + " to " + (g.Buyers.Count > 0 ? string.Join(", ", g.Buyers) : "nobody");
				if (g.Suppliers.Count == 0 && g.Buyers.Count == 0) who = "<color=#8F8F80>no partner trades it</color>";
				var cells = new[]
				{
					g.Name,
					g.SimIn > 0.0005f ? $"<color=#9CCC7A>+{N(g.SimIn)}</color>" : (g.In > 0f ? "<color=#8F8F80>full</color>" : ""),
					g.SimOut > 0.0005f ? $"<color=#E09A59>-{N(g.SimOut)}</color>" : (g.Out > 0f ? "<color=#8F8F80>none left</color>" : ""),
					Math.Abs(g.Net) > 0.0005f ? (g.Net > 0 ? "+" : "") + N(g.Net) : "0",
					limit,
					g.Buy > 0f ? N(g.Buy, "0") : "",
					g.Sell > 0f ? N(g.Sell, "0") : "",
					Math.Abs(g.Modifier - 1f) > 0.001f ? $"<color=#E8C787>×{N(g.Modifier)}</color>" : "×1",
					who,
				};
				var asset = g.Asset;
				var row = Row(t, x, ry, cw, cells, Ui.TextColor, (n % 2 == 0) ? Ui.RowColor : Ui.PanelColor, 13.5f);
				if (row != null) AddHit(row.rectTransform, () => ShowHistory(asset), row);
				ry += rowH;
			}
			if (pages > 1)
			{
				float py = H - FooterH - 36f;
				Ui.Txt(k, t, $"Page {_page + 1} / {pages}", 13f, Ui.Grey, W - 290f, py, 120f, 26f, TextAlignmentOptions.MidlineRight);
				Button(t, "PREV", W - 160f, py, 70f, 26f, () => _page--, false, 13f);
				Button(t, "NEXT", W - 86f, py, 70f, 26f, () => _page++, false, 13f);
			}
		}

		/// <summary>A table row; returns its background (clickable when a hit is added) or null without one.</summary>
		Image Row(Transform t, float x, float y, float[] widths, string[] cells, Color color, Color? back, float size)
		{
			float total = 0f;
			foreach (var w in widths) total += w;
			Image bg = back.HasValue ? Ui.Img(t, null, Image.Type.Simple, back.Value, x, y, total, 21f, raycast: true) : null;
			float cx = x;
			for (int i = 0; i < widths.Length && i < cells.Length; i++)
			{
				Ui.Txt(_kit, t, cells[i], size, color, cx + 6f, y, widths[i] - 8f, 21f,
					i == 0 || i == widths.Length - 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
				cx += widths[i];
			}
			return bg;
		}

		void ShowHistory(PathAsset asset)
		{
			_historyGood = History.Key(asset);
			_view = ViewHistory;
			_histPage = -1; // page to the selected good
		}

		// ---------------- PRICE HISTORY ----------------

		static readonly int[] RangeDays = { 30, 90, 365, int.MaxValue };
		static readonly string[] RangeNames = { "30 DAYS", "90 DAYS", "1 YEAR", "ALL" };
		static readonly Color BuyColor = new Color(0.91f, 0.78f, 0.53f);
		static readonly Color SellColor = new Color(0.61f, 0.80f, 0.48f);
		static readonly Color StockColor = new Color(0.45f, 0.55f, 0.65f, 0.45f);

		void BuildHistory(Transform t, MarketView v)
		{
			var k = _kit;
			var goods = v.Goods.FindAll(g => g.Buy > 0f || g.Sell > 0f);
			if (goods.Count == 0)
			{
				Ui.Txt(k, t, "This market has no priced goods.", 16f, Ui.Grey, 18f, BodyY + 8f, W - 36f, 26f);
				return;
			}
			int sel = goods.FindIndex(g => History.Key(g.Asset) == _historyGood);
			if (sel < 0) { sel = 0; _historyGood = History.Key(goods[0].Asset); }

			// Left: the goods, paged
			float lx = 16f, lw = 220f, rowH = 25f, top = BodyY + 4f, bottom = H - FooterH - 40f;
			int perPage = Math.Max(1, (int)((bottom - top) / rowH));
			int pages = (goods.Count + perPage - 1) / perPage;
			if (_histPage < 0) _histPage = sel / perPage;
			_histPage = Mathf.Clamp(_histPage, 0, pages - 1);
			float y = top;
			for (int i = _histPage * perPage; i < goods.Count && i < (_histPage + 1) * perPage; i++)
			{
				var g = goods[i];
				bool on = i == sel;
				var card = Ui.Img(t, null, Image.Type.Simple, on ? Ui.SelectedColor : Ui.CardColor, lx, y, lw, rowH - 3f, raycast: true);
				if (on) Ui.Border(card.transform, lw, rowH - 3f, 1f, Ui.Gold);
				Ui.Txt(k, card.transform, g.Name, 14f, on ? Ui.Gold : Ui.TextColor, 8f, 0f, lw - 16f, rowH - 3f);
				var key = History.Key(g.Asset);
				AddHit(card.rectTransform, () => _historyGood = key, card);
				y += rowH;
			}
			if (pages > 1)
			{
				float py = H - FooterH - 34f;
				Button(t, "PREV", lx, py, 70f, 26f, () => _histPage = Math.Max(0, _histPage - 1), false, 13f);
				Ui.Txt(k, t, $"{_histPage + 1} / {pages}", 13f, Ui.Grey, lx + 74f, py, 72f, 26f, TextAlignmentOptions.Center);
				Button(t, "NEXT", lx + lw - 70f, py, 70f, 26f, () => _histPage++, false, 13f);
			}

			// Right: chart
			var cur = goods[sel];
			float rx = lx + lw + 18f, rw = W - rx - 16f;
			Ui.Txt(k, t, cur.Name, 20f, Ui.Gold, rx, BodyY, rw - 340f, 28f, header: true);
			for (int r = 0; r < RangeNames.Length; r++)
			{
				int rr = r;
				Button(t, RangeNames[r], W - 16f - (RangeNames.Length - r) * 84f, BodyY + 2f, 80f, 26f, () => _range = rr, _range == r, 13f);
			}
			Ui.Txt(k, t, $"Now: buy <color=#E8C787>{N(cur.Buy, "0")}</color> · sell <color=#9CCC7A>{N(cur.Sell, "0")}</color> · stock {N(cur.Stock, "0.#")}" +
				(Math.Abs(cur.Modifier - 1f) > 0.001f ? $" · price ×{N(cur.Modifier)} in this market" : ""),
				13.5f, Ui.TextColor, rx, BodyY + 30f, rw, 22f);

			// Points: recorded days plus today's live value when today isn't recorded yet
			var pts = new List<PricePoint>();
			var series = History.Series(v.Label, cur.Asset);
			if (series != null) pts.AddRange(series);
			int today = History.TodayIndex();
			if (pts.Count == 0 || pts[pts.Count - 1].Day < today)
				pts.Add(new PricePoint { Day = today, Buy = cur.Buy, Sell = cur.Sell, Stock = cur.Stock });
			int span = RangeDays[_range];
			if (span != int.MaxValue) pts.RemoveAll(p => p.Day < today - span);

			float cx = rx + 54f, cy = BodyY + 64f, cw = rw - 54f - 48f, ch = H - FooterH - 36f - cy;
			Ui.Img(t, null, Image.Type.Simple, new Color(0.08f, 0.11f, 0.14f, 0.9f), cx, cy, cw, ch);
			Ui.Border(Ui.Img(t, null, Image.Type.Simple, new Color(0, 0, 0, 0), cx, cy, cw, ch).transform, cw, ch, 1f, new Color(Ui.Bronze.r, Ui.Bronze.g, Ui.Bronze.b, 0.6f));

			if (pts.Count < 2)
			{
				var first = History.FirstDay();
				string since = first.HasValue ? $"Recording since {first.Value:d MMM yyyy}." : "Recording starts now.";
				Ui.Txt(k, t, $"{since} The game keeps no price history, so this mod records each market once per game day " +
					"(after its daily trade). Let a few days pass and the chart fills in.",
					14f, Ui.Grey, cx + 20f, cy + ch / 2f - 30f, cw - 40f, 60f, TextAlignmentOptions.Center, wrap: true);
				return;
			}

			// Keep the chart light: at most ~160 points
			if (pts.Count > 160)
			{
				var thin = new List<PricePoint>();
				float step = (pts.Count - 1) / 159f;
				for (int i = 0; i < 160; i++) thin.Add(pts[(int)Math.Round(i * step)]);
				pts = thin;
			}
			float lo = float.MaxValue, hi = float.MinValue, smax = 0f;
			foreach (var p in pts)
			{
				lo = Math.Min(lo, Math.Min(p.Buy, p.Sell));
				hi = Math.Max(hi, Math.Max(p.Buy, p.Sell));
				smax = Math.Max(smax, p.Stock);
			}
			float pad = Math.Max((hi - lo) * 0.08f, Math.Max(1f, hi * 0.02f));
			lo = Math.Max(0f, lo - pad); hi += pad;
			int d0 = pts[0].Day, d1 = Math.Max(pts[pts.Count - 1].Day, d0 + 1);
			float X(int day) => cx + 4f + (cw - 8f) * (day - d0) / (float)(d1 - d0);
			float Y(float price) => cy + 4f + (ch - 8f) * (1f - (price - lo) / (hi - lo));

			// Stock as faint bars on its own scale (right axis)
			if (smax > 0f)
			{
				float bw = Math.Max(2f, (cw - 8f) / pts.Count * 0.6f);
				foreach (var p in pts)
				{
					float bh = (ch - 8f) * 0.35f * p.Stock / smax;
					if (bh >= 0.5f) Ui.Img(t, null, Image.Type.Simple, StockColor, X(p.Day) - bw / 2f, cy + ch - 4f - bh, bw, bh);
				}
			}
			for (int i = 1; i < pts.Count; i++)
			{
				Seg(t, X(pts[i - 1].Day), Y(pts[i - 1].Buy), X(pts[i].Day), Y(pts[i].Buy), 2f, BuyColor);
				Seg(t, X(pts[i - 1].Day), Y(pts[i - 1].Sell), X(pts[i].Day), Y(pts[i].Sell), 2f, SellColor);
			}

			// Axes
			for (int i = 0; i <= 2; i++)
			{
				float val = lo + (hi - lo) * i / 2f, yy = Y(val);
				Ui.Txt(k, t, N(val, "0"), 12.5f, Ui.Grey, rx, yy - 10f, 50f, 20f, TextAlignmentOptions.MidlineRight);
				Ui.Img(t, null, Image.Type.Simple, new Color(1f, 1f, 1f, 0.06f), cx, yy, cw, 1f);
			}
			if (smax > 0f)
				Ui.Txt(k, t, $"stock\n{N(smax, "0.#")}", 12f, new Color(0.6f, 0.68f, 0.76f), cx + cw + 4f, cy + ch - (ch - 8f) * 0.35f - 18f, 46f, 34f, wrap: true);
			Ui.Txt(k, t, pts[0].Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture), 12.5f, Ui.Grey, cx, cy + ch + 2f, 160f, 20f);
			Ui.Txt(k, t, pts[pts.Count - 1].Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture), 12.5f, Ui.Grey, cx + cw - 160f, cy + ch + 2f, 160f, 20f, TextAlignmentOptions.MidlineRight);
			Ui.Txt(k, t, "<color=#E8C787>Buy</color>  ·  <color=#9CCC7A>Sell</color>  ·  <color=#7A90A6>Stock (bars)</color>", 12.5f, Ui.TextColor, cx + cw / 2f - 120f, cy + ch + 2f, 240f, 20f, TextAlignmentOptions.Center);
		}

		/// <summary>A straight line from (x1, y1) to (x2, y2), in our top-left coordinates.</summary>
		static void Seg(Transform parent, float x1, float y1, float x2, float y2, float thickness, Color c)
		{
			float dx = x2 - x1, dy = -(y2 - y1);
			float len = (float)Math.Sqrt(dx * dx + dy * dy);
			if (len < 0.5f) return;
			var rt = Ui.Node("seg", parent);
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 0.5f);
			rt.anchoredPosition = new Vector2(x1, -y1);
			rt.sizeDelta = new Vector2(len + 1f, thickness);
			rt.localEulerAngles = new Vector3(0f, 0f, (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI));
			var img = rt.gameObject.AddComponent<Image>();
			img.color = c;
			img.raycastTarget = false;
		}

		// Not ColorUtility.ToHtmlStringRGB: through the IL2CPP interop it throws IndexOutOfRangeException (1.0.0 in game).
		static string Hex(Color c) =>
			((int)Math.Round(Mathf.Clamp01(c.r) * 255f)).ToString("X2") +
			((int)Math.Round(Mathf.Clamp01(c.g) * 255f)).ToString("X2") +
			((int)Math.Round(Mathf.Clamp01(c.b) * 255f)).ToString("X2");
	}
}
