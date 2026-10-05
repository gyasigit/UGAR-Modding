// Campaign state and the two events most mods need: a campaign was loaded, and in-game days passed.
// Day detection is the same as UGAR British Events: RuntimeVars.date is compared each frame; a new player Country
// object means a campaign was (re)loaded, which includes coming back from a battle.
using System;
using System.Collections.Generic;
using World;
using World.SceneObject;

namespace UGAR.ModKit
{
	/// <summary>The running campaign: player country, date, settlements, and campaign/day events.</summary>
	public static class Game
	{
		static IntPtr _country;
		static DateTime _lastDay;

		/// <summary>The player's country, or null outside the campaign map.</summary>
		public static Country PlayerCountry
		{
			get
			{
				var sm = MonoBehaviourSingleton<SceneManager>.instance;
				return sm == null ? null : sm.PlayerCountry;
			}
		}

		/// <summary>True while a campaign is loaded (the player country exists).</summary>
		public static bool InCampaign => PlayerCountry != null;

		/// <summary>The player's nation.</summary>
		public static ENation PlayerNation => RuntimeVars.playerNation;

		/// <summary>Today's in-game date (time of day dropped). Only meaningful in a campaign.</summary>
		public static DateTime Today
		{
			get
			{
				var d = RuntimeVars.date;
				return new DateTime(d.Year, d.Month, d.Day);
			}
		}

		/// <summary>
		/// Raised once per campaign load, on the first frame the player country exists (also after returning from a
		/// battle). Handlers get the player country.
		/// </summary>
		public static event Action<Country> CampaignLoaded;

		/// <summary>
		/// Raised when the in-game date moves forward: (player country, today, days passed since the last call).
		/// Not raised for jumps over 60 days (a loaded save, not time passing).
		/// </summary>
		public static event Action<Country, DateTime, int> NewDay;

		/// <summary>Settlements (towns, forts, ports, villages) owned by <paramref name="owner"/>, or all when null.</summary>
		public static List<RegionLocality> Settlements(ENation? owner = null)
		{
			var list = new List<RegionLocality>();
			foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
				if (l != null && (owner == null || l.ownerNation == owner.Value))
					list.Add(l);
			return list;
		}

		/// <summary>The settlement with this name (town or fort name, case-insensitive), or null.</summary>
		public static RegionLocality FindSettlement(string name)
		{
			foreach (var l in UnityEngine.Object.FindObjectsOfType<RegionLocality>())
			{
				if (l == null) continue;
				if (Same(Safe(() => l.Name), name) || Same(Safe(() => l.townName), name) || Same(Safe(() => l.fortName), name))
					return l;
			}
			return null;
		}

		static bool Same(string a, string b) => a != null && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

		static string Safe(Func<string> f)
		{
			try { return f(); } catch { return null; }
		}

		internal static void Tick()
		{
			var country = PlayerCountry;
			if (country == null || country.modifiersManager == null)
			{
				_country = IntPtr.Zero;
				return;
			}
			DateTime today;
			try { today = Today; }
			catch { return; }

			if (country.Pointer != _country)
			{
				_country = country.Pointer;
				_lastDay = today;
				Raise(CampaignLoaded, h => h(country), "CampaignLoaded");
				return;
			}
			if (today <= _lastDay)
			{
				if (today < _lastDay) _lastDay = today;
				return;
			}
			int days = (int)(today - _lastDay).TotalDays;
			_lastDay = today;
			if (days > 60)
				return;
			Raise(NewDay, h => h(country, today, days), "NewDay");
		}

		/// <summary>Calls each handler on its own so one failing mod doesn't stop the others.</summary>
		static void Raise<T>(T handlers, Action<T> call, string name) where T : Delegate
		{
			if (handlers == null)
				return;
			foreach (var d in handlers.GetInvocationList())
			{
				try
				{
					call((T)d);
				}
				catch (Exception e)
				{
					ModKit.Log.LogError($"{name} handler from {d.Method.DeclaringType?.Assembly.GetName().Name} failed: {e}");
				}
			}
		}
	}
}
