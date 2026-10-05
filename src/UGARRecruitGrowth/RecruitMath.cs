// How the game grows recruits (see docs/recruit-growth.md):
//
// Every day State.DailyUpdate -> State.ConstructionUpdate -> Region.DailyUpdate calls, for every locality in the region,
//   RegionLocality.CalculateRecruits(share, 1), with share = loyaltyEffectCurve(region loyalty of the owner) / localities in region.
// CalculateRecruits:
//   intake = GetAvailableRecruits(share)
//   if (intake > recruits) { if owner is the player: intake += state doctrine bonus (paid in money); recruits += intake; }
// so a settlement only refills while its stock is below one day's intake (the stock tops out just under twice the intake).
// GetAvailableRecruits(share) =
//   (int)(manpower * (region RECRUITS_LOYALTY_PERCENT + locality RECRUITS_LOYALTY_PERCENT) * difficulty.recruitsModifier
//         * RegionConfig.recruitingBasis * (1 + Leadership department effect) * share * locality RECRUITS_PERCENT
//         * ArmyConfig.bountyRecruiting(bounty / expected bounty))
//   manpower = workforce * progress of construction slot 0 (unless destroyRepaired) + slaves if the country has USE_SLAVES.
// Weekly, Region.ManualUpdate -> CalculatePopulationGrows adds workforce: pop * populationGrowthPercent * workforcePercent
//   minus the slave share.
// Britain (NationSettings.hasRemoteLocalities): the home localities in the remote region grow recruits the same way. Once a
//   day ColonialStorageManager.CheckRecruitsDelivery ships them to America if the America pool + recruits at sea is below
//   the player's cap, the home localities hold at least a fifth of a ship load, and a transport ship is free. The cargo
//   sails for remoteUnitsConfig.shipsCountDays(1) * (1 - TRANSPORTATION_TIME) days, lands in a port and is unloaded into
//   EuropeanManager.recruits, the pool British regiments draw on.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using World;
using World.ColonialStorage;
using World.SceneObject;
using World.StateDoctrine;

namespace UGARRecruitGrowth
{
	internal sealed class Factor
	{
		public string Name;
		public string Value;
		public string Note;
	}

	internal sealed class LocalityGrowth
	{
		public string Name;
		public string RegionName;
		public int Stock;
		public int Intake;          // GetAvailableRecruits, the game's own number
		public int PolicyBonus;     // state doctrine bonus, player only, if the treasury can pay
		public float Share;         // loyalty effect / localities in region
		public float Loyalty;
		public int LocalitiesInRegion;
		public int Workforce;
		public int Slaves;
		public float Progress;      // construction slot 0
		public bool UsesSlaves;
		public bool DestroyRepaired;
		public int WeeklyWorkforce;
		public int CustomWorkforce;     // part of WeeklyWorkforce from UGAR Custom Buildings
		public string[] CustomBuildings = Array.Empty<string>();
		public float Estimate;     // product of the factors below (should match Intake before rounding)
		public float Manpower, Basis, LoyaltyEffect, Difficulty, LoyaltyRecruitMod, LocalityMod, LeadershipMod, BountyMod, BountyRatio;
		public readonly List<Factor> Factors = new List<Factor>();
		public bool IsPlayer;
		public bool IsBritishHome;
		public string Error;

		public bool GrowsTomorrow => Intake > Stock;
		public int GainTomorrow => GrowsTomorrow ? Intake + PolicyBonus : 0;
	}

	internal sealed class Convoy
	{
		public int Recruits;
		public int DaysLeft;
	}

	internal sealed class BritishPipeline
	{
		public int Pool;            // EuropeanManager.recruits: what British regiments draw on in America
		public int Cap;             // EuropeanManager.maxRecruits, set by the player on the Britain screen
		public float OnWay;         // ColonialStorageManager.RecruitsOnWay
		public int HomeReady;       // ColonialStorageManager.BritainRecruitsCount
		public int HomeDaily;       // sum of tomorrow's gains in the home localities that count
		public int MaxHomeLocalities;
		public int ShipLoad;
		public int Threshold;
		public int TravelDays;
		public int ShipsFree = -1;
		public readonly List<Convoy> Convoys = new List<Convoy>();
		public readonly List<LocalityGrowth> Home = new List<LocalityGrowth>();
		public string BlockedBy;     // home locality with an open supply request (stops all shipping that day)
		public string NextShipment;
		public string NextArrival;
		public string Error;
	}

	internal static class RecruitMath
	{
		static World.Configuration.GameConfig Game => Config.game;

		public static Country PlayerCountry
		{
			get
			{
				var sm = MonoBehaviourSingleton<SceneManager>.instance;
				return sm == null ? null : sm.PlayerCountry;
			}
		}

		public static DateTime Today
		{
			get
			{
				var d = RuntimeVars.date;
				return new DateTime(d.Year, d.Month, d.Day);
			}
		}

		public static string Date(int daysFromToday) => Today.AddDays(daysFromToday).ToString("d MMM yyyy");

		static float LoyaltyOf(Region region, ENation nation)
		{
			// Same array Region.GetLoyalty reads; the game treats NaN as 0.5.
			var arr = region.loyalty;
			int i = (int)nation;
			if (arr == null || i < 0 || i >= arr.Length)
				return 0f;
			float v = arr[i];
			return float.IsNaN(v) ? 0.5f : v;
		}

		/// <summary>The share Region.DailyUpdate passes to CalculateRecruits for this locality.</summary>
		public static float DailyShare(RegionLocality l, out float loyalty, out int count)
		{
			var region = l.region;
			loyalty = 0f;
			count = 0;
			if (region == null || region.localities == null)
				return 0f;
			count = region.localities.Count;
			if (count == 0)
				return 0f;
			loyalty = LoyaltyOf(region, l.ownerNation);
			return Game.region.loyaltyEffectCurve.Evaluate(loyalty) / count;
		}

		public static int PolicyBonus(RegionLocality l, int intake)
		{
			// Only the player's localities get the state doctrine bonus. pay=false: the check is read-only.
			if (l.ownerNation != RuntimeVars.playerNation || intake <= 0)
				return 0;
			var state = l.region?.state;
			var doctrine = state?.doctrineAsset;
			if (doctrine == null)
				return 0;
			doctrine.IsEnoghtMoney(state, Doctrine.EDoctrineEffect.Recruits, intake, out float bonus, false);
			return (int)bonus;
		}

		public static LocalityGrowth Compute(RegionLocality l)
		{
			var g = new LocalityGrowth();
			try
			{
				g.Name = l.Name;
				g.RegionName = l.region?.Name;
				g.Stock = l.recruits;
				g.IsPlayer = l.ownerNation == RuntimeVars.playerNation;
				g.Share = DailyShare(l, out g.Loyalty, out g.LocalitiesInRegion);
				g.Intake = l.GetAvailableRecruits(g.Share, 1f);
				if (g.Intake > g.Stock)
					g.PolicyBonus = PolicyBonus(l, g.Intake);
				Explain(l, g);
				g.CustomWorkforce = CustomBuildings.WeeklyWorkforce(l);
				g.CustomBuildings = CustomBuildings.In(l);
				g.WeeklyWorkforce = WeeklyWorkforceGrowth(l) + g.CustomWorkforce;
			}
			catch (Exception e)
			{
				g.Error = e.Message;
			}
			return g;
		}

		static void Explain(RegionLocality l, LocalityGrowth g)
		{
			var game = Game;
			var country = ENationUtil.GetCountry(l.ownerNation);
			g.Workforce = l.workforce;
			g.Slaves = l.slaves;
			g.DestroyRepaired = l.destroyRepaired;
			g.UsesSlaves = country?.modifiersManager != null && country.modifiersManager.GetModifierValue(EModifier.USE_SLAVES, false) > 0f;
			g.Progress = 1f;
			float manpower = g.Workforce;
			if (!g.DestroyRepaired)
			{
				var slots = l.constructionSlots;
				var c = slots != null && slots.Length > 0 ? slots[0] : null;
				g.Progress = c != null ? c.Progress : 0f;
				manpower = (int)(g.Workforce * g.Progress);
			}
			if (g.UsesSlaves)
				manpower += g.Slaves;

			float regionLoyaltyPct = l.region?.customModifiers != null ? l.region.customModifiers.GetModifierValue(EModifier.RECRUITS_LOYALTY_PERCENT, false) : 0f;
			float localityLoyaltyPct = l.modifierManager != null ? l.modifierManager.GetModifierValue(EModifier.RECRUITS_LOYALTY_PERCENT, true) : 0f;
			float localityPct = l.modifierManager != null ? l.modifierManager.GetModifierValue(EModifier.RECRUITS_PERCENT, true) : 1f;
			float difficulty = game.difficultyConfig.GetDifficultySettings(RuntimeVars.difficulty).recruitsModifier;
			float basis = game.region.recruitingBasis;
			var dm = MonoBehaviourSingleton<DepartmentManager>.instance;
			float leadership = dm != null ? dm.GetEffect(EDepartmentEffect.Leadership) : 0f;
			float bountyRatio = 1f, bounty = 1f;
			if (country != null)
			{
				float expected = country.ExpectedBounty;
				bountyRatio = expected != 0f ? country.bounty / expected : 1f;
				bounty = game.army.bountyRecruiting.Evaluate(bountyRatio);
			}
			float loyaltyEffect = g.Share * Math.Max(g.LocalitiesInRegion, 1);

			g.Estimate = manpower * (regionLoyaltyPct + localityLoyaltyPct) * difficulty * basis * (1f + leadership) * g.Share * localityPct * bounty;
			g.Manpower = manpower;
			g.Basis = basis;
			g.LoyaltyEffect = loyaltyEffect;
			g.Difficulty = difficulty;
			g.LoyaltyRecruitMod = regionLoyaltyPct + localityLoyaltyPct;
			g.LocalityMod = localityPct;
			g.LeadershipMod = 1f + leadership;
			g.BountyMod = bounty;
			g.BountyRatio = bountyRatio;

			string mpNote = g.DestroyRepaired ? "workforce (rebuilt town: full workforce counts)" : $"workforce {g.Workforce:N0} x town development {g.Progress:P0}";
			if (g.UsesSlaves)
				mpNote += $" + {g.Slaves:N0} slaves";
			Add(g, "Manpower", manpower.ToString("N0"), mpNote);
			Add(g, "Region loyalty", Pct(g.Loyalty), $"x{loyaltyEffect:0.###} loyalty effect");
			Add(g, "Region split", $"/{g.LocalitiesInRegion}", "daily intake is shared between the region's settlements");
			Add(g, "Recruiting basis", basis.ToString("0.####"), "game constant (RegionConfig)");
			Add(g, "Difficulty", "x" + difficulty.ToString("0.##"), null);
			Add(g, "Loyalty recruiting modifiers", "x" + (regionLoyaltyPct + localityLoyaltyPct).ToString("0.##"),
				$"region {regionLoyaltyPct:+0.##;-0.##;0} + settlement {localityLoyaltyPct:0.##} (buildings, events)");
			Add(g, "Settlement recruits modifier", "x" + localityPct.ToString("0.##"), "buildings and events in this settlement");
			Add(g, "Leadership department", "x" + (1f + leadership).ToString("0.##"), null);
			Add(g, "Bounty", "x" + bounty.ToString("0.##"), $"bounty is {bountyRatio:P0} of the expected amount");
		}

		static void Add(LocalityGrowth g, string name, string value, string note) => g.Factors.Add(new Factor { Name = name, Value = value, Note = note });

		static string Pct(float v) => v.ToString("P0");

		/// <summary>Mirror of Region.CalculatePopulationGrows (run weekly): the workforce it adds to this locality.</summary>
		static int WeeklyWorkforceGrowth(RegionLocality l)
		{
			var rc = Game.region;
			// The game grows from the Population property (scaled by town development), not the raw field.
			float f = l.Population * rc.populationGrowthPercent;
			f *= rc.workforcePercent;
			int toSlaves = (int)(f * l.slavesPercent);
			f -= toSlaves;
			return f >= 0f ? (int)f : 0;
		}

		// ---------------- Britain ----------------

		static IntPtr RecruitAssetPtr()
		{
			// GameConfig.inventorySettings keeps one asset per EInventoryType at +0x58; CheckRecruitsDelivery reads
			// element 14 (EInventoryType.Recruit) as the recruits cargo item.
			var inv = Game.inventorySettings;
			if (inv == null)
				return IntPtr.Zero;
			IntPtr arr = Marshal.ReadIntPtr(inv.Pointer + 0x58);
			if (arr == IntPtr.Zero || Marshal.ReadInt32(arr + 0x18) <= (int)EInventoryType.Recruit)
				return IntPtr.Zero;
			return Marshal.ReadIntPtr(arr + 0x20 + 8 * (int)EInventoryType.Recruit);
		}

		public static List<RegionLocality> HomeLocalities(ColonialStorageManager csm)
		{
			var list = new List<RegionLocality>();
			var region = csm?.remoteRegion;
			if (region == null)
				return list;
			var src = region.regionVisualization != null && region.regionVisualization.localities != null
				? region.regionVisualization.localities
				: region.localities;
			if (src == null)
				return list;
			for (int i = 0; i < src.Count; i++)
				list.Add(src[i]);
			return list;
		}

		public static bool IsBritishHome(RegionLocality l)
		{
			var csm = PlayerCountry?.colonialStorageManager;
			var region = csm?.remoteRegion;
			return region != null && l.region != null && l.region.Pointer == region.Pointer;
		}

		public static BritishPipeline Britain()
		{
			var country = PlayerCountry;
			if (country == null || country.settings == null || !country.settings.hasRemoteLocalities)
				return null;
			var em = country.europeanManager;
			var csm = country.colonialStorageManager;
			if (em == null || csm == null)
				return null;
			var p = new BritishPipeline();
			try
			{
				p.Pool = em.recruits;
				p.Cap = em.maxRecruits;
				p.OnWay = csm.RecruitsOnWay;
				p.HomeReady = csm.BritainRecruitsCount();
				var mods = country.modifiersManager;
				p.MaxHomeLocalities = (int)mods.GetModifierValue(EModifier.MAX_REMOTE_LOCALITY, false);

				var home = HomeLocalities(csm);
				for (int i = 0; i < home.Count; i++)
				{
					var g = Compute(home[i]);
					g.IsBritishHome = true;
					p.Home.Add(g);
					if (i < p.MaxHomeLocalities)
					{
						p.HomeDaily += g.GainTomorrow;
						if (p.BlockedBy == null && home[i].HasUnresolvedSupplyRequest)
							p.BlockedBy = g.Name;
					}
				}

				var bcc = Game.britainCampaignConfig;
				var csc = bcc.colonialStorageConfig;
				p.ShipLoad = (int)(csc.shipSettings.capacity * csc.GetModifier(EInventoryType.Recruit));
				p.Threshold = (int)Math.Ceiling(p.ShipLoad / 5f);
				int baseDays = (int)bcc.remoteUnitsConfig.shipsCountDays.Evaluate(1f);
				p.TravelDays = (int)(baseDays * (1f - mods.GetModifierValue(EModifier.TRANSPORTATION_TIME, false)));
				try { p.ShipsFree = csm.ShipsInStorage; } catch { p.ShipsFree = -1; }

				IntPtr recruitAsset = RecruitAssetPtr();
				var fleets = csm.fleets;
				for (int i = 0; fleets != null && i < fleets.Count; i++)
				{
					var tc = fleets[i];
					int n = 0;
					var cargo = tc.cargo;
					for (int j = 0; cargo != null && j < cargo.Count; j++)
					{
						var item = cargo[j];
						if (item?.asset != null && item.asset.Pointer == recruitAsset)
							n += (int)item.count;
					}
					if (n > 0)
						p.Convoys.Add(new Convoy { Recruits = n, DaysLeft = tc.travellingDays });
				}
				p.Convoys.Sort((a, b) => a.DaysLeft.CompareTo(b.DaysLeft));

				Forecast(p);
			}
			catch (Exception e)
			{
				p.Error = e.Message;
			}
			return p;
		}

		static void Forecast(BritishPipeline p)
		{
			if (p.Convoys.Count > 0)
			{
				var c = p.Convoys[0];
				p.NextArrival = $"{c.Recruits:N0} recruits reach port in {Days(c.DaysLeft)} (about {Date(c.DaysLeft)}), then unload into the America pool.";
			}

			float room = p.Cap - (p.Pool + p.OnWay);
			if (room <= 0f)
			{
				p.NextShipment = $"Paused: America pool {p.Pool:N0} + at sea {p.OnWay:N0} has reached your cap of {p.Cap:N0}. Raise the cap on the Britain screen to keep shipping.";
				return;
			}
			if (p.BlockedBy != null)
			{
				p.NextShipment = $"Paused: {p.BlockedBy} has an open supply request; the game sends no recruits from Britain until it is filled.";
				return;
			}
			if (p.ShipsFree == 0)
			{
				p.NextShipment = "Waiting for a free transport ship.";
				return;
			}
			if (p.HomeReady < p.Threshold)
			{
				int missing = p.Threshold - p.HomeReady;
				string when = p.HomeDaily > 0 ? $"about {Days((int)Math.Ceiling(missing / (float)p.HomeDaily))}" : "never at the current rate (home settlements are not growing)";
				p.NextShipment = $"Waiting: Britain has {p.HomeReady:N0} ready, a ship sails once it has {p.Threshold:N0} (a fifth of a {p.ShipLoad:N0} load). ETA {when}.";
				return;
			}
			int load = Math.Min(p.ShipLoad, p.HomeReady);
			p.NextShipment = $"Next ship takes {load:N0} recruits at the next daily check and lands in about {Days(p.TravelDays + 1)} ({Date(p.TravelDays + 1)}).";
		}

		public static string Days(int d) => d == 1 ? "1 day" : $"{d} days";
	}
}
