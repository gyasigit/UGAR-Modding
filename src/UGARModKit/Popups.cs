// Game-style popups: the campaign event window (one button) and the question window (several answers).
//
// Same technique as UGAR British Events (Events.Show, Choice.cs):
// - Message: a runtime GlobalEventSettings (no effects) → new GlobalEvent(settings, nation, nation).Execute(Parliament)
//   queues it in the World.UI.Window.GlobalEvent window.
// - Question: a runtime QuestionEventSettings with one Answer per option (no game effects) → new QuestionEvent(...)
//   copies them into QuestionEvent.Answer objects; Execute queues it in World.UI.Window.QuestionEvent. The window asks
//   each answer CanBeExecuted() (greys it out when false) and calls Execute() on the chosen one. Prefixes below
//   recognise our answers by pointer and run the mod's callbacks instead. The window refuses a question whose answers
//   all fail, so keep one option that is always available.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using World;
using World.Effect;
using World.Event;
using World.SceneObject;

namespace UGAR.ModKit
{
	/// <summary>One answer of a question popup.</summary>
	public sealed class Option
	{
		/// <summary>Button text.</summary>
		public string Text;

		/// <summary>
		/// Optional: returns null when the option can be picked, or a reason (shown by the game) to grey it out.
		/// Called whenever the window refreshes.
		/// </summary>
		public Func<string> Blocked;

		/// <summary>Runs when the player picks this option.</summary>
		public Action Chosen;

		/// <summary>Makes an option.</summary>
		public Option(string text, Action chosen = null, Func<string> blocked = null)
		{
			Text = text;
			Chosen = chosen;
			Blocked = blocked;
		}

		/// <summary>An option that costs <paramref name="price"/>: greyed out when the player can't afford it, and paid
		/// before <paramref name="chosen"/> runs. The price is added to the button text.</summary>
		public static Option Paid(string text, Price price, Action chosen) =>
			new Option($"{text} ({price})",
				() =>
				{
					if (price.TryPay(Game.PlayerCountry))
						chosen?.Invoke();
					else
						ModKit.Log.LogInfo($"Popup option \"{text}\": could no longer afford {price}; nothing happened.");
				},
				() => price.CanAfford(Game.PlayerCountry, out var missing) ? null : missing);
	}

	/// <summary>Shows messages and questions in the game's own event windows.</summary>
	public static class Popups
	{
		sealed class Pending
		{
			public Option Option;
			public QuestionEvent Event;     // keeps the event alive until answered
		}

		static readonly Dictionary<IntPtr, Pending> _answers = new Dictionary<IntPtr, Pending>();
		static readonly List<UnityEngine.Object> _keepAlive = new List<UnityEngine.Object>();
		static int _counter;

		/// <summary>
		/// Shows a message in the campaign event window. <paramref name="image"/> null = a game event picture
		/// (see <see cref="Images.GameEventPicture"/>). Needs a loaded campaign.
		/// </summary>
		public static void Show(string title, string text, string button = "Very well", Sprite image = null)
		{
			var country = Game.PlayerCountry ?? throw new InvalidOperationException("Popups need a loaded campaign.");
			var s = ScriptableObject.CreateInstance<GlobalEventSettings>();
			s.name = $"UGARMods/ModKit/Message{++_counter}";
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_keepAlive.Add(s);
			s.header = title ?? "";
			s.content = text ?? "";
			s.button = string.IsNullOrEmpty(button) ? "Very well" : button;
			s.image = image ?? Images.GameEventPicture();
			s.effects = new Il2CppReferenceArray<EffectAsset>(0);
			s.showEffects = false;
			s.notification = false;
			s.hidden = false;
			s.checkWarWithPlayer = false;
			s.optionalDest = country.Nation;
			new GlobalEvent(s, country.Nation, country.Nation).Execute(EEventSource.Parliament);
		}

		/// <summary>
		/// Asks a question in the game's question window, one button per option. Keep at least one option that is
		/// never blocked: the game won't show a question whose options are all greyed out.
		/// </summary>
		public static void Ask(string title, string text, Sprite image, params Option[] options)
		{
			var country = Game.PlayerCountry ?? throw new InvalidOperationException("Popups need a loaded campaign.");
			if (options == null || options.Length == 0)
				throw new ArgumentException("Ask needs at least one option", nameof(options));

			var s = ScriptableObject.CreateInstance<QuestionEventSettings>();
			s.name = $"UGARMods/ModKit/Question{++_counter}";
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_keepAlive.Add(s);
			s.header = title ?? "";
			s.content = text ?? "";
			image ??= Images.GameEventPicture();
			s.image = image;
			s.hidden = false;
			s.checkWarWithPlayer = false;
			s.optionalDest = country.Nation;

			var arr = new Il2CppReferenceArray<QuestionEventSettings.Answer>(options.Length);
			for (int i = 0; i < options.Length; i++)
				arr[i] = new QuestionEventSettings.Answer
				{
					content = options[i].Text ?? "",
					effects = new Il2CppReferenceArray<EffectAsset>(0),
					notification = false,
				};
			s.answers = arr;

			var ev = new QuestionEvent(s, country.Nation, country.Nation);
			try { if (image != null) ev.image = image; } catch { }
			var answers = ev.answers;
			if (answers == null || answers.Length != options.Length)
				throw new InvalidOperationException("The game did not create the question's answers.");
			for (int i = 0; i < options.Length; i++)
				_answers[answers[i].Pointer] = new Pending { Option = options[i], Event = ev };
			ev.Execute(EEventSource.Parliament);
		}

		/// <summary>Same as <see cref="Ask(string, string, Sprite, Option[])"/> with the default picture.</summary>
		public static void Ask(string title, string text, params Option[] options) => Ask(title, text, null, options);

		static void Forget(QuestionEvent ev)
		{
			var remove = new List<IntPtr>();
			foreach (var kv in _answers)
				if (kv.Value.Event == ev) remove.Add(kv.Key);
			foreach (var p in remove)
				_answers.Remove(p);
		}

		[HarmonyPatch(typeof(QuestionEvent.Answer), nameof(QuestionEvent.Answer.CanBeExecuted))]
		internal static class CanBeExecutedPatch
		{
			static bool Prefix(QuestionEvent.Answer __instance, ref QuestionEvent.ExecutableResult __result)
			{
				if (__instance == null || !_answers.TryGetValue(__instance.Pointer, out var p))
					return true;
				string reason = null;
				try
				{
					reason = p.Option.Blocked?.Invoke();
				}
				catch (Exception e)
				{
					ModKit.Log.LogWarning($"Popup option \"{p.Option.Text}\": Blocked check failed: {e.Message}");
				}
				__result = new QuestionEvent.ExecutableResult(reason == null, reason ?? "");
				return false;
			}
		}

		[HarmonyPatch(typeof(QuestionEvent.Answer), nameof(QuestionEvent.Answer.Execute))]
		internal static class ExecutePatch
		{
			static bool Prefix(QuestionEvent.Answer __instance)
			{
				if (__instance == null || !_answers.TryGetValue(__instance.Pointer, out var p))
					return true;
				Forget(p.Event);
				try
				{
					p.Option.Chosen?.Invoke();
				}
				catch (Exception e)
				{
					ModKit.Log.LogError($"Popup option \"{p.Option.Text}\" failed: {e}");
				}
				return false;
			}
		}
	}
}
