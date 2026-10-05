// Country-wide modifiers (World.EModifier), the same system the game's events, doctrines and buildings use.
// Entries go in Country.modifiersManager.staticList, which is saved with the campaign; durationDays 0 = permanent,
// otherwise ModifiersManager.ManualUpdate counts it down. Calculate() re-sums the array the game reads.
// Not every EModifier does something at country level: see docs/modkit.md.
using System;
using World;
using World.Modifiers;
using World.SceneObject;

namespace UGAR.ModKit
{
	/// <summary>Adds and reads country modifiers.</summary>
	public static class Modifiers
	{
		/// <summary>
		/// Adds a modifier to the country, saved with the campaign. <paramref name="modifier"/> is an EModifier name
		/// such as "BLOCK_MILITA". Percent modifiers are fractions: 0.1 = +10%. <paramref name="days"/> 0 = permanent.
		/// Returns false for an unknown modifier name.
		/// </summary>
		public static bool AddToCountry(Country country, string modifier, float value, bool percent = true, int days = 0)
		{
			if (!Enum.TryParse<EModifier>(modifier, true, out var type))
			{
				ModKit.Log.LogWarning($"Unknown modifier \"{modifier}\" (use an EModifier name).");
				return false;
			}
			AddToCountry(country, type, value, percent, days);
			return true;
		}

		/// <summary>Same as the string overload, with the enum value.</summary>
		public static void AddToCountry(Country country, EModifier modifier, float value, bool percent = true, int days = 0)
		{
			var mm = country?.modifiersManager ?? throw new InvalidOperationException("No campaign country.");
			mm.staticList.Add(new ModifierParameterData(modifier, value, Math.Max(0, days), country.Nation, true) { percent = percent });
			mm.Calculate();
		}

		/// <summary>The country's current total for a modifier (without the +1 the game adds for percent reads).</summary>
		public static float Get(Country country, EModifier modifier) =>
			country?.modifiersManager == null ? 0f : country.modifiersManager.GetModifierValue(modifier, false);
	}
}
