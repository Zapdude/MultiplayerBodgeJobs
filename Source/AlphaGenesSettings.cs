using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

internal static class AlphaGenesSettings
{
	internal sealed class Snapshot : IExposable
	{
		internal float QuestRate = 1f;

		internal bool DisableQuests;

		internal bool GeneRemovalComa = true;

		internal bool RandomGenesToShamblers = true;

		internal bool Valid
		{
			get
			{
				if (float.IsFinite(QuestRate) && QuestRate >= 0.1f)
				{
					return QuestRate <= 5f;
				}
				return false;
			}
		}

		internal bool SameAs(Snapshot other)
		{
			if (QuestRate == other.QuestRate && DisableQuests == other.DisableQuests && GeneRemovalComa == other.GeneRemovalComa)
			{
				return RandomGenesToShamblers == other.RandomGenesToShamblers;
			}
			return false;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref QuestRate, "questRate", 1f, forceSave: true);
			Scribe_Values.Look(ref DisableQuests, "disableQuests", defaultValue: false, forceSave: true);
			Scribe_Values.Look(ref GeneRemovalComa, "geneRemovalComa", defaultValue: true, forceSave: true);
			Scribe_Values.Look(ref RandomGenesToShamblers, "randomGenesToShamblers", defaultValue: true, forceSave: true);
		}
	}

	private sealed class SettingsEdit
	{
		internal Snapshot Before;

		internal Game Game;

		internal bool Enabled;
	}

	private static FieldInfo settingsField;

	private static FieldInfo questRateField;

	private static FieldInfo disableQuestsField;

	private static FieldInfo comaField;

	private static FieldInfo shamblerField;

	private static Func<Listing_Standard, string, Rect, bool> resetButton;

	private const string SettingsKey = "sarg.alphagenes.settings";

	internal static bool Installed { get; private set; }

	private static object Settings => settingsField.GetValue(null);

	internal static void Install()
	{
		Type type = AccessTools.TypeByName("AlphaGenes.AlphaGenes_Settings");
		settingsField = RequiredField(AccessTools.TypeByName("AlphaGenes.AlphaGenes_Mod"), "settings");
		questRateField = RequiredField(type, "AG_QuestRate");
		disableQuestsField = RequiredField(type, "AG_DisableQuests");
		comaField = RequiredField(type, "AG_GeneRemovalComa");
		shamblerField = RequiredField(type, "AG_RandomGenesToShamblers");
		resetButton = (Func<Listing_Standard, string, Rect, bool>)AccessTools.DeclaredMethod("AlphaGenes.SettingsHelper:Settings_Button").CreateDelegate(typeof(Func<Listing_Standard, string, Rect, bool>));
		SessionServices.Install();
		SessionServices.RegisterSettings("sarg.alphagenes.settings", typeof(Snapshot), () => Capture(), () => new Snapshot(), (IExposable value) =>
		{
			Apply((Snapshot)value);
		}, (IExposable value) => value is Snapshot snapshot && snapshot.Valid, () => (ModSettings)Settings, "mpAlphaGenesSettings");
		MP.RegisterSyncMethod(typeof(AlphaGenesSettings), "SyncSettings").SetHostOnly().SetVersion(16);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type, "DoWindowContents"), new HarmonyMethod(typeof(AlphaGenesSettings), "BeforeSettings"), null, new HarmonyMethod(typeof(AlphaGenesSettings), "SettingsControls"), new HarmonyMethod(typeof(AlphaGenesSettings), "AfterSettings"));
		Installed = true;
	}

	private static FieldInfo RequiredField(Type type, string name)
	{
		return AccessTools.DeclaredField(type, name) ?? throw new MissingFieldException(type?.FullName, name);
	}

	internal static Snapshot Capture()
	{
		object settings = Settings;
		return new Snapshot
		{
			QuestRate = (float)questRateField.GetValue(settings),
			DisableQuests = (bool)disableQuestsField.GetValue(settings),
			GeneRemovalComa = (bool)comaField.GetValue(settings),
			RandomGenesToShamblers = (bool)shamblerField.GetValue(settings)
		};
	}

	private static void Apply(Snapshot snapshot)
	{
		object settings = Settings;
		questRateField.SetValue(settings, snapshot.QuestRate);
		disableQuestsField.SetValue(settings, snapshot.DisableQuests);
		comaField.SetValue(settings, snapshot.GeneRemovalComa);
		shamblerField.SetValue(settings, snapshot.RandomGenesToShamblers);
	}

	internal static IExposable SettingsState(Game game)
	{
		return SessionServices.SettingsForGame("sarg.alphagenes.settings", game);
	}

	private static void BeforeSettings(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			Current.Game.GetComponent<AlphaGenesSettingsComponent>().EnsureSession();
			__state = new SettingsEdit
			{
				Before = Capture(),
				Game = Current.Game,
				Enabled = GUI.enabled
			};
		}
	}

	private static Exception AfterSettings(Exception __exception, SettingsEdit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		Snapshot snapshot = Capture();
		Apply(__state.Before);
		GUI.enabled = __state.Enabled;
		if (__exception == null && MP.IsHosting && MP.InInterface && Current.Game == __state.Game && !snapshot.SameAs(__state.Before))
		{
			SyncSettings(snapshot.QuestRate, snapshot.DisableQuests, snapshot.GeneRemovalComa, snapshot.RandomGenesToShamblers);
		}
		return __exception;
	}

	private static void SyncSettings(float questRate, bool disableQuests, bool geneRemovalComa, bool randomGenesToShamblers)
	{
		Snapshot snapshot = new Snapshot
		{
			QuestRate = questRate,
			DisableQuests = disableQuests,
			GeneRemovalComa = geneRemovalComa,
			RandomGenesToShamblers = randomGenesToShamblers
		};
		if (snapshot.Valid && MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.ApplySettings(SettingsState(Current.Game), snapshot);
		}
	}

	private static float QuestRateSlider(Listing_Standard listing, float value, float min, float max)
	{
		bool enabled = GUI.enabled;
		GUI.enabled &= !MP.IsInMultiplayer || MP.IsHosting;
		try
		{
			return listing.Slider(value, min, max);
		}
		finally
		{
			GUI.enabled = enabled;
		}
	}

	private static bool QuestRateReset(Listing_Standard listing, string label, Rect rect)
	{
		bool enabled = GUI.enabled;
		GUI.enabled &= !MP.IsInMultiplayer || MP.IsHosting;
		try
		{
			return resetButton(listing, label, rect);
		}
		finally
		{
			GUI.enabled = enabled;
		}
	}

	private static void SimulationCheckbox(Listing_Standard listing, string label, ref bool value, string tooltip, float height, float labelPct)
	{
		bool enabled = GUI.enabled;
		GUI.enabled &= !MP.IsInMultiplayer || MP.IsHosting;
		try
		{
			listing.CheckboxLabeled(label, ref value, tooltip, height, labelPct);
		}
		finally
		{
			GUI.enabled = enabled;
		}
	}

	private static IEnumerable<CodeInstruction> SettingsControls(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo slider = AccessTools.DeclaredMethod(typeof(Listing_Standard), "Slider", new Type[3]
		{
			typeof(float),
			typeof(float),
			typeof(float)
		});
		MethodInfo reset = resetButton.Method;
		MethodInfo checkbox = AccessTools.DeclaredMethod(typeof(Listing_Standard), "CheckboxLabeled", new Type[5]
		{
			typeof(string),
			typeof(bool).MakeByRefType(),
			typeof(string),
			typeof(float),
			typeof(float)
		});
		int sliders = 0;
		int resets = 0;
		int checkboxes = 0;
		bool simulationField = false;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Ldflda && instruction.operand is FieldInfo fieldInfo && (fieldInfo == disableQuestsField || fieldInfo == comaField || fieldInfo == shamblerField))
			{
				simulationField = true;
			}
			MethodInfo methodInfo = null;
			if (instruction.Calls(slider))
			{
				sliders++;
				methodInfo = AccessTools.DeclaredMethod(typeof(AlphaGenesSettings), "QuestRateSlider");
			}
			else if (instruction.Calls(reset))
			{
				resets++;
				methodInfo = AccessTools.DeclaredMethod(typeof(AlphaGenesSettings), "QuestRateReset");
			}
			else if (instruction.Calls(checkbox) & simulationField)
			{
				simulationField = false;
				checkboxes++;
				methodInfo = AccessTools.DeclaredMethod(typeof(AlphaGenesSettings), "SimulationCheckbox");
			}
			yield return (methodInfo == null) ? instruction : new CodeInstruction(instruction)
			{
				opcode = OpCodes.Call,
				operand = methodInfo
			};
		}
		if ((sliders != 1 || resets != 1 || checkboxes != 3) | simulationField)
		{
			throw new InvalidOperationException($"Alpha Genes simulation settings controls changed: slider={sliders}, reset={resets}, checkbox={checkboxes}");
		}
	}
}
