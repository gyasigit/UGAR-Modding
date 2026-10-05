using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using World;
using World.SceneObject;
using World.UI.GeneralPage.ResourcePanel;

namespace UGARRecruitGrowth
{
	/// <summary>
	/// British campaigns: a "colonial recruits" number on the campaign top bar, built from a copy of the game's own
	/// population element (icon frame, font, colours) placed right after it. It counts the recruits waiting in your
	/// settlements in America (not Great Britain's home settlements, not the America pool). Click it for the per-town list.
	///
	/// The top bar's own numbers next to it are not recruits: the population element shows national workforce in
	/// thousands (PlayerManager.Workforce, "0.#K"), the officer element the specialist resource (Country.inventory.officers).
	/// </summary>
	public class TopBar : MonoBehaviour
	{
		public TopBar(IntPtr ptr) : base(ptr) { }

		const float RefreshSeconds = 2f;

		internal static RectTransform Element;   // read by Overlay for clicks and placement
		internal static int LastTotal;

		GameObject _clone;
		TextMeshProUGUI _count;
		GeneralResourcePanel _bar;
		float _next;
		bool _broken, _loggedLayout;

		void OnDestroy() => Remove();

		// The bar's HorizontalLayoutGroup shares a fixed width between its items, so a 9th item squeezes all of them
		// (numbers ran into the next icon). We widen the bar by one slot while our item is there.
		RectTransform _widened;
		float _widenedBy;

		void Remove()
		{
			try
			{
				if (_widened != null && _widenedBy != 0f)
					_widened.sizeDelta -= new Vector2(_widenedBy, 0f);
			}
			catch (Exception) { }
			_widened = null;
			_widenedBy = 0f;
			try { if (_clone != null) Destroy(_clone); } catch (Exception) { }
			_clone = null;
			_count = null;
			Element = null;
		}

		static bool Wanted()
		{
			if (!Plugin.Enabled.Value || !Plugin.ColonialTopBar.Value)
				return false;
			var c = RecruitMath.PlayerCountry;
			return c != null && c.settings != null && c.settings.hasRemoteLocalities;
		}

		void Update()
		{
			if (_broken || Time.realtimeSinceStartup < _next)
				return;
			_next = Time.realtimeSinceStartup + RefreshSeconds;
			try
			{
				if (!Wanted())
				{
					if (_clone != null)
						Remove();
					return;
				}
				if (!Alive(_bar))
				{
					_bar = UnityEngine.Object.FindObjectOfType<GeneralResourcePanel>();
					if (!Alive(_bar))
					{
						if (_clone != null)
							Remove();
						return;
					}
				}
				if (_clone == null && !Build())
					return;
				// Hidden on the England screen, like the game's own colony-only numbers.
				bool england = false;
				try { england = RecruitMath.PlayerCountry.IsEuropeanAction; } catch (Exception) { }
				if (_clone.activeSelf == england)
					_clone.SetActive(!england);
				LastTotal = ColonialTotal(out _);
				_count.text = Format(LastTotal);
			}
			catch (Exception e)
			{
				_broken = true;
				Plugin.Logger.LogError($"Colonial recruits counter disabled after an error: {e}");
				Remove();
			}
		}

		static bool Alive(Component c)
		{
			try { return c != null && c.gameObject != null && c.gameObject.activeInHierarchy; } catch (Exception) { return false; }
		}

		internal static string Format(int n) => n >= 10000 ? (n / 1000f).ToString("0.#") + "K" : n.ToString("N0");

		/// <summary>Recruits waiting in the player's settlements outside Great Britain.</summary>
		internal static int ColonialTotal(out int settlements)
		{
			int total = 0;
			settlements = 0;
			var player = RuntimeVars.playerNation;
			foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
			{
				if (l == null || l.ownerNation != player || l.destroyed || RecruitMath.IsBritishHome(l))
					continue;
				total += Math.Max(0, l.recruits);
				settlements++;
			}
			return total;
		}

		bool Build()
		{
			var source = _bar.populationElement;
			if (source == null)
				return false;
			var src = source.gameObject;
			var parent = src.transform.parent;
			// Copy under an inactive holder so none of the game's scripts on it start; then strip them.
			var holder = new GameObject("ColonialRecruitsHolder");
			holder.SetActive(false);
			var clone = UnityEngine.Object.Instantiate(src, holder.transform, false);
			clone.name = "UGAR ColonialRecruits";
			var pe = clone.GetComponent<PopulationResourceElement>();
			var countText = pe != null ? pe.count : null;
			var icon = pe != null ? pe.icon : null;
			try { if (pe != null && pe.messageRoot != null) pe.messageRoot.gameObject.SetActive(false); } catch (Exception) { }
			foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (mb == null)
					continue;
				string n = mb.GetIl2CppType().FullName;
				if (n.StartsWith("UnityEngine.", StringComparison.Ordinal) || n.StartsWith("TMPro.", StringComparison.Ordinal))
					continue;
				UnityEngine.Object.DestroyImmediate(mb);
			}
			// The population element pops "+x" messages under a child root; drop anything that isn't the icon or count.
			if (countText == null)
				countText = clone.GetComponentInChildren<TextMeshProUGUI>(true);
			if (countText == null)
			{
				UnityEngine.Object.Destroy(holder);
				Plugin.Logger.LogWarning("Colonial recruits counter: the population element has no text to copy.");
				return false;
			}
			try
			{
				var sprite = Config.game.hud.GetGlobalResourceIcon(EGlobalResource.RECRUITS_BRIT).icon;
				if (icon != null && sprite != null)
					icon.sprite = sprite;
			}
			catch (Exception) { }

			var layout = parent != null ? parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
			var rt = clone.GetComponent<RectTransform>();
			var srcRt = src.GetComponent<RectTransform>();
			var parentRt = parent != null ? parent.GetComponent<RectTransform>() : null;
			float slot = srcRt != null ? srcRt.rect.width + (layout != null ? layout.spacing : 0f) : 0f;
			float parentWidthBefore = parentRt != null ? parentRt.rect.width : 0f;

			clone.transform.SetParent(parent, false);
			UnityEngine.Object.Destroy(holder);
			clone.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
			if (layout != null && parentRt != null && slot > 0f && parent.GetComponent<ContentSizeFitter>() == null)
			{
				parentRt.sizeDelta += new Vector2(slot, 0f);
				_widened = parentRt;
				_widenedBy = slot;
			}
			if (!_loggedLayout && layout != null && parentRt != null)
			{
				var h = layout.TryCast<HorizontalLayoutGroup>();
				Plugin.Logger.LogInfo($"Top bar layout: '{parent.name}' width {parentWidthBefore:0} -> +{_widenedBy:0} (slot {slot:0}), " +
					$"anchors {parentRt.anchorMin}-{parentRt.anchorMax}, pivot {parentRt.pivot}, spacing {layout.spacing}, " +
					$"controlWidth {layout.childControlWidth}, expandWidth {layout.childForceExpandWidth}, children {parent.childCount}.");
			}
			if (layout == null && rt != null && srcRt != null)
			{
				// No layout group: sit directly under the population element so nothing on the bar is covered.
				rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0f, -srcRt.rect.height - 2f);
			}
			if (!_loggedLayout)
			{
				_loggedLayout = true;
				Plugin.Logger.LogInfo($"Colonial recruits counter added to the top bar after '{src.name}' under '{parent?.name}' " +
					$"({(layout != null ? layout.GetIl2CppType().Name : "no layout group, placed below it")}).");
			}
			foreach (var g in clone.GetComponentsInChildren<Graphic>(true))
				g.raycastTarget = g is Image; // the frame/icon catch clicks so they don't reach the map
			_clone = clone;
			_count = countText;
			Element = rt;
			clone.SetActive(true);
			return true;
		}
	}
}
