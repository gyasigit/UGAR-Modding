// Small UGUI toolkit, the same look as the Weapon Workshop window: a solid dark blue-grey
// panel with the game tooltip's frame art and a thin bronze border, the game's own fonts, and our own hit-tested
// buttons (no game scripts or Button wiring). Fonts come from the game tooltip and, once seen, the market screen.
using System;
using Common.UI;
using Common.UI.Hint;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UGARTradePartners
{
	internal sealed class Kit
	{
		public TMP_FontAsset Font;
		public TMP_FontAsset HeaderFont;
		public Sprite Frame;
		public Image.Type FrameType = Image.Type.Sliced;
		public Color FrameColor = new Color(0.12f, 0.13f, 0.13f, 0.97f);
		public GameObject TabTemplate; // sanitized clone of a market page tab, inactive
		public bool FromMarket;
		public string Source = "defaults";
	}

	internal static class Ui
	{
		public static readonly Color Gold = new Color(0.91f, 0.78f, 0.53f);
		public static readonly Color Grey = new Color(0.58f, 0.56f, 0.50f);
		public static readonly Color Good = new Color(0.61f, 0.80f, 0.48f);
		public static readonly Color Bad = new Color(0.88f, 0.44f, 0.35f);
		public static readonly Color Warn = new Color(0.91f, 0.65f, 0.35f);
		public static readonly Color PanelColor = new Color(0.106f, 0.145f, 0.188f, 0.97f);  // #1B2530
		public static readonly Color CardColor = new Color(0.16f, 0.20f, 0.24f, 0.97f);
		public static readonly Color RowColor = new Color(0.13f, 0.17f, 0.21f, 0.9f);
		public static readonly Color ButtonColor = new Color(0.19f, 0.24f, 0.29f, 1f);
		public static readonly Color SelectedColor = new Color(0.30f, 0.27f, 0.20f, 1f);
		public static readonly Color Bronze = new Color(0.62f, 0.49f, 0.28f, 1f);
		public static readonly Color TextColor = new Color(0.86f, 0.82f, 0.72f, 1f);

		static GameObject _templates;

		public static Kit Discover(Component market, Kit previous)
		{
			var kit = previous ?? new Kit();
			if (kit.Font == null)
			{
				try
				{
					var hint = SingletonPrefab<TextHint>.Instance;
					if (hint != null)
					{
						var t = hint.text ?? hint.GetComponentInChildren<TextMeshProUGUI>(true);
						if (t != null) kit.Font = t.font;
						var img = hint.GetComponent<Image>();
						if (img != null && img.sprite != null) { kit.Frame = img.sprite; kit.FrameType = img.type; kit.FrameColor = img.color; }
						kit.Source = "tooltip";
					}
				}
				catch (Exception e) { Plugin.Logger.LogWarning($"Trade partners look: tooltip not usable ({e.Message})"); }
			}
			if (kit.FromMarket || market == null)
				return kit;
			try
			{
				TextMeshProUGUI best = null;
				foreach (var t in market.GetComponentsInChildren<TextMeshProUGUI>(true))
					if (t != null && t.font != null && (best == null || t.fontSize > best.fontSize)) best = t;
				if (best != null) kit.HeaderFont = best.font;
				kit.FromMarket = true;
				kit.Source = $"tooltip + market screen (header font {kit.HeaderFont?.name ?? "none"})";
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Trade partners look: market screen parts not usable ({e.Message}).");
			}
			return kit;
		}

		/// <summary>An inactive copy with only UGUI/TextMeshPro components left (no game scripts, no Button/Toggle wiring).</summary>
		public static GameObject Sanitize(GameObject src, string name)
		{
			if (_templates == null)
			{
				_templates = new GameObject("TradePartnersTemplates");
				_templates.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(_templates);
			}
			var clone = UnityEngine.Object.Instantiate(src, _templates.transform, false);
			clone.name = name;
			foreach (var c in clone.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (c == null) continue;
				string ns = c.GetIl2CppType().Namespace ?? "";
				string n = c.GetIl2CppType().Name;
				bool keep = ns == "TMPro" || (ns == "UnityEngine.UI" && n != "Button" && n != "Toggle" && n != "Selectable");
				if (!keep) UnityEngine.Object.DestroyImmediate(c);
			}
			return clone;
		}

		public static void DestroyTemplates()
		{
			try { if (_templates != null) UnityEngine.Object.Destroy(_templates); } catch (Exception) { }
			_templates = null;
		}

		public static RectTransform Node(string name, Transform parent)
		{
			var go = new GameObject(name);
			var rt = go.AddComponent<RectTransform>();
			rt.SetParent(parent, false);
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			return rt;
		}

		public static void Place(RectTransform rt, float x, float y, float w, float h)
		{
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = new Vector2(x, -y);
			rt.sizeDelta = new Vector2(w, h);
		}

		public static Image Img(Transform parent, Sprite s, Image.Type type, Color c, float x, float y, float w, float h, bool raycast = false)
		{
			var rt = Node("img", parent);
			Place(rt, x, y, w, h);
			var img = rt.gameObject.AddComponent<Image>();
			img.sprite = s;
			img.type = s == null ? Image.Type.Simple : type;
			img.color = c;
			img.raycastTarget = raycast;
			return img;
		}

		public static void Border(Transform parent, float w, float h, float t, Color c)
		{
			Img(parent, null, Image.Type.Simple, c, 0f, 0f, w, t);
			Img(parent, null, Image.Type.Simple, c, 0f, h - t, w, t);
			Img(parent, null, Image.Type.Simple, c, 0f, 0f, t, h);
			Img(parent, null, Image.Type.Simple, c, w - t, 0f, t, h);
		}

		public static TextMeshProUGUI Txt(Kit kit, Transform parent, string text, float size, Color c, float x, float y, float w, float h,
			TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool header = false, bool wrap = false)
		{
			var rt = Node("txt", parent);
			Place(rt, x, y, w, h);
			var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
			var font = header && kit.HeaderFont != null ? kit.HeaderFont : kit.Font;
			if (font != null) t.font = font;
			t.richText = true;
			t.fontSize = size;
			t.color = c;
			t.alignment = align;
			t.enableWordWrapping = wrap;
			t.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
			t.raycastTarget = false;
			t.text = text;
			return t;
		}
	}
}
