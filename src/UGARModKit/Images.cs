// Sprites for popups: png/jpg files from a mod's folder, or pictures borrowed from the game's own events.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using World.Event;

namespace UGAR.ModKit
{
	/// <summary>Loads pictures for popups.</summary>
	public static class Images
	{
		static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
		static readonly List<UnityEngine.Object> _keepAlive = new List<UnityEngine.Object>();

		/// <summary>
		/// A png or jpg file as a sprite (cached), or null if it can't be read. Relative paths are relative to
		/// BepInEx\plugins. The game's event pictures are about 1000 x 500.
		/// </summary>
		public static Sprite Load(string file)
		{
			if (string.IsNullOrEmpty(file))
				return null;
			if (!Path.IsPathRooted(file))
				file = Path.Combine(BepInEx.Paths.PluginPath, file);
			if (_cache.TryGetValue(file, out var cached) && cached != null)
				return cached;
			try
			{
				var tex = new Texture2D(2, 2);
				if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(file)))
				{
					ModKit.Log.LogWarning($"{file} is not a png/jpg image.");
					return null;
				}
				tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
				var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
				sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
				_keepAlive.Add(tex);
				_keepAlive.Add(sp);
				_cache[file] = sp;
				return sp;
			}
			catch (Exception e)
			{
				ModKit.Log.LogWarning($"Image {file} not loaded: {e.Message}");
				return null;
			}
		}

		/// <summary>
		/// The picture of a game event by asset name (case-insensitive), or with no name the player nation's first
		/// parliament event picture (any event picture for nations without a parliament). Null if none is loaded.
		/// UGAR British Events writes the available names to BepInEx\config\ugar.britishevents.info.txt.
		/// </summary>
		public static Sprite GameEventPicture(string assetName = null)
		{
			Sprite first = null;
			try
			{
				foreach (var ev in GameEvents())
				{
					if (ev.image == null) continue;
					if (string.IsNullOrEmpty(assetName))
						return ev.image;
					if (string.Equals(ev.name, assetName, StringComparison.OrdinalIgnoreCase))
						return ev.image;
					first ??= ev.image;
				}
			}
			catch (Exception e)
			{
				ModKit.Log.LogWarning($"Game event pictures not searched: {e.Message}");
			}
			if (!string.IsNullOrEmpty(assetName))
				ModKit.Log.LogWarning($"Game event picture \"{assetName}\" not found; using another one.");
			return first;
		}

		static IEnumerable<TimelineEventSettings> GameEvents()
		{
			var pe = Game.PlayerCountry?.settings?.parliamentEvents;
			if (pe != null)
				for (int i = 0; i < pe.Length; i++)
					if (pe[i] != null) yield return pe[i];
			foreach (var o in Resources.FindObjectsOfTypeAll<TimelineEventSettings>())
				if (o != null) yield return o;
		}
	}
}
