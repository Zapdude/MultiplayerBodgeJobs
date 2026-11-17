using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

internal static class HuntersUseMeleeSettings
{
	internal sealed class Snapshot : IExposable
	{
		internal bool EnableFistFighting;

		internal bool EnableSimpleSidearms;

		internal bool SameAs(Snapshot other)
		{
			if (EnableFistFighting == other.EnableFistFighting)
			{
				return EnableSimpleSidearms == other.EnableSimpleSidearms;
			}
			return false;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref EnableFistFighting, "enableFistFighting", defaultValue: false, forceSave: true);
			Scribe_Values.Look(ref EnableSimpleSidearms, "enableSimpleSidearms", defaultValue: false, forceSave: true);
		}
	}

	private sealed class SettingsEdit
	{
		internal Snapshot Before;

		internal Game Game;

		internal int Epoch;

		internal bool Enabled;

		internal Color ContentColor;
	}

	private const string SettingsKey = "mlie.huntersusemelee.settings";

	private static FieldInfo settingsField;

	private static FieldInfo fistsField;

	private static FieldInfo sidearmsField;

	private static int sessionEpoch;

	internal static bool Installed { get; private set; }

	private static ModSettings Settings => (ModSettings)settingsField.GetValue(null);

	internal static void Install()
	{
		if (!Installed)
		{
			Type type = AccessTools.TypeByName("HuntersUseMelee.HuntersUseMeleeMod");
			Type type2 = AccessTools.TypeByName("HuntersUseMelee.HuntersUseMeleeSettings");
			settingsField = Required(type, "Settings");
			fistsField = Required(type2, "EnableFistFighting");
			sidearmsField = Required(type2, "EnableSimpleSidearms");
			SessionServices.Install();
			SessionServices.RegisterSettings("mlie.huntersusemelee.settings", typeof(Snapshot), () => Capture(), () => new Snapshot(), (IExposable value) =>
			{
				Apply((Snapshot)value);
			}, (IExposable value) => value is Snapshot, () => Settings, "mpHuntersUseMeleeSettings");
			SessionServices.RegisterLifecycle("mlie.huntersusemelee.settings.ui", null, () =>
			{
				sessionEpoch++;
			});
			MP.RegisterSyncMethod(typeof(HuntersUseMeleeSettings), "SyncSettings").SetHostOnly().SetVersion(18);
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoSettingsWindowContents"), new HarmonyMethod(typeof(HuntersUseMeleeSettings), "BeforeSettings"), null, null, new HarmonyMethod(typeof(HuntersUseMeleeSettings), "AfterSettings"));
			Installed = true;
		}
	}

	private static FieldInfo Required(Type type, string name)
	{
		return AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type?.FullName, name);
	}

	internal static Snapshot Capture()
	{
		ModSettings settings = Settings;
		return new Snapshot
		{
			EnableFistFighting = (bool)fistsField.GetValue(settings),
			EnableSimpleSidearms = (bool)sidearmsField.GetValue(settings)
		};
	}

	private static void Apply(Snapshot selected)
	{
		ModSettings settings = Settings;
		fistsField.SetValue(settings, selected.EnableFistFighting);
		sidearmsField.SetValue(settings, selected.EnableSimpleSidearms);
	}

	internal static IExposable SettingsState(Game game)
	{
		return SessionServices.SettingsForGame("mlie.huntersusemelee.settings", game);
	}

	private static void BeforeSettings(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			Current.Game.GetComponent<HuntersUseMeleeSettingsComponent>().EnsureSession();
			__state = new SettingsEdit
			{
				Before = Capture(),
				Game = Current.Game,
				Epoch = sessionEpoch,
				Enabled = GUI.enabled,
				ContentColor = GUI.contentColor
			};
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
			if (!MP.IsInMultiplayer || Current.Game != __state.Game || sessionEpoch != __state.Epoch)
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
				Apply(__state.Before);
			}
			if (__exception == null && MP.IsHosting && MP.InInterface && !snapshot.SameAs(__state.Before))
			{
				SyncSettings(snapshot.EnableFistFighting, snapshot.EnableSimpleSidearms);
			}
			return __exception;
		}
		finally
		{
			GUI.enabled = __state.Enabled;
			GUI.contentColor = __state.ContentColor;
		}
	}

	private static void SyncSettings(bool enableFistFighting, bool enableSimpleSidearms)
	{
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.ApplySettings(SettingsState(Current.Game), new Snapshot
			{
				EnableFistFighting = enableFistFighting,
				EnableSimpleSidearms = enableSimpleSidearms
			});
		}
	}
}
