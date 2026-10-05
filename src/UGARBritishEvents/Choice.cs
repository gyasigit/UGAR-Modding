// Events with a cost are shown in the game's own question window (the one events with choices use), with two answers:
// Pay (greyed out when Britain can't afford it) and Pass.
//
// How the game's question window works:
// - new World.Event.QuestionEvent(QuestionEventSettings, srs, dest) copies each settings answer's content (localized,
//   falling back to the plain string) and effects into a QuestionEvent.Answer; Execute(source) queues it in the
//   World.UI.Window.QuestionEvent singleton window.
// - The window's Init sets each answer button Interactable from IAnswer.CanBeExecuted() (ExecutableResult: value +
//   notification). CheckExecution refuses a question whose answers all fail, so Pass always succeeds.
// - SelectAnswer(i) calls IAnswer.Execute(source) on the chosen answer, then closes/advances the window.
// Our answers have no game effects; prefixes on QuestionEvent.Answer.CanBeExecuted / Execute recognise them by pointer
// and do the affordability check and the payment instead.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using World;
using World.Effect;
using World.Event;
using World.SceneObject;

namespace UGARBritishEvents
{
	internal static class Choice
	{
		sealed class Pending
		{
			public EventDefinition Def;
			public Cost Cost;
			public bool IsPay;
			public QuestionEvent Event;     // keeps the wrapper (and the event) alive until answered
		}

		static readonly Dictionary<IntPtr, Pending> _answers = new Dictionary<IntPtr, Pending>();
		static readonly List<UnityEngine.Object> _keepAlive = new List<UnityEngine.Object>();

		public static void Show(Country country, EventDefinition d, Cost cost, string effectText, Sprite image)
		{
			var s = ScriptableObject.CreateInstance<QuestionEventSettings>();
			s.name = "UGARMods/Events/" + d.Id;
			s.hideFlags = HideFlags.DontUnloadUnusedAsset;
			_keepAlive.Add(s);
			s.header = d.Title;
			s.content = d.Text + $"\n\nThe Crown asks for {cost.Describe()}." + effectText;
			s.image = image;
			s.hidden = false;
			s.checkWarWithPlayer = false;
			s.optionalDest = country.Nation;

			var pay = new QuestionEventSettings.Answer
			{
				content = $"{(string.IsNullOrEmpty(d.PayButton) ? "Pay" : d.PayButton)} ({cost.Describe()})",
				effects = new Il2CppReferenceArray<EffectAsset>(0),
				notification = false,
			};
			var pass = new QuestionEventSettings.Answer
			{
				content = string.IsNullOrEmpty(d.PassButton) ? "Pass (no cost, no effect)" : d.PassButton,
				effects = new Il2CppReferenceArray<EffectAsset>(0),
				notification = false,
			};
			var arr = new Il2CppReferenceArray<QuestionEventSettings.Answer>(2);
			arr[0] = pay;
			arr[1] = pass;
			s.answers = arr;

			var ev = new QuestionEvent(s, country.Nation, country.Nation);
			try { if (image != null) ev.image = image; } catch { }
			var answers = ev.answers;
			if (answers == null || answers.Length != 2)
				throw new InvalidOperationException("question event has no answers");
			_answers[answers[0].Pointer] = new Pending { Def = d, Cost = cost, IsPay = true, Event = ev };
			_answers[answers[1].Pointer] = new Pending { Def = d, Cost = cost, IsPay = false, Event = ev };
			ev.Execute(EEventSource.Parliament);
		}

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
				try
				{
					bool ok = true;
					string missing = "";
					if (p.IsPay)
					{
						var country = Events.PlayerCountry;
						ok = country != null && p.Cost.CanAfford(country, out missing);
					}
					__result = new QuestionEvent.ExecutableResult(ok, missing ?? "");
					return false;
				}
				catch (Exception e)
				{
					Plugin.Logger.LogWarning($"British events: affordability check failed: {e.Message}");
					return true;
				}
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
					var country = Events.PlayerCountry;
					if (country == null)
						return false;
					if (p.IsPay && p.Cost.CanAfford(country, out var missing))
					{
						p.Cost.Pay(country);
						Events.Grant(country, p.Def, $"paid {p.Cost.Describe()}");
					}
					else
					{
						Plugin.Logger.LogInfo($"British events: passed on \"{p.Def.Id}\" ({(p.IsPay ? "could no longer afford " : "declined ")}{p.Cost.Describe()}).");
					}
				}
				catch (Exception e)
				{
					Plugin.Logger.LogError($"British events: answer to \"{p.Def.Id}\" failed: {e}");
				}
				return false;
			}
		}
	}
}
