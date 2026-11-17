using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sarg.alphaarmoury")]
internal sealed class AlphaArmoury
{
	internal sealed class Snapshot : IExposable
	{
		internal int Min = 1;

		internal int Max = 3;

		internal int Flags = 16;

		internal bool Valid
		{
			get
			{
				if (Min >= 1 && Max <= 10 && Min < Max)
				{
					int flags = Flags;
					if (flags >= 0)
					{
						return flags < 64;
					}
					return false;
				}
				return false;
			}
		}

		internal bool SameAs(Snapshot other)
		{
			if (Min == other.Min && Max == other.Max)
			{
				return Flags == other.Flags;
			}
			return false;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref Min, "minWeaponTraits", 1);
			Scribe_Values.Look(ref Max, "maxWeaponTraits", 3);
			Scribe_Values.Look(ref Flags, "simulationFlags", 16);
		}
	}

	private sealed record SettingsEdit(Game Game, int Epoch, bool Enabled, Snapshot Original, int Startup);

	private const string Key = "sarg.alphaarmoury.settings";

	private static readonly string[] SimulationFlags = new string[6] { "sendWeaponPods", "addWeaponsToMoreMercs", "addKitsToMoreMercs", "addWeaponsToAllRaids", "makeRaidWeaponsBiocoded", "makeRaidWeaponsDestroyedOnDrop" };

	private static FieldInfo settings;

	private static FieldInfo min;

	private static FieldInfo max;

	private static FieldInfo questReward;

	private static FieldInfo urbanRuins;

	private static FieldInfo[] flags;

	private static int? loadedStartup;

	private static int sessionEpoch;

	internal static bool Installed { get; private set; }

	public AlphaArmoury(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("AlphaArmoury.AlphaArmoury_Mod");
		Type settingsType = AccessTools.TypeByName("AlphaArmoury.AlphaArmoury_Settings");
		settings = AccessTools.DeclaredField(type, "settings");
		min = Field("minWeaponTraits", typeof(int));
		max = Field("maxWeaponTraits", typeof(int));
		flags = SimulationFlags.Select((string name) => Field(name, typeof(bool))).ToArray();
		questReward = Field("makeMoreCommonAsQuestRewards", typeof(bool));
		urbanRuins = Field("addUniquesToAUR", typeof(bool));
		(AccessTools.DeclaredMethod("Multiplayer.Client.Compatibility.StartupSettings:Register") ?? throw new MissingMethodException("Update Multiplayer together with Compatibility: StartupSettings.Register")).CreateDelegate<Action<string, Func<int>, Func<int>>>()("Alpha Armoury", () => loadedStartup ?? (-1), StartupOptionsForDefs);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(typeof(LoadedModManager), "ApplyPatches"), Hook("CaptureLoadedStartup"));
		SessionServices.Install();
		SessionServices.RegisterSettings("sarg.alphaarmoury.settings", typeof(Snapshot), Capture, () => new Snapshot(), (IExposable state) =>
		{
			Apply((Snapshot)state);
		}, (IExposable state) => state is Snapshot snapshot && snapshot.Valid, () => (ModSettings)settings.GetValue(null), "mpAlphaArmourySettings");
		SessionServices.RegisterLifecycle("sarg.alphaarmoury.settings.ui", null, () =>
		{
			sessionEpoch++;
		});
		MP.RegisterSyncMethod(typeof(AlphaArmoury), "SyncSettings").SetHostOnly().SetVersion(35);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoSettingsWindowContents"), Hook("BeforeSettings"), null, null, Hook("AfterSettings"));
		Installed = true;
		FieldInfo Field(string name, Type type2)
		{
			FieldInfo fieldInfo = AccessTools.DeclaredField(settingsType, name);
			if ((object)fieldInfo == null || !fieldInfo.IsStatic || !(fieldInfo.FieldType == type2))
			{
				throw new MissingFieldException(settingsType.FullName, name);
			}
			return fieldInfo;
		}
	}

	private static HarmonyMethod Hook(string name)
	{
		return new HarmonyMethod(typeof(AlphaArmoury), name);
	}

	private static int StartupOptions()
	{
		return (((bool)questReward.GetValue(null)) ? 1 : 0) | (((bool)urbanRuins.GetValue(null)) ? 2 : 0);
	}

	private static int StartupOptionsForDefs()
	{
		return StartupOptions() & ((!ModLister.HasActiveModWithName("Ancient urban ruins")) ? 1 : 3);
	}

	private static void CaptureLoadedStartup()
	{
		loadedStartup = StartupOptionsForDefs();
	}

	private static void RestoreStartupOptions(int options)
	{
		questReward.SetValue(null, (options & 1) != 0);
		urbanRuins.SetValue(null, (options & 2) != 0);
	}

	private static Snapshot Capture()
	{
		Snapshot snapshot = new Snapshot
		{
			Min = (int)min.GetValue(null),
			Max = (int)max.GetValue(null),
			Flags = 0
		};
		for (int i = 0; i < flags.Length; i++)
		{
			if ((bool)flags[i].GetValue(null))
			{
				snapshot.Flags |= 1 << i;
			}
		}
		return snapshot;
	}

	private static void Apply(Snapshot state)
	{
		min.SetValue(null, state.Min);
		max.SetValue(null, state.Max);
		for (int i = 0; i < flags.Length; i++)
		{
			flags[i].SetValue(null, (state.Flags & (1 << i)) != 0);
		}
	}

	internal static IExposable State(Game game)
	{
		return SessionServices.SettingsForGame("sarg.alphaarmoury.settings", game);
	}

	private static void BeforeSettings(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.EnsureSettings(State(Current.Game));
			__state = new SettingsEdit(Current.Game, sessionEpoch, GUI.enabled, Capture(), StartupOptions());
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
				snapshot = Capture();
			}
			finally
			{
				bool flag = StartupOptions() != __state.Startup;
				RestoreStartupOptions(__state.Startup);
				Apply(__state.Original);
				if (flag && MP.IsHosting)
				{
					Messages.Message("MpStartupSettingsOutsideRoom".Translate("Alpha Armoury"), MessageTypeDefOf.RejectInput, historical: false);
				}
			}
			if (__exception == null && MP.IsHosting && MP.InInterface && snapshot.Valid && !snapshot.SameAs(__state.Original))
			{
				SyncSettings(snapshot.Min, snapshot.Max, snapshot.Flags);
			}
			return __exception;
		}
		finally
		{
			GUI.enabled = __state.Enabled;
		}
	}

	private static void SyncSettings(int minTraits, int maxTraits, int simulationFlags)
	{
		if (MP.IsExecutingSyncCommand && Current.Game != null)
		{
			Snapshot snapshot = new Snapshot
			{
				Min = minTraits,
				Max = maxTraits,
				Flags = simulationFlags
			};
			if (snapshot.Valid)
			{
				SessionServices.ApplySettings(State(Current.Game), snapshot);
			}
		}
	}
}
