// Small UGUI toolkit for the workshop window, dressed in the game's own art.
//
// The look is borrowed from the production screen at runtime (no asset files): the New Order list's framed panel
// sprite, a musket card's background, name font and stat bar sprites, the order button and the count input field,
// and one of the MUSKETS/CANNONS/SHIPS/SUPPLY filter tabs (cloned as our WORKSHOP tab). Clones are stripped of the
// game's own scripts (only UnityEngine.UI / TextMeshPro components stay), and clicks are found by hit-testing our
// rectangles, so nothing of the game's event wiring is involved. If the production screen hasn't been opened yet,
// the game tooltip's frame and font are used instead.
using System;
using System.Collections.Generic;
using Common.UI;
using Common.UI.Hint;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Filters = World.UI.GeneralPage.Production.NewOrder.Filters;
using MusketElement = World.UI.GeneralPage.Production.NewOrder.MusketElement.RootElement;
using OrderElement = World.UI.GeneralPage.Production.NewOrder.Element;

namespace UGARWeaponWorkshop
{
	internal sealed class Kit
	{
		public TMP_FontAsset Font;
		public Material FontMaterial;
		public TMP_FontAsset HeaderFont;
		public Material HeaderMaterial;
		public Color Text = new Color(0.86f, 0.82f, 0.72f);
		public Color Header = new Color(0.91f, 0.78f, 0.53f);
		public Sprite Frame;
		public Image.Type FrameType = Image.Type.Sliced;
		public Color FrameColor = new Color(0.12f, 0.13f, 0.13f, 0.97f);
		public Sprite Card;
		public Image.Type CardType = Image.Type.Sliced;
		public Color CardColor = new Color(0.17f, 0.18f, 0.17f, 1f);
		public Sprite Button;
		public Image.Type ButtonType = Image.Type.Sliced;
		public Color ButtonColor = new Color(0.30f, 0.27f, 0.21f, 1f);
		public Sprite BarBack;
		public Sprite BarFill;
		public Color BarBackColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
		public Color BarFillColor = new Color(0.72f, 0.62f, 0.40f, 1f);
		public GameObject TabTemplate;   // sanitized clone, inactive
		public GameObject InputTemplate; // sanitized clone, inactive
		public bool FromProduction;
		public string Source = "defaults";
	}

	internal static class Ui
	{
		public static readonly Color Gold = new Color(0.91f, 0.78f, 0.53f);
		public static readonly Color Grey = new Color(0.54f, 0.52f, 0.46f);
		public static readonly Color Good = new Color(0.61f, 0.80f, 0.48f);
		public static readonly Color Bad = new Color(0.88f, 0.44f, 0.35f);
		public static readonly Color Warn = new Color(0.91f, 0.65f, 0.35f);
		// Colours of the production screen: dark blue-grey panels, bronze trim, light parchment text.
		public static readonly Color PanelColor = new Color(0.106f, 0.145f, 0.188f, 0.96f);  // #1B2530
		public static readonly Color CardColor = new Color(0.16f, 0.20f, 0.24f, 0.97f);
		public static readonly Color ButtonColor = new Color(0.19f, 0.24f, 0.29f, 1f);
		public static readonly Color Bronze = new Color(0.62f, 0.49f, 0.28f, 1f);
		public static readonly Color TextColor = new Color(0.86f, 0.82f, 0.72f, 1f);  // #DBD1B8

		/// <summary>A thin border inside a w x h rectangle.</summary>
		public static void Border(Transform parent, float w, float h, float t, Color c)
		{
			Img(parent, null, Image.Type.Simple, c, 0f, 0f, w, t);
			Img(parent, null, Image.Type.Simple, c, 0f, h - t, w, t);
			Img(parent, null, Image.Type.Simple, c, 0f, 0f, t, h);
			Img(parent, null, Image.Type.Simple, c, w - t, 0f, t, h);
		}

		static GameObject _templates;

		// ---------------- Borrowing the game's look ----------------

		public static Kit Discover(Filters filters, Kit previous)
		{
			var kit = previous != null && previous.FromProduction ? previous : new Kit();
			if (kit.Font == null)
			{
				try
				{
					var hint = SingletonPrefab<TextHint>.Instance;
					if (hint != null)
					{
						var t = hint.text ?? hint.GetComponentInChildren<TextMeshProUGUI>(true);
						// The font's own default material: borrowed materials can carry a heavy outline/underlay.
						if (t != null) { kit.Font = t.font; kit.FontMaterial = null; }
						kit.Text = TextColor;
						var img = hint.GetComponent<Image>();
						if (img != null && img.sprite != null) { kit.Frame = img.sprite; kit.FrameType = img.type; kit.FrameColor = Opaque(img.color); }
						kit.Source = "tooltip";
					}
				}
				catch (Exception e) { Plugin.Logger.LogWarning($"Workshop look: tooltip not usable ({e.Message})"); }
			}
			if (kit.FromProduction || filters == null)
				return kit;
			try
			{
				// The production screen's panel, card and button images are light sprites that the game tints or layers at
				// runtime: used on their own they drew a WHITE window (1.1.2). So the frame, cards and buttons use our own
				// dark colours (plus the tooltip's dark frame sprite); only the font, header font, bars and the tab are borrowed.
				var parts = new List<string>();
				var list = filters.itemList;
				if (list != null)
				{

					MusketElement musket = null;
					var muskets = list.GetComponentsInChildren<MusketElement>(true);
					if (muskets != null && muskets.Length > 0) musket = muskets[0];
					OrderElement element = musket;
					if (element == null)
					{
						var all = list.GetComponentsInChildren<OrderElement>(true);
						if (all != null && all.Length > 0) element = all[0];
					}
					if (element != null)
					{
						var name = element.itemName;
						if (name != null) { kit.Font = name.font; kit.FontMaterial = null; parts.Add("font " + name.font?.name); }
					}
					if (musket != null && musket.efficiencyBar != null)
					{
						var bar = musket.efficiencyBar;
						if (bar.sprite != null) kit.BarFill = bar.sprite;
						kit.BarFillColor = Opaque(bar.color);
						var back = bar.transform.parent != null ? bar.transform.parent.GetComponent<Image>() : null;
						if (back != null && back.sprite != null) { kit.BarBack = back.sprite; kit.BarBackColor = back.color; }
						parts.Add($"bars {bar.sprite?.name}/{back?.sprite?.name}");
					}
				}
				// Header font: the biggest text on the production screen ("PRODUCTION").
				var panel = Patches.OpenProduction;
				if (panel != null)
				{
					TextMeshProUGUI best = null;
					foreach (var t in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
						if (t != null && (best == null || t.fontSize > best.fontSize)) best = t;
					if (best != null) { kit.HeaderFont = best.font; kit.HeaderMaterial = null; parts.Add("header " + best.font?.name); }
				}
				// A filter tab to copy for our WORKSHOP tab.
				var group = filters.buttonGroup;
				if (group != null && kit.TabTemplate == null)
				{
					var gt = group.transform;
					for (int i = 0; i < gt.childCount; i++)
					{
						var c = gt.GetChild(i);
						if (c.GetComponent<Image>() != null && c.GetComponentInChildren<TextMeshProUGUI>(true) != null)
						{
							kit.TabTemplate = Sanitize(c.gameObject, "WorkshopTabTemplate");
							parts.Add("tab");
							break;
						}
					}
				}
				kit.FromProduction = parts.Exists(p => p.StartsWith("font", StringComparison.Ordinal));
				kit.Source = $"production screen ({string.Join(", ", parts)}); frame: tooltip {kit.Frame?.name ?? "none"} on dark panel";
				Plugin.Logger.LogInfo($"Workshop window look from the {kit.Source}.");
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Workshop look: production screen parts not usable ({e.Message}); using the tooltip style.");
			}
			return kit;
		}

		static Color Opaque(Color c) => new Color(c.r, c.g, c.b, Math.Max(c.a, 0.92f));

		static Image LargestSliced(Transform root, bool includeParents)
		{
			Image best = null;
			float bestArea = 0f;
			void Consider(Image img)
			{
				if (img == null || img.sprite == null || (img.type != Image.Type.Sliced && img.type != Image.Type.Tiled)) return;
				var r = img.rectTransform.rect;
				float a = Math.Abs(r.width * r.height);
				if (a > bestArea) { best = img; bestArea = a; }
			}
			foreach (var img in root.GetComponentsInChildren<Image>(true)) Consider(img);
			if (includeParents)
				for (var p = root.parent; p != null && best == null; p = p.parent)
					Consider(p.GetComponent<Image>());
			return best;
		}

		/// <summary>An inactive copy with only UGUI/TextMeshPro components left (no game scripts, no Button/Toggle wiring).</summary>
		public static GameObject Sanitize(GameObject src, string name)
		{
			if (_templates == null)
			{
				_templates = new GameObject("WeaponWorkshopTemplates");
				_templates.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(_templates);
			}
			var clone = UnityEngine.Object.Instantiate(src, _templates.transform, false);
			clone.name = name;
			StripScripts(clone);
			return clone;
		}

		public static void StripScripts(GameObject go)
		{
			var comps = go.GetComponentsInChildren<MonoBehaviour>(true);
			foreach (var c in comps)
			{
				if (c == null) continue;
				string ns = c.GetIl2CppType().Namespace ?? "";
				string n = c.GetIl2CppType().Name;
				bool keep = ns == "TMPro" || (ns == "UnityEngine.UI" && n != "Button" && n != "Toggle" && n != "Selectable");
				if (!keep)
					UnityEngine.Object.DestroyImmediate(c);
			}
		}

		// ---------------- Building ----------------

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

		public static TextMeshProUGUI Txt(Kit kit, Transform parent, string text, float size, Color c, float x, float y, float w, float h,
			TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool header = false, bool wrap = false)
		{
			var rt = Node("txt", parent);
			Place(rt, x, y, w, h);
			var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
			var font = header && kit.HeaderFont != null ? kit.HeaderFont : kit.Font;
			var mat = header && kit.HeaderFont != null ? kit.HeaderMaterial : kit.FontMaterial;
			if (font != null) t.font = font;
			if (mat != null && font != null) t.fontSharedMaterial = mat;
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

		/// <summary>A bar like the production cards': dark track, light fill of width value × w.</summary>
		public static void Bar(Kit kit, Transform parent, float x, float y, float w, float h, float value, Color? fill = null)
		{
			Img(parent, kit.BarBack, Image.Type.Sliced, kit.BarBackColor, x, y, w, h);
			float v = Mathf.Clamp01(value);
			if (v > 0.001f)
				Img(parent, kit.BarFill, Image.Type.Sliced, fill ?? kit.BarFillColor, x, y, Math.Max(2f, w * v), h);
		}
	}
}
