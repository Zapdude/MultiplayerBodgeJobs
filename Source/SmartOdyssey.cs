using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sarg.smartodyssey")]
internal sealed class SmartOdyssey
{
	internal sealed class Snapshot : IExposable
	{
		internal float Mutator = 1f;

		internal float Landmark = 1f;

		internal List<string> DefNames = new List<string>();

		internal List<float> Chances = new List<float>();

		internal bool Valid
		{
			get
			{
				float mutator = Mutator;
				if (mutator >= 0f && mutator <= 20f)
				{
					mutator = Landmark;
					if (mutator >= 0f && mutator <= 20f && DefNames != null && Chances != null && DefNames.Count == Chances.Count && DefNames.All((string name) => !string.IsNullOrEmpty(name)) && DefNames.Distinct(StringComparer.Ordinal).Count() == DefNames.Count)
					{
						return Chances.All((float value) => value >= 0f && value <= 1f);
					}
				}
				return false;
			}
		}

		internal bool SameAs(Snapshot other)
		{
			if (Mutator == other.Mutator && Landmark == other.Landmark && DefNames.SequenceEqual(other.DefNames))
			{
				return Chances.SequenceEqual(other.Chances);
			}
			return false;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref Mutator, "mutatorMultiplier", 1f);
			Scribe_Values.Look(ref Landmark, "landmarkMultiplier", 1f);
			Scribe_Collections.Look(ref DefNames, "defNames", LookMode.Value);
			Scribe_Collections.Look(ref Chances, "chances", LookMode.Value);
		}
	}

	private sealed record SettingsEdit(Game Game, int Epoch, bool Enabled, Snapshot Original, Snapshot Effective);

	private const string Key = "sarg.smartodyssey.settings";

	private static FieldInfo settings;

	private static FieldInfo mutator;

	private static FieldInfo landmark;

	private static FieldInfo commonalities;

	private static int sessionEpoch;

	internal static bool Installed { get; private set; }

	public SmartOdyssey(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("SmartOdyssey.SmartOdyssey_Mod");
		Type type2 = AccessTools.TypeByName("SmartOdyssey.SmartOdyssey_Settings");
		settings = AccessTools.DeclaredField(type, "settings");
		mutator = AccessTools.DeclaredField(type2, "mutatorMultiplier");
		landmark = AccessTools.DeclaredField(type2, "landmarkMutatorMultiplier");
		commonalities = AccessTools.DeclaredField(type2, "mutatorCommonalities");
		if (settings?.FieldType != type2 || mutator?.FieldType != typeof(float) || landmark?.FieldType != typeof(float) || commonalities?.FieldType != typeof(Dictionary<string, float>))
		{
			throw new InvalidOperationException("Smart Odyssey settings changed");
		}
		SessionServices.Install();
		SessionServices.RegisterSettings("sarg.smartodyssey.settings", typeof(Snapshot), Capture, () => new Snapshot(), (IExposable state) =>
		{
			Apply((Snapshot)state);
		}, (IExposable state) => state is Snapshot snapshot && snapshot.Valid, () => (ModSettings)settings.GetValue(null), "mpSmartOdysseySettings");
		SessionServices.RegisterLifecycle("sarg.smartodyssey.settings.ui", null, () =>
		{
			sessionEpoch++;
		});
		MP.RegisterSyncMethod(typeof(SmartOdyssey), "SyncSettings").SetHostOnly().SetVersion(35);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoSettingsWindowContents"), new HarmonyMethod(typeof(SmartOdyssey), "BeforeSettings"), null, null, new HarmonyMethod(typeof(SmartOdyssey), "AfterSettings"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, "ExposeData"), null, new HarmonyMethod(typeof(SmartOdyssey), "EnsureDictionaryAfterLoad"));
		LongEventHandler.ExecuteWhenFinished(() =>
		{
			EnsureDictionary((ModSettings)settings.GetValue(null));
		});
		Installed = true;
	}

	private static Snapshot Capture()
	{
		return Capture(forEditing: false);
	}

	private static void EnsureDictionaryAfterLoad(ModSettings __instance)
	{
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			EnsureDictionary(__instance);
		}
	}

	private static void EnsureDictionary(ModSettings authorSettings)
	{
		if (authorSettings != null && commonalities.GetValue(authorSettings) == null)
		{
			commonalities.SetValue(authorSettings, new Dictionary<string, float>(StringComparer.Ordinal));
		}
	}

	private static Snapshot Capture(bool forEditing)
	{
		Snapshot snapshot = new Snapshot
		{
			Mutator = (float)mutator.GetValue(null),
			Landmark = (float)landmark.GetValue(null)
		};
		if (commonalities.GetValue(settings.GetValue(null)) is Dictionary<string, float> source)
		{
			foreach (var (text2, num2) in source.OrderBy((KeyValuePair<string, float> pair) => pair.Key, StringComparer.Ordinal))
			{
				if (forEditing)
				{
					TileMutatorDef namedSilentFail = DefDatabase<TileMutatorDef>.GetNamedSilentFail(text2);
					if (namedSilentFail == null || namedSilentFail.chanceOnNonLandmarkTile == num2)
					{
						continue;
					}
				}
				snapshot.DefNames.Add(text2);
				snapshot.Chances.Add(num2);
			}
		}
		return snapshot;
	}

	private static void Apply(Snapshot state)
	{
		Dictionary<string, float> dictionary = new Dictionary<string, float>(StringComparer.Ordinal);
		for (int i = 0; i < state.DefNames.Count; i++)
		{
			dictionary.Add(state.DefNames[i], state.Chances[i]);
		}
		mutator.SetValue(null, state.Mutator);
		landmark.SetValue(null, state.Landmark);
		commonalities.SetValue(settings.GetValue(null), dictionary);
	}

	internal static IExposable State(Game game)
	{
		return SessionServices.SettingsForGame("sarg.smartodyssey.settings", game);
	}

	private static void BeforeSettings(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.EnsureSettings(State(Current.Game));
			__state = new SettingsEdit(Current.Game, sessionEpoch, GUI.enabled, Capture(), Capture(forEditing: true));
			GUI.enabled &= MP.IsHosting;
		}
	}

	private static Exception AfterSettings(Exception __exception, SettingsEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		try
		{
			if (!MP.IsInMultiplayer || Current.Game != __state.Game || __state.Epoch != sessionEpoch)
			{
				return __exception;
			}
			Snapshot snapshot;
			try
			{
				snapshot = Capture(forEditing: true);
			}
			finally
			{
				Apply(__state.Original);
			}
			if (__exception == null && MP.IsHosting && MP.InInterface && snapshot.Valid && !snapshot.SameAs(__state.Effective))
			{
				SyncSettings(snapshot.Mutator, snapshot.Landmark, snapshot.DefNames.ToArray(), snapshot.Chances.ToArray());
			}
			return __exception;
		}
		finally
		{
			GUI.enabled = __state.Enabled;
		}
	}

	private static void SyncSettings(float mutatorMultiplier, float landmarkMultiplier, string[] names, float[] chances)
	{
		if (MP.IsExecutingSyncCommand && Current.Game != null && names != null && chances != null && names.Length == chances.Length && !names.Any((string name) => string.IsNullOrEmpty(name) || DefDatabase<TileMutatorDef>.GetNamedSilentFail(name) == null))
		{
			Snapshot snapshot = new Snapshot
			{
				Mutator = mutatorMultiplier,
				Landmark = landmarkMultiplier,
				DefNames = names.ToList(),
				Chances = chances.ToList()
			};
			if (snapshot.Valid)
			{
				SessionServices.ApplySettings(State(Current.Game), snapshot);
			}
		}
	}
}
