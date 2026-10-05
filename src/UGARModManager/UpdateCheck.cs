// Checks once per game start whether a newer UGAR Mod Pack is released on GitHub, and lets the F8 window say so.
//
// - Installed version: BepInEx\ugar-modpack-install.json, written by the pack installer ("version"). Without it (mods
//   installed by hand) there is nothing to compare, so no check is made.
// - Latest version: the GitHub API's latest release (pre-releases and drafts are skipped by GitHub), its tag_name
//   (e.g. "v1.6.0") and html_url. One anonymous request on a background thread; failures are only logged.
// - Nothing is downloaded or installed: the window shows a button that opens the release page in the browser.
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BepInEx;

namespace UGARModManager
{
	internal static class UpdateCheck
	{
		public const string Repo = "gyasigit/UGAR-Modding";
		const string ApiUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
		public const string ReleasesUrl = "https://github.com/" + Repo + "/releases/latest";

		/// <summary>Installed pack version, or null when the pack installer wasn't used.</summary>
		public static string Installed { get; private set; }
		/// <summary>Newer released version, or null (none, not checked yet, or the check failed).</summary>
		public static volatile string Available;
		public static volatile string AvailableUrl;

		public static void Start()
		{
			Installed = ReadInstalled();
			if (!Plugin.CheckForUpdates.Value)
				return;
			if (Installed == null)
			{
				Plugin.Logger.LogInfo("Update check skipped: no ugar-modpack-install.json (pack not installed with Install.bat).");
				return;
			}
			Task.Run(Run);
		}

		static string ReadInstalled()
		{
			try
			{
				var path = Path.Combine(Paths.BepInExRootPath, "ugar-modpack-install.json");
				if (!File.Exists(path))
					return null;
				using var doc = JsonDocument.Parse(File.ReadAllText(path));
				return doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
			}
			catch
			{
				return null;
			}
		}

		static async Task Run()
		{
			try
			{
				using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
				http.DefaultRequestHeaders.UserAgent.ParseAdd("UGAR-Mod-Manager/" + Plugin.Version);
				http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
				var json = await http.GetStringAsync(ApiUrl).ConfigureAwait(false);
				using var doc = JsonDocument.Parse(json);
				var tag = doc.RootElement.GetProperty("tag_name").GetString();
				var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : ReleasesUrl;
				var latest = tag?.TrimStart('v', 'V');
				if (latest != null && Compare(latest, Installed) > 0)
				{
					AvailableUrl = url;
					Available = latest;
					Plugin.Logger.LogInfo($"Update available: UGAR Mod Pack {latest} (installed {Installed}). {url}");
				}
				else
				{
					Plugin.Logger.LogInfo($"Update check: UGAR Mod Pack {Installed} is up to date (latest release {latest}).");
				}
			}
			catch (Exception e)
			{
				Plugin.Logger.LogInfo($"Update check failed (offline?): {e.Message}");
			}
		}

		/// <summary>SemVer-style compare of "1.5.9-beta" style versions: numbers first, then a pre-release tag sorts
		/// before the plain release (1.6.0-beta &lt; 1.6.0).</summary>
		public static int Compare(string a, string b)
		{
			Split(a, out var na, out var pa);
			Split(b, out var nb, out var pb);
			for (int i = 0; i < Math.Max(na.Length, nb.Length); i++)
			{
				int x = i < na.Length ? na[i] : 0, y = i < nb.Length ? nb[i] : 0;
				if (x != y) return x.CompareTo(y);
			}
			if (pa == pb) return 0;
			if (pa == null) return 1;
			if (pb == null) return -1;
			return string.CompareOrdinal(pa, pb);
		}

		static void Split(string v, out int[] nums, out string pre)
		{
			v = (v ?? "0").Trim();
			int dash = v.IndexOf('-');
			pre = dash >= 0 ? v.Substring(dash + 1) : null;
			var parts = (dash >= 0 ? v.Substring(0, dash) : v).Split('.');
			nums = new int[parts.Length];
			for (int i = 0; i < parts.Length; i++)
				int.TryParse(parts[i], out nums[i]);
		}
	}
}
