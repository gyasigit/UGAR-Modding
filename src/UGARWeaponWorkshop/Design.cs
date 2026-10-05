// A weapon design: a vanilla base weapon plus four parts, and the final numbers worked out when it was designed.
//
// The whole design is stored in the weapon's asset path ("UGARMods/Weapons/<code>"). Saves keep weapons by path
// (company weapon, storage, production orders and the research list), so a save carries its own designs and loads
// them back through the Resources.Load hook without any side file. The stored numbers make loading independent of
// what the player has unlocked at that moment; only the range curve is rebuilt from the vanilla assets.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UGARWeaponWorkshop
{
	internal enum Category { Musket, Carbine, Rifle, BreechLoader }

	internal static class Parts
	{
		// Barrel: S short, M standard, L long. Rifling: N smoothbore, R rifled. Bayonet: N none, K socket, W sword.
		// Quality: 1 rough, 2 standard, 3 fine, 4 masterwork.
		public static readonly char[] Barrels = { 'S', 'M', 'L' };
		public static readonly char[] Riflings = { 'N', 'R' };
		public static readonly char[] Bayonets = { 'N', 'K', 'W' };
		public static readonly char[] Qualities = { '1', '2', '3', '4' };

		public static string BarrelName(char c) => c == 'S' ? "Short" : c == 'L' ? "Long" : "Standard";
		public static string RiflingName(char c) => c == 'R' ? "Rifled" : "Smoothbore";
		public static string BayonetName(char c) => c == 'N' ? "None" : c == 'W' ? "Sword" : "Socket";
		public static string QualityName(char c) => c == '1' ? "Rough" : c == '3' ? "Fine" : c == '4' ? "Masterwork" : "Standard";

		// ---- Balance numbers (documented in docs/weapon-workshop.md) ----
		// Barrel
		public const float ShortReload = 0.88f, ShortRange = 0.90f, ShortFar = 0.90f, ShortIron = 0.85f, ShortCost = 0.95f;
		public const float LongReload = 1.06f, LongRange = 1.08f, LongFar = 1.06f, LongIron = 1.15f, LongCost = 1.08f;
		public const float FarFrom = 0.6f; // the barrel changes the range curve from 60% of effective range outwards
		// Rifling added to a smoothbore
		public const float RifledRange = 1.20f, RifledAccuracy = 0.15f, RifledReload = 1.70f, RifledMelee = 0.85f, RifledCost = 1.5f;
		public const float RifledWork = 2.2f; // cutting the grooves: factory work only (rifles take 1-5 work vs 0.1-0.4 for muskets)
		// Bayonet
		public const float NoBayonetMelee = 0.55f, NoBayonetReload = 0.97f, NoBayonetCost = 0.95f;
		public const float SwordMelee = 1.30f, SwordReload = 1.03f, SwordIron = 0.004f, SwordCost = 1.08f;
		// Quality: accuracy (randLow, randHi), reload, cost
		public static readonly float[] QualityLow = { -0.05f, 0f, 0.04f, 0.08f };
		public static readonly float[] QualityHigh = { -0.04f, 0f, 0.03f, 0.06f };
		public static readonly float[] QualityReload = { 1.04f, 1f, 0.97f, 0.94f };
		public static readonly float[] QualityCost = { 0.80f, 1f, 1.30f, 1.75f };
		// Caps: best unlocked value plus this margin
		public const float CapRange = 1.05f, CapReload = 0.95f, CapAccuracy = 0.03f, CapMelee = 1.10f;
		// Absolute limits whatever is unlocked
		public const float MinReload = 25f, MaxRange = 700f, MaxHigh = 1.35f;
		// Cost: performance exponents and limits of the cost factor
		public const float EffExp = 1.2f, ReloadExp = 0.6f, MeleeExp = 0.25f, MinCostFactor = 0.5f, MaxCostFactor = 4f;
		// One-time development cost: (DevBase + DevPerPrice x price) x config multiplier; Mk II pays MkDiscount of it
		public const float DevBase = 400f, DevPerPrice = 30f, MkDiscount = 0.75f;

		public static Category CategoryOf(string baseName)
		{
			if (baseName == "Rifle_Ferguson") return Category.BreechLoader;
			if (baseName.StartsWith("Rifle", StringComparison.Ordinal)) return Category.Rifle;
			if (baseName.StartsWith("Carbine", StringComparison.Ordinal) || baseName.Contains("Short")) return Category.Carbine;
			return Category.Musket;
		}

		public static bool IsRifled(Category c) => c == Category.Rifle || c == Category.BreechLoader;

		public static string CategoryName(Category c) => c switch
		{
			Category.Carbine => "carbine",
			Category.Rifle => "rifle",
			Category.BreechLoader => "breech-loader",
			_ => "musket",
		};
	}

	/// <summary>The numbers of a weapon the workshop changes.</summary>
	internal sealed class Stats
	{
		public float Range, Reload, Damage, Melee, Low, High, Price, Work, Gold, Iron, Wood;

		public float Efficiency => (Low + High) * 0.5f * Damage * Range / 100f;

		public Stats Clone() => (Stats)MemberwiseClone();

#if !BALANCE_TEST
		public static Stats Of(WeaponTemplate w)
		{
			var s = new Stats
			{
				Range = w.effectiveRange,
				Reload = w.baseReload,
				Damage = w.damage,
				Melee = w.meleeDamage,
				Low = w.randLow,
				High = w.randHi,
				Price = w.price,
				Work = w.productionCost,
				Gold = w.productionGoldCost,
			};
			var comps = w.components;
			for (int i = 0; comps != null && i < comps.Length; i++)
			{
				var c = comps[i];
				var a = c?.asset;
				if (a == null) continue;
				if (a.name == "Iron") s.Iron = c.count;
				else if (a.name == "Wood") s.Wood = c.count;
			}
			return s;
		}
#endif
	}

	/// <summary>Best values among the weapons the player can build on.</summary>
	internal sealed class Caps
	{
		public float Range = float.MaxValue, Reload = 0f, Low = float.MaxValue, High = float.MaxValue, Melee = float.MaxValue;
		public float MinReloadAny = 0f; // including the breech-loader, used when it is the base

		public static Caps From(IEnumerable<(string name, Stats s)> unlocked)
		{
			float range = 0, low = 0, high = 0, melee = 0, reload = float.MaxValue, reloadAny = float.MaxValue;
			bool any = false;
			foreach (var (name, s) in unlocked)
			{
				any = true;
				range = Math.Max(range, s.Range);
				low = Math.Max(low, s.Low);
				high = Math.Max(high, s.High);
				melee = Math.Max(melee, s.Melee);
				reloadAny = Math.Min(reloadAny, s.Reload);
				if (Parts.CategoryOf(name) != Category.BreechLoader)
					reload = Math.Min(reload, s.Reload);
			}
			if (!any)
				return new Caps();
			if (reload == float.MaxValue) reload = reloadAny;
			return new Caps
			{
				Range = range * Parts.CapRange,
				Reload = reload * Parts.CapReload,
				MinReloadAny = reloadAny * Parts.CapReload,
				Low = low + Parts.CapAccuracy,
				High = high + Parts.CapAccuracy,
				Melee = melee * Parts.CapMelee,
			};
		}
	}

	internal sealed class Design
	{
		public const int FormatVersion = 1;

		public string Id;            // unique, stable
		public string Name;
		public string Base;          // vanilla weapon asset name, e.g. Musket_BrownBess_1778
		public char Barrel = 'M', Rifling = 'N', Bayonet = 'K', Quality = '2';
		public string Donor = "";    // rifle whose range curve a rifled smoothbore takes
		public Stats Final;          // numbers after parts, caps and costs

		public Category Category => Parts.CategoryOf(Base);
		public bool AddsRifling => Rifling == 'R' && !Parts.IsRifled(Category);
		public string PartsCode => new string(new[] { Barrel, Rifling, Bayonet, Quality });

		public Design CloneParts() => new Design
		{
			Base = Base, Barrel = Barrel, Rifling = Rifling, Bayonet = Bayonet, Quality = Quality, Donor = Donor, Name = Name,
		};

		public string PartsText()
		{
			var parts = new List<string> { $"{Parts.BarrelName(Barrel)} barrel" };
			if (AddsRifling) parts.Add("rifled");
			parts.Add(Bayonet == 'N' ? "no bayonet" : $"{Parts.BayonetName(Bayonet).ToLowerInvariant()} bayonet");
			parts.Add($"{Parts.QualityName(Quality).ToLowerInvariant()} quality");
			return string.Join(", ", parts);
		}

		// ---------------- Stats ----------------

		/// <summary>Applies the parts to the base stats, then the caps; returns the final numbers and what was capped.</summary>
		public static Stats Compute(Design d, Stats b, Caps caps, List<string> capped)
		{
			var s = b.Clone();
			int q = Math.Max(0, Array.IndexOf(Parts.Qualities, d.Quality));
			float costParts = Parts.QualityCost[q];
			float iron = b.Iron;

			switch (d.Barrel)
			{
				case 'S': s.Reload *= Parts.ShortReload; s.Range *= Parts.ShortRange; iron *= Parts.ShortIron; costParts *= Parts.ShortCost; break;
				case 'L': s.Reload *= Parts.LongReload; s.Range *= Parts.LongRange; iron *= Parts.LongIron; costParts *= Parts.LongCost; break;
			}
			if (d.AddsRifling)
			{
				s.Range *= Parts.RifledRange;
				s.Low += Parts.RifledAccuracy;
				s.High += Parts.RifledAccuracy;
				s.Reload *= Parts.RifledReload;
				s.Melee *= Parts.RifledMelee;
				costParts *= Parts.RifledCost;
			}
			switch (d.Bayonet)
			{
				case 'N': s.Melee *= Parts.NoBayonetMelee; s.Reload *= Parts.NoBayonetReload; costParts *= Parts.NoBayonetCost; break;
				case 'W': s.Melee *= Parts.SwordMelee; s.Reload *= Parts.SwordReload; iron += Parts.SwordIron; costParts *= Parts.SwordCost; break;
			}
			s.Low += Parts.QualityLow[q];
			s.High += Parts.QualityHigh[q];
			s.Reload *= Parts.QualityReload[q];

			// Caps: the best unlocked weapon (plus a small margin), then absolute limits.
			if (caps != null)
			{
				float minReload = d.Category == Category.BreechLoader ? caps.MinReloadAny : caps.Reload;
				if (s.Range > caps.Range) { s.Range = caps.Range; capped?.Add($"range (best unlocked {caps.Range / Parts.CapRange:0})"); }
				if (s.Reload < minReload) { s.Reload = minReload; capped?.Add($"reload (fastest unlocked {minReload / Parts.CapReload:0.#})"); }
				if (s.Low > caps.Low) { s.Low = caps.Low; capped?.Add("accuracy, low roll"); }
				if (s.High > caps.High) { s.High = caps.High; capped?.Add("accuracy, high roll"); }
				if (s.Melee > caps.Melee) { s.Melee = caps.Melee; capped?.Add("melee"); }
			}
			s.Reload = Math.Max(s.Reload, Parts.MinReload);
			s.Range = Math.Min(s.Range, Parts.MaxRange);
			s.High = Math.Min(s.High, Parts.MaxHigh);
			s.Low = Math.Min(s.Low, s.High - 0.05f);

			// Cost: what the parts cost to make, times how much better the gun performs than its base.
			double perf = Math.Pow(s.Efficiency / Math.Max(0.01f, b.Efficiency), Parts.EffExp)
				* Math.Pow(b.Reload / Math.Max(1f, s.Reload), Parts.ReloadExp)
				* Math.Pow(Math.Max(0.001f, s.Melee) / Math.Max(0.001f, b.Melee), Parts.MeleeExp);
			float cf = (float)Math.Clamp(costParts * perf, Parts.MinCostFactor, Parts.MaxCostFactor);
			s.Price = Math.Max(3f, (float)Math.Round(b.Price * cf));
			s.Work = Round(b.Work * cf * (d.AddsRifling ? Parts.RifledWork : 1f), 3);
			s.Gold = Round(b.Gold * cf, 2);
			s.Iron = Round(iron * (0.85f + 0.15f * cf), 4);
			s.Wood = b.Wood;

			s.Range = Round(s.Range, 0);
			s.Reload = Round(s.Reload, 1);
			s.Low = Round(s.Low, 3);
			s.High = Round(s.High, 3);
			s.Melee = Round(s.Melee, 4);
			return s;
		}

		static float Round(float v, int digits) => (float)Math.Round(v, digits);

		public static float DevelopmentCost(Stats s, bool markTwo, float multiplier)
		{
			float c = (Parts.DevBase + Parts.DevPerPrice * s.Price) * multiplier * (markTwo ? Parts.MkDiscount : 1f);
			return Math.Max(0f, (float)Math.Round(c / 50f) * 50f);
		}

		// ---------------- Path code ----------------

		static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

		public string Encode()
		{
			var s = Final;
			var sb = new StringBuilder();
			sb.Append("v=").Append(FormatVersion);
			sb.Append(";id=").Append(Id);
			sb.Append(";b=").Append(Base);
			sb.Append(";p=").Append(PartsCode);
			if (!string.IsNullOrEmpty(Donor)) sb.Append(";cv=").Append(Donor);
			sb.Append(";rg=").Append(F(s.Range));
			sb.Append(";rl=").Append(F(s.Reload));
			sb.Append(";lo=").Append(F(s.Low));
			sb.Append(";hi=").Append(F(s.High));
			sb.Append(";ml=").Append(F(s.Melee));
			sb.Append(";pr=").Append(F(s.Price));
			sb.Append(";wk=").Append(F(s.Work));
			sb.Append(";gd=").Append(F(s.Gold));
			sb.Append(";fe=").Append(F(s.Iron));
			sb.Append(";n=").Append(Uri.EscapeDataString(Name ?? ""));
			return sb.ToString();
		}

		/// <summary>Reads a path code; null if it isn't one of ours or is unreadable.</summary>
		public static Design Decode(string code, out string error)
		{
			error = null;
			try
			{
				var kv = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (var part in code.Split(';'))
				{
					int eq = part.IndexOf('=');
					if (eq > 0) kv[part.Substring(0, eq)] = part.Substring(eq + 1);
				}
				float G(string k) => kv.TryGetValue(k, out var v) ? float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture) : 0f;
				if (!kv.TryGetValue("id", out var id) || !kv.TryGetValue("b", out var b))
				{
					error = "missing id or base";
					return null;
				}
				var p = kv.TryGetValue("p", out var pc) && pc.Length == 4 ? pc : "MNK2";
				return new Design
				{
					Id = id,
					Base = b,
					Barrel = p[0], Rifling = p[1], Bayonet = p[2], Quality = p[3],
					Donor = kv.TryGetValue("cv", out var cv) ? cv : "",
					Name = kv.TryGetValue("n", out var n) ? Uri.UnescapeDataString(n) : id,
					Final = new Stats
					{
						Range = G("rg"), Reload = G("rl"), Low = G("lo"), High = G("hi"), Melee = G("ml"),
						Price = G("pr"), Work = G("wk"), Gold = G("gd"), Iron = G("fe"),
					},
				};
			}
			catch (Exception e)
			{
				error = e.Message;
				return null;
			}
		}

		public static string NewId()
		{
			// Seconds since 2020 in base 36 plus 3 random characters: short, unique enough, sortable by creation.
			long secs = (long)(DateTime.UtcNow - new DateTime(2020, 1, 1)).TotalSeconds;
			const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
			var sb = new StringBuilder();
			for (long v = secs; v > 0; v /= 36) sb.Insert(0, digits[(int)(v % 36)]);
			var rng = new Random();
			for (int i = 0; i < 3; i++) sb.Append(digits[rng.Next(36)]);
			return sb.ToString();
		}

		/// <summary>"Pattern 2 Rifle" → "Pattern 2 Rifle Mk II"; "X Mk II" → "X Mk III".</summary>
		public static string NextMark(string name)
		{
			string[] marks = { "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
			int at = name.LastIndexOf(" Mk ", StringComparison.Ordinal);
			if (at >= 0)
			{
				string cur = name.Substring(at + 4);
				int i = Array.IndexOf(marks, cur);
				if (i >= 0 && i + 1 < marks.Length)
					return name.Substring(0, at) + " Mk " + marks[i + 1];
				return name.Substring(0, at) + " Mk " + cur + "+";
			}
			return name + " Mk II";
		}
	}
}
