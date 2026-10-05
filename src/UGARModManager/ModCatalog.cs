using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace UGARModManager
{
	/// <summary>Optional ugar-mod.json that sits next to a mod's dll.</summary>
	public sealed class ModManifest
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Author { get; set; }
		public string Category { get; set; }
		/// <summary>Settings ("Section.Key") that only take effect after a restart.</summary>
		public List<string> RestartRequired { get; set; } = new List<string>();
		/// <summary>Settings shown only when "Show advanced settings" is on.</summary>
		public List<string> Advanced { get; set; } = new List<string>();
		/// <summary>Settings never shown in the manager.</summary>
		public List<string> Hidden { get; set; } = new List<string>();
		/// <summary>False for mods that can't be switched off from the manager.</summary>
		public bool CanDisable { get; set; } = true;
		/// <summary>False for mods that can't be reloaded while the game runs (live reload).</summary>
		public bool HotReload { get; set; } = true;
		/// <summary>File name patterns (like "*.building.json") anywhere under BepInEx\plugins that reload this mod when they change.
		/// Files in the mod's own folder always do.</summary>
		public List<string> Watch { get; set; } = new List<string>();
		/// <summary>GUIDs of mods this one links to: when one of them is reloaded, this mod is reloaded too.</summary>
		public List<string> ReloadWith { get; set; } = new List<string>();
	}

	public sealed class SettingInfo
	{
		public ConfigEntryBase Entry;
		public bool RestartRequired;
		public bool Advanced;
		public string EditBuffer;
		public string Key => Entry.Definition.Section + "." + Entry.Definition.Key;
	}

	public sealed class ModInfo
	{
		public string Guid;
		public string Name;
		public string Version;
		public string DllPath;
		public ModManifest Manifest;
		/// <summary>Running in this session.</summary>
		public bool Loaded;
		/// <summary>The dll is named *.dll, so BepInEx loads it next launch.</summary>
		public bool EnabledOnDisk;
		public bool CanDisable = true;
		/// <summary>Can be reloaded while the game runs (see HotReload).</summary>
		public bool CanReload;
		public ConfigFile Config;
		public readonly List<SettingInfo> Settings = new List<SettingInfo>();
		public bool Expanded;

		public string Description => Manifest?.Description;
		public string Category => string.IsNullOrEmpty(Manifest?.Category) ? "Other mods" : Manifest.Category;
		public bool PendingRestart => Loaded != EnabledOnDisk;
	}

	public static class ModCatalog
	{
		public const string ManifestName = "ugar-mod.json";
		public const string DisabledSuffix = ".disabled";

		static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			ReadCommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true,
		};

		public static readonly List<ModInfo> Mods = new List<ModInfo>();
		/// <summary>Settings changed this session that need a restart, as "Mod: Setting".</summary>
		public static readonly HashSet<string> RestartReasons = new HashSet<string>();

		public static void Refresh()
		{
			var expanded = new HashSet<string>(Mods.Where(m => m.Expanded).Select(m => m.Guid ?? m.DllPath));
			Mods.Clear();
			var seenDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var kv in IL2CPPChainloader.Instance.Plugins)
			{
				var info = kv.Value;
				var mod = new ModInfo
				{
					Guid = info.Metadata.GUID,
					Name = info.Metadata.Name,
					Version = info.Metadata.Version?.ToString(),
					DllPath = info.Location,
					Loaded = true,
					EnabledOnDisk = true,
					Config = (info.Instance as BasePlugin)?.Config,
				};
				if (!string.IsNullOrEmpty(mod.DllPath))
				{
					seenDlls.Add(mod.DllPath);
					// The player may have switched it off earlier this session.
					if (!File.Exists(mod.DllPath) && File.Exists(mod.DllPath + DisabledSuffix))
						mod.EnabledOnDisk = false;
				}
				mod.Manifest = ReadManifest(mod.DllPath);
				mod.CanReload = HotReload.CanReload(info, out _);
				if (mod.Guid == Plugin.Guid)
					mod.CanDisable = false;
				Mods.Add(mod);
			}

			// Mods that are switched off (or were added after the game started).
			if (Directory.Exists(Paths.PluginPath))
			{
				foreach (var file in Directory.EnumerateFiles(Paths.PluginPath, "*", SearchOption.AllDirectories))
				{
					bool disabled = file.EndsWith(".dll" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
					if (!disabled)
						continue;
					string dll = file.Substring(0, file.Length - DisabledSuffix.Length);
					if (seenDlls.Contains(dll))
						continue;
					var manifest = ReadManifest(dll);
					Mods.Add(new ModInfo
					{
						Guid = manifest?.Id,
						Name = manifest?.Name ?? Path.GetFileNameWithoutExtension(dll),
						DllPath = dll,
						Manifest = manifest,
						Loaded = false,
						EnabledOnDisk = false,
					});
				}
			}

			foreach (var mod in Mods)
			{
				if (mod.Manifest != null)
				{
					if (!string.IsNullOrEmpty(mod.Manifest.Name))
						mod.Name = mod.Manifest.Name;
					mod.CanDisable &= mod.Manifest.CanDisable;
				}
				if (string.IsNullOrEmpty(mod.DllPath))
					mod.CanDisable = false;
				mod.Expanded = expanded.Contains(mod.Guid ?? mod.DllPath);
				CollectSettings(mod);
			}

			Mods.Sort((a, b) =>
			{
				// Manager first, then by category and name.
				if (a.Guid == Plugin.Guid) return -1;
				if (b.Guid == Plugin.Guid) return 1;
				int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
				return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
			});
		}

		static void CollectSettings(ModInfo mod)
		{
			if (mod.Config == null)
				return;
			var m = mod.Manifest;
			foreach (var entry in mod.Config.Values.OrderBy(e => e.Definition.Section).ThenBy(e => e.Definition.Key))
			{
				var s = new SettingInfo { Entry = entry };
				var tags = entry.Description?.Tags ?? Array.Empty<object>();
				bool HasTag(string t) => tags.Any(x => x is string str && string.Equals(str, t, StringComparison.OrdinalIgnoreCase));
				bool Listed(List<string> list) => list != null && list.Any(k => string.Equals(k, s.Key, StringComparison.OrdinalIgnoreCase));

				if (HasTag("Hidden") || Listed(m?.Hidden))
					continue;
				s.RestartRequired = HasTag("RestartRequired") || Listed(m?.RestartRequired);
				s.Advanced = HasTag("Advanced") || Listed(m?.Advanced);
				mod.Settings.Add(s);
			}
		}

		internal static ModManifest ReadManifest(string dllPath)
		{
			if (string.IsNullOrEmpty(dllPath))
				return null;
			string dir = Path.GetDirectoryName(dllPath);
			// A manifest can be shared by the folder (ugar-mod.json) or belong to one dll (MyMod.ugar-mod.json).
			string own = Path.Combine(dir, Path.GetFileNameWithoutExtension(dllPath) + "." + ManifestName);
			string shared = Path.Combine(dir, ManifestName);
			bool inPluginRoot = string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(Paths.PluginPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
			string path = File.Exists(own) ? own : (!inPluginRoot && File.Exists(shared) ? shared : null);
			if (path == null)
				return null;
			try
			{
				return JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(path), JsonOptions);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Could not read {path}: {e.Message}");
				return null;
			}
		}

		/// <summary>Renames the mod's dll so BepInEx loads (or skips) it on the next launch.</summary>
		public static string SetEnabledOnDisk(ModInfo mod, bool enable)
		{
			if (!mod.CanDisable || mod.EnabledOnDisk == enable)
				return null;
			string on = mod.DllPath;
			string off = on + DisabledSuffix;
			try
			{
				if (enable)
					File.Move(off, on);
				else
					File.Move(on, off);
				mod.EnabledOnDisk = enable;
				Plugin.Logger.LogInfo($"{mod.Name} will be {(enable ? "loaded" : "skipped")} from the next launch ({Path.GetFileName(on)}).");
				return null;
			}
			catch (Exception e)
			{
				Plugin.Logger.LogError($"Could not {(enable ? "enable" : "disable")} {mod.Name}: {e}");
				return $"Could not {(enable ? "enable" : "disable")} {mod.Name}: {e.Message}";
			}
		}

		/// <summary>A reloaded mod has re-read all its settings.</summary>
		public static void ClearRestart(string modName) => RestartReasons.RemoveWhere(r => r.StartsWith(modName + ": "));

		public static void NoteRestart(ModInfo mod, SettingInfo s)
		{
			RestartReasons.Add($"{mod.Name}: {s.Entry.Definition.Key}");
		}
	}
}
