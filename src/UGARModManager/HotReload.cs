// Live reload: applies changed mods without restarting the game.
//
//   - A mod's dll was replaced, or one of its data files (*.json) changed: the mod is unloaded and its new dll is
//     loaded from memory. Unloading calls the plugin's Unload() override (if any), removes every Harmony patch whose
//     patch method lives in the old assembly, and destroys the old assembly's injected components (whose OnDestroy
//     removes the panels they created).
//   - A .cfg file was edited outside the game: its ConfigFile is re-read, so the running mod sees the new values.
//
// Il2CppInterop refuses to inject a second type with the same full name, so before loading, the new dll is rewritten
// with Mono.Cecil: every type that derives from an il2cpp type (MonoBehaviours and other injected classes) gets a
// namespace with a reload number ("UGARBulkRecruit" -> "UGARBulkRecruit.Reload2"), and the assembly is renamed the same
// way ("UGARBulkRecruit.Reload2"). Other types keep their names. A mod that finds another mod's assembly by name must
// accept the ".Reload<n>" suffix and take the highest number (see UGARRecruitGrowth/CustomBuildings.cs).
//
// Only mods with a ugar-mod.json can be reloaded (the manifest can opt out with "hotReload": false). The manager
// itself can't be reloaded.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Mono.Cecil;
using UnityEngine;

namespace UGARModManager
{
	public static class HotReload
	{
		/// <summary>One line per reload this session, newest last (shown in the window).</summary>
		public static readonly List<string> History = new List<string>();
		/// <summary>Reload count per plugin GUID.</summary>
		public static readonly Dictionary<string, int> ReloadCount = new Dictionary<string, int>();

		static int _generation;
		static bool _started;
		/// <summary>Reloaded assemblies by their unique name ("UGARRecruitGrowth.Reload5").</summary>
		static readonly ConcurrentDictionary<string, Assembly> Reloaded = new ConcurrentDictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
		static FileSystemWatcher _pluginWatcher, _configWatcher;
		static readonly ConcurrentQueue<string> _changes = new ConcurrentQueue<string>();
		static readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		static float _lastChange;
		/// <summary>dll path -> size and write time of the dll the running code came from.</summary>
		static readonly Dictionary<string, (long, DateTime)> _stamps = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
		/// <summary>cfg path -> text last seen, so our own saves don't trigger a reload.</summary>
		static readonly Dictionary<string, string> _cfgText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public static event Action<string> Message;

		static void Say(string msg, bool error = false)
		{
			if (error) Plugin.Logger.LogError(msg); else Plugin.Logger.LogInfo(msg);
			History.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
			if (History.Count > 30)
				History.RemoveAt(0);
			Message?.Invoke(msg);
		}

		// ---- Start-up and file watching ----

		/// <summary>Called every frame from the manager window's Update.</summary>
		public static void Tick()
		{
			if (!_started)
				Start();

			while (_changes.TryDequeue(out var path))
			{
				_pending.Add(path);
				_lastChange = Time.realtimeSinceStartup;
			}
			if (_pending.Count == 0 || Time.realtimeSinceStartup - _lastChange < Plugin.ReloadDelay.Value)
				return;

			var paths = _pending.ToList();
			_pending.Clear();
			try
			{
				HandleChanges(paths);
			}
			catch (Exception e)
			{
				Say($"Live reload failed: {e.Message}", true);
				Plugin.Logger.LogError(e);
			}
		}

		static void Start()
		{
			_started = true;
			AssemblyLoadContext.Default.Resolving += (_, name) =>
				name.Name != null && Reloaded.TryGetValue(name.Name, out var a) ? a : null;
			foreach (var info in IL2CPPChainloader.Instance.Plugins.Values)
				Remember(info.Location);
			foreach (var cfg in LoadedConfigs())
				_cfgText[cfg.ConfigFilePath] = ReadText(cfg.ConfigFilePath);

			// Leftover dlls renamed by an earlier deploy while the game was running (the running game may still lock some).
			try
			{
				foreach (var f in Directory.EnumerateFiles(Paths.PluginPath, "*.old-*", SearchOption.AllDirectories))
					try { File.Delete(f); } catch (Exception) { }
			}
			catch (Exception) { }

			try
			{
				_pluginWatcher = Watch(Paths.PluginPath, true);
				_configWatcher = Watch(Paths.ConfigPath, false);
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"Live reload can't watch the mod folders: {e.Message}. The Reload buttons still work.");
			}
		}

		static FileSystemWatcher Watch(string dir, bool subdirs)
		{
			var w = new FileSystemWatcher(dir)
			{
				IncludeSubdirectories = subdirs,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
			};
			FileSystemEventHandler onChange = (_, e) => _changes.Enqueue(e.FullPath);
			w.Changed += onChange;
			w.Created += onChange;
			w.Renamed += (_, e) => _changes.Enqueue(e.FullPath);
			w.EnableRaisingEvents = true;
			return w;
		}

		static void Remember(string dll)
		{
			if (string.IsNullOrEmpty(dll) || !File.Exists(dll))
				return;
			var fi = new FileInfo(dll);
			_stamps[dll] = (fi.Length, fi.LastWriteTimeUtc);
		}

		static bool DllChanged(string dll)
		{
			if (string.IsNullOrEmpty(dll) || !File.Exists(dll))
				return false;
			var fi = new FileInfo(dll);
			return !_stamps.TryGetValue(dll, out var s) || s != (fi.Length, fi.LastWriteTimeUtc);
		}

		static IEnumerable<ConfigFile> LoadedConfigs() =>
			IL2CPPChainloader.Instance.Plugins.Values.Select(i => (i.Instance as BasePlugin)?.Config).Where(c => c != null);

		static string ReadText(string path)
		{
			try
			{
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				using var r = new StreamReader(fs);
				return r.ReadToEnd();
			}
			catch (Exception)
			{
				return null;
			}
		}

		static void HandleChanges(List<string> paths)
		{
			var toReload = new List<PluginInfo>();
			bool manifests = false;

			foreach (var path in paths)
			{
				string name = Path.GetFileName(path);
				if (name.Contains(".old-") || name.EndsWith(ModCatalog.DisabledSuffix, StringComparison.OrdinalIgnoreCase))
					continue;

				if (path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
				{
					if (Plugin.WatchConfigFiles.Value)
						ReloadConfig(path);
					continue;
				}
				if (!Plugin.WatchModFiles.Value)
					continue;

				if (name.EndsWith(ModCatalog.ManifestName, StringComparison.OrdinalIgnoreCase))
				{
					manifests = true;
					continue;
				}
				if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				{
					var info = IL2CPPChainloader.Instance.Plugins.Values.FirstOrDefault(i => string.Equals(i.Location, path, StringComparison.OrdinalIgnoreCase));
					if (info != null && DllChanged(path) && CanReload(info, out _))
						toReload.Add(info);
					continue;
				}
				if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
					toReload.AddRange(OwnersOfDataFile(path));
			}

			foreach (var info in toReload.Distinct())
				Reload(info, "files changed");
			if (manifests || toReload.Count > 0)
				ModCatalog.Refresh();
		}

		/// <summary>Mods that read this data file: by their manifest's "watch" patterns, or because it's in their folder.</summary>
		static IEnumerable<PluginInfo> OwnersOfDataFile(string path)
		{
			string name = Path.GetFileName(path);
			foreach (var info in IL2CPPChainloader.Instance.Plugins.Values)
			{
				if (!CanReload(info, out var manifest))
					continue;
				bool match = manifest.Watch != null && manifest.Watch.Any(p => Glob(p).IsMatch(name));
				if (!match)
				{
					string dir = Path.GetDirectoryName(info.Location);
					bool inPluginRoot = string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(Paths.PluginPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
					match = !inPluginRoot && path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
				}
				if (match)
					yield return info;
			}
		}

		static Regex Glob(string pattern) =>
			new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);

		static void ReloadConfig(string path)
		{
			var cfg = LoadedConfigs().FirstOrDefault(c => string.Equals(c.ConfigFilePath, path, StringComparison.OrdinalIgnoreCase));
			if (cfg == null)
				return;
			string text = ReadText(path);
			if (text == null || (_cfgText.TryGetValue(path, out var seen) && seen == text))
				return;
			try
			{
				cfg.Reload();
				_cfgText[path] = ReadText(path);
				Say($"Settings re-read from {Path.GetFileName(path)}.");
			}
			catch (Exception e)
			{
				// Probably still being written; try again on the next change.
				Plugin.Logger.LogWarning($"Could not re-read {path}: {e.Message}");
			}
		}

		// ---- Reloading a mod ----

		public static bool CanReload(PluginInfo info, out ModManifest manifest)
		{
			manifest = null;
			if (info == null || info.Metadata.GUID == Plugin.Guid || string.IsNullOrEmpty(info.Location) || !(info.Instance is BasePlugin))
				return false;
			manifest = ModCatalog.ReadManifest(info.Location);
			return manifest != null && manifest.HotReload;
		}

		public static bool Reload(string guid, string reason)
		{
			if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(guid, out var info))
				return false;
			bool ok = Reload(info, reason);
			ModCatalog.Refresh();
			return ok;
		}

		static bool Reload(PluginInfo info, string reason, HashSet<string> done = null)
		{
			done ??= new HashSet<string>();
			if (!done.Add(info.Metadata.GUID))
				return true;
			if (!CanReload(info, out _))
			{
				Say($"{info.Metadata.Name} can't be reloaded while the game runs; restart the game to apply its changes.", true);
				return false;
			}
			if (!File.Exists(info.Location))
			{
				Say($"{info.Metadata.Name}: {Path.GetFileName(info.Location)} is missing, nothing to reload.", true);
				return false;
			}

			var sw = Stopwatch.StartNew();
			var old = (BasePlugin)info.Instance;
			int gen = ++_generation;
			Assembly asm;
			string typeName;
			try
			{
				byte[] bytes = Prepare(File.ReadAllBytes(info.Location), info.Metadata.GUID, gen, Paths.BepInExAssemblyDirectory, Path.Combine(Paths.BepInExRootPath, "interop"), Paths.PluginPath, out typeName);
				var alc = new ReloadContext($"UGAR live reload {info.Metadata.GUID} #{gen}");
				using var ms = new MemoryStream(bytes);
				asm = alc.LoadFromStream(ms);
				Reloaded[asm.GetName().Name] = asm;
			}
			catch (Exception e)
			{
				// Nothing has been unloaded yet: the old version keeps running.
				Say($"{info.Metadata.Name}: the new dll could not be loaded ({e.Message}). The old version keeps running.", true);
				Plugin.Logger.LogError(e);
				return false;
			}

			Unload(old, info.Metadata.Name);

			try
			{
				var type = asm.GetType(typeName, true);
				var plugin = (BasePlugin)Activator.CreateInstance(type);
				SetProperty(info, nameof(PluginInfo.Instance), plugin);
				SetProperty(info, nameof(PluginInfo.TypeName), typeName);
				plugin.Load();
			}
			catch (Exception e)
			{
				var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
				Say($"{info.Metadata.Name}: the new version failed to start ({inner.Message}). Fix it and save again, or restart the game.", true);
				Plugin.Logger.LogError(inner);
				return false;
			}

			Remember(info.Location);
			var cfg = (info.Instance as BasePlugin)?.Config;
			if (cfg != null)
				_cfgText[cfg.ConfigFilePath] = ReadText(cfg.ConfigFilePath);
			ReloadCount[info.Metadata.GUID] = ReloadCount.TryGetValue(info.Metadata.GUID, out var n) ? n + 1 : 1;
			ModCatalog.ClearRestart(ModCatalog.ReadManifest(info.Location)?.Name ?? info.Metadata.Name);
			string ver = SafeTypes(asm).Select(t => t.GetCustomAttribute<BepInPlugin>()).FirstOrDefault(a => a != null)?.Version?.ToString();
			Say($"Reloaded {info.Metadata.Name}{(ver != null ? " " + ver : "")} ({reason}, {sw.ElapsedMilliseconds} ms).");

			// Mods that link to this one ("reloadWith" in their manifest) pick up the new version too.
			foreach (var other in IL2CPPChainloader.Instance.Plugins.Values.ToList())
			{
				if (!CanReload(other, out var m) || m.ReloadWith == null)
					continue;
				if (m.ReloadWith.Any(g => string.Equals(g, info.Metadata.GUID, StringComparison.OrdinalIgnoreCase)))
					Reload(other, "linked to " + info.Metadata.Name, done);
			}
			return true;
		}

		static void SetProperty(object target, string name, object value) =>
			target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

		/// <summary>Takes the old version of a mod out of the game.</summary>
		static void Unload(BasePlugin old, string name)
		{
			var oldAsm = old.GetType().Assembly;

			try
			{
				old.Unload();
			}
			catch (Exception e)
			{
				Plugin.Logger.LogWarning($"{name}: its Unload() threw: {e.Message}");
			}

			// Harmony patches whose patch method is in the old assembly.
			int unpatched = 0;
			foreach (var original in Harmony.GetAllPatchedMethods().ToList())
			{
				var patches = Harmony.GetPatchInfo(original);
				if (patches == null)
					continue;
				foreach (var p in patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers))
				{
					if (p.PatchMethod?.DeclaringType?.Assembly != oldAsm)
						continue;
					try
					{
						new Harmony(p.owner).Unpatch(original, p.PatchMethod);
						unpatched++;
					}
					catch (Exception e)
					{
						Plugin.Logger.LogWarning($"{name}: could not remove patch {p.PatchMethod.Name} on {original.Name}: {e.Message}");
					}
				}
			}

			// Handlers the old version subscribed to other mods' static events (UGAR ModKit's Game.NewDay and so on).
			int handlers = 0;
			var hosts = IL2CPPChainloader.Instance.Plugins.Values.Select(i => i.Instance?.GetType().Assembly)
				.Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("UGARModKit", StringComparison.Ordinal)))
				.Where(a => a != null && a != oldAsm).Distinct().ToList();
			foreach (var host in hosts)
			{
				foreach (var t in SafeTypes(host))
				{
					if (t.ContainsGenericParameters)
						continue;
					foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
					{
						if (f.IsLiteral || f.IsInitOnly || !typeof(Delegate).IsAssignableFrom(f.FieldType))
							continue;
						try
						{
							var d = (Delegate)f.GetValue(null);
							if (d == null)
								continue;
							var keep = d.GetInvocationList().Where(x => x.Method?.DeclaringType?.Assembly != oldAsm).ToArray();
							if (keep.Length == d.GetInvocationList().Length)
								continue;
							handlers += d.GetInvocationList().Length - keep.Length;
							f.SetValue(null, Delegate.Combine(keep));
						}
						catch (Exception) { }
					}
				}
			}
			if (handlers > 0)
				Plugin.Logger.LogInfo($"{name}: removed {handlers} event handler(s) it had added to other mods.");

			// Injected components of the old assembly. Their OnDestroy should remove anything else they created (our mods
			// destroy their DontDestroyOnLoad panels there); objects nobody owns can't be told apart from the game's own.
			var injected = new HashSet<string>(SafeTypes(oldAsm).Where(t => typeof(Il2CppObjectBase).IsAssignableFrom(t)).Select(t => t.Namespace + "." + t.Name));
			int destroyed = 0;
			if (injected.Count > 0)
			{
				foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<MonoBehaviour>()))
				{
					try
					{
						var il2Type = obj.GetIl2CppType();
						if (!injected.Contains(il2Type.Namespace + "." + il2Type.Name))
							continue;
						UnityEngine.Object.Destroy(obj);
						destroyed++;
					}
					catch (Exception) { }
				}
			}
			Plugin.Logger.LogInfo($"{name}: old version unloaded ({unpatched} patches removed, {destroyed} components destroyed).");
		}

		static IEnumerable<Type> SafeTypes(Assembly asm)
		{
			try { return asm.GetTypes(); }
			catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
		}

		/// <summary>Resolves the reloaded dll's references to the assemblies the game already has loaded.</summary>
		sealed class ReloadContext : AssemblyLoadContext
		{
			public ReloadContext(string name) : base(name) { }

			protected override Assembly Load(AssemblyName name)
			{
				Assembly found = null;
				foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
				{
					if (!string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
						continue;
					// Prefer the original (default context) copy; reloaded copies are only used by name lookups.
					if (found == null || GetLoadContext(a) == Default)
						found = a;
				}
				return found; // null: the runtime falls back to the default context (BepInEx's resolvers).
			}
		}

		// ---- Preparing the new dll ----

		/// <summary>
		/// Gives the injected types of the new dll a fresh namespace and returns the rewritten dll, plus the full name of
		/// the plugin class with this GUID.
		/// </summary>
		internal static byte[] Prepare(byte[] dll, string guid, int gen, string coreDir, string interopDir, string pluginDir, out string pluginType)
		{
			var resolver = new DefaultAssemblyResolver();
			resolver.AddSearchDirectory(coreDir);
			resolver.AddSearchDirectory(interopDir);
			resolver.AddSearchDirectory(pluginDir);
			using var module = ModuleDefinition.ReadModule(new MemoryStream(dll), new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });

			pluginType = null;
			foreach (var t in module.Types)
			{
				var attr = t.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == typeof(BepInPlugin).FullName);
				if (attr != null && attr.ConstructorArguments.Count > 0 && (attr.ConstructorArguments[0].Value as string) == guid)
					pluginType = t.FullName;
			}
			if (pluginType == null)
				throw new InvalidOperationException($"no [BepInPlugin(\"{guid}\")] class in the new dll");

			var injected = module.Types.Where(t => IsInjected(t, module, interopDir) || t.NestedTypes.Any(n => IsInjected(n, module, interopDir))).ToList();

			// typeof(OwnType) in attributes is stored by name: point those at the definitions before renaming.
			FixAttributes(module, module);
			FixAttributes(module.Assembly, module);
			foreach (var t in module.GetTypes())
			{
				FixAttributes(t, module);
				foreach (var m in t.Methods) { FixAttributes(m, module); foreach (var p in m.Parameters) FixAttributes(p, module); }
				foreach (var f in t.Fields) FixAttributes(f, module);
				foreach (var p in t.Properties) FixAttributes(p, module);
				foreach (var e in t.Events) FixAttributes(e, module);
			}

			foreach (var t in injected)
			{
				string ns = string.IsNullOrEmpty(t.Namespace) ? "UGARLiveReload" : t.Namespace;
				t.Namespace = $"{ns}.Reload{gen}";
			}

			// A unique assembly name too. Il2CppInterop's trampolines refer to parameter types (like an injected class's
			// nested enum) by assembly name; under the old name they bind to the first copy, which lacks the renamed types
			// ("Could not load type 'Mode'"). The Default context's Resolving hook maps the new name back to this copy.
			module.Assembly.Name.Name = $"{module.Assembly.Name.Name}.Reload{gen}";

			var outMs = new MemoryStream();
			module.Write(outMs);
			return outMs.ToArray();
		}

		/// <summary>True for classes derived from an il2cpp (interop) type, which Il2CppInterop injects by full name.</summary>
		static bool IsInjected(TypeDefinition t, ModuleDefinition module, string interopDir)
		{
			for (var b = t.BaseType; b != null;)
			{
				if (b.Scope == module || b is TypeDefinition)
				{
					var def = b as TypeDefinition ?? module.GetType(b.FullName);
					if (def == null)
						return false;
					b = def.BaseType;
					continue;
				}
				string asmName = b.Scope?.Name ?? "";
				return asmName.StartsWith("Il2CppInterop") || File.Exists(Path.Combine(interopDir, asmName + ".dll"));
			}
			return false;
		}

		static void FixAttributes(Mono.Cecil.ICustomAttributeProvider provider, ModuleDefinition module)
		{
			if (!provider.HasCustomAttributes)
				return;
			foreach (var ca in provider.CustomAttributes)
			{
				for (int i = 0; i < ca.ConstructorArguments.Count; i++)
					ca.ConstructorArguments[i] = FixArgument(ca.ConstructorArguments[i], module);
				for (int i = 0; i < ca.Fields.Count; i++)
					ca.Fields[i] = new Mono.Cecil.CustomAttributeNamedArgument(ca.Fields[i].Name, FixArgument(ca.Fields[i].Argument, module));
				for (int i = 0; i < ca.Properties.Count; i++)
					ca.Properties[i] = new Mono.Cecil.CustomAttributeNamedArgument(ca.Properties[i].Name, FixArgument(ca.Properties[i].Argument, module));
			}
		}

		static CustomAttributeArgument FixArgument(CustomAttributeArgument a, ModuleDefinition module) =>
			new CustomAttributeArgument(a.Type, FixValue(a.Value, module));

		static object FixValue(object v, ModuleDefinition module)
		{
			switch (v)
			{
				case TypeDefinition _:
					return v;
				case TypeReference tr when tr.Scope == module || tr.Scope?.Name == module.Assembly.Name.Name:
					return module.GetType(tr.FullName) ?? (object)tr;
				case CustomAttributeArgument inner:
					return FixArgument(inner, module);
				case CustomAttributeArgument[] arr:
					return arr.Select(x => FixArgument(x, module)).ToArray();
				default:
					return v;
			}
		}
	}
}
