// Small per-country values stored inside the campaign save, so mods can remember things without their own save file.
//
// Same technique as UGAR British Events: marker entries in
// Country.modifiersManager.staticList, which Country.Write/Load save with every field. A marker is
// ModifierParameterData(type 0, effect = value, durationDays 0, nation = base + key code, visible false):
// - ModifiersManager.Calculate only sums entries whose nation is the manager's nation or -1, so markers never change
//   a modifier;
// - durationDays 0 = permanent; tooltips only read dynamicList.
// A save played without the mod keeps the markers and nothing happens.
//
// Ranges: British Events uses 0x40000000 and 0x41000000. The kit uses 0x42000000 (flags) and 0x43000000 (numbers).
using System;
using System.Collections.Generic;
using World;
using World.Modifiers;
using World.SceneObject;

namespace UGAR.ModKit
{
	/// <summary>
	/// Flags and numbers saved with the campaign, per country. Keys are strings; prefix them with your mod
	/// (<c>"mymod.offerShown"</c>). Numbers are 32-bit floats: whole numbers are exact up to 16,777,216.
	/// </summary>
	public static class SaveData
	{
		const int FlagBase = 0x42000000;
		const int NumberBase = 0x43000000;
		const int Span = 0x1000000;

		static readonly Dictionary<int, string> _seen = new Dictionary<int, string>();

		/// <summary>True if the flag is set for this country.</summary>
		public static bool HasFlag(Country country, string key) => IndexOf(country, FlagBase + Code(key)) >= 0;

		/// <summary>Sets the flag (does nothing if already set).</summary>
		public static void SetFlag(Country country, string key)
		{
			int n = FlagBase + Code(key);
			if (IndexOf(country, n) < 0)
				Add(country, n, 0f);
		}

		/// <summary>Clears the flag.</summary>
		public static void ClearFlag(Country country, string key) => RemoveAll(country, FlagBase + Code(key));

		/// <summary>The stored number, or <paramref name="fallback"/> when none is stored.</summary>
		public static float GetNumber(Country country, string key, float fallback = 0f)
		{
			var list = country?.modifiersManager?.staticList;
			int i = IndexOf(country, NumberBase + Code(key));
			return i < 0 ? fallback : list[i].effect;
		}

		/// <summary>True if a number is stored under this key.</summary>
		public static bool HasNumber(Country country, string key) => IndexOf(country, NumberBase + Code(key)) >= 0;

		/// <summary>Stores a number (replaces the old one).</summary>
		public static void SetNumber(Country country, string key, float value)
		{
			int n = NumberBase + Code(key);
			int i = IndexOf(country, n);
			if (i >= 0)
				country.modifiersManager.staticList[i].effect = value;
			else
				Add(country, n, value);
		}

		/// <summary>Adds to a stored number (starting from 0) and returns the new value.</summary>
		public static float AddNumber(Country country, string key, float delta)
		{
			float v = GetNumber(country, key) + delta;
			SetNumber(country, key, v);
			return v;
		}

		/// <summary>Removes a stored number.</summary>
		public static void RemoveNumber(Country country, string key) => RemoveAll(country, NumberBase + Code(key));

		/// <summary>The 24-bit code a key is stored under (FNV-1a of the lower-case key).</summary>
		public static int Code(string key)
		{
			if (string.IsNullOrEmpty(key))
				throw new ArgumentException("SaveData key must not be empty", nameof(key));
			uint h = 2166136261;
			foreach (char c in key.ToLowerInvariant())
			{
				h ^= c;
				h *= 16777619;
			}
			int code = (int)(h & 0xFFFFFF);
			lock (_seen)
			{
				if (_seen.TryGetValue(code, out var other) && !string.Equals(other, key, StringComparison.OrdinalIgnoreCase))
					ModKit.Log.LogWarning($"SaveData keys \"{other}\" and \"{key}\" share a code and would overwrite each other; rename one.");
				else
					_seen[code] = key;
			}
			return code;
		}

		static int IndexOf(Country country, int nation)
		{
			var list = country?.modifiersManager?.staticList;
			if (list == null)
				return -1;
			for (int i = 0; i < list.Count; i++)
			{
				var m = list[i];
				if (m != null && (int)m.nation == nation)
					return i;
			}
			return -1;
		}

		static void Add(Country country, int nation, float value)
		{
			var list = country?.modifiersManager?.staticList ?? throw new InvalidOperationException("No campaign country to save into.");
			list.Add(new ModifierParameterData((EModifier)0, value, 0, (ENation)nation, false));
		}

		static void RemoveAll(Country country, int nation)
		{
			var list = country?.modifiersManager?.staticList;
			if (list == null)
				return;
			for (int i = list.Count - 1; i >= 0; i--)
				if (list[i] != null && (int)list[i].nation == nation)
					list.RemoveAt(i);
		}
	}
}
