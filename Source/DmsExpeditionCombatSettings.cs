using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

internal static class DmsExpeditionCombatSettings
{
	internal sealed class Snapshot : IExposable
	{
		internal bool Damage;

		public void ExposeData()
		{
			Scribe_Values.Look(ref Damage, "debrisDamage", defaultValue: false, forceSave: true);
		}
	}

	private sealed class Edit
	{
		internal Game Game;

		internal bool Original;

		internal bool Shared;
	}

	private const string Key = "aoba.deadmanswitch.expedition.combat";

	private static FieldInfo damage;

	internal static bool Installed { get; private set; }

	private static object Settings => DeadMansSwitchExpedition.Settings;

	internal static void Install()
	{
		damage = AccessTools.DeclaredField(AccessTools.TypeByName("DMSE.PlayerConfigSettings"), "interceptDebrisDamage");
		SessionServices.Install();
		SessionServices.RegisterSettings("aoba.deadmanswitch.expedition.combat", typeof(Snapshot), () => Capture(), () => new Snapshot(), (IExposable value) =>
		{
			damage.SetValue(Settings, ((Snapshot)value).Damage);
		}, (IExposable value) => value is Snapshot, () => (ModSettings)Settings, "mpDmseCombatSettings");
		MP.RegisterSyncMethod(typeof(DmsExpeditionCombatSettings), "SyncDamage").SetHostOnly().SetVersion(30);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("DMSE.DMSEMod:DoSettingsWindowContents"), new HarmonyMethod(typeof(DmsExpeditionCombatSettings), "Before"), null, new HarmonyMethod(typeof(DmsExpeditionCombatSettings), "SettingsControls"), new HarmonyMethod(typeof(DmsExpeditionCombatSettings), "After"));
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("DMSE.InterceptDebrisChunk:Land"), null, null, new HarmonyMethod(typeof(DmsExpeditionCombatSettings), "DamageSettingIl"));
		Installed = true;
	}

	private static Snapshot Capture()
	{
		return new Snapshot
		{
			Damage = (Settings != null && (bool)damage.GetValue(Settings))
		};
	}

	private static void Before(out Edit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.EnsureSettings(State(Current.Game));
			bool flag = (bool)damage.GetValue(Settings);
			__state = new Edit
			{
				Game = Current.Game,
				Original = flag,
				Shared = flag
			};
		}
	}

	private static Exception After(Exception __exception, Edit __state)
	{
		if (__state == null)
		{
			return __exception;
		}
		bool flag = (bool)damage.GetValue(Settings);
		damage.SetValue(Settings, __state.Original);
		if (__exception == null && Current.Game == __state.Game && MP.IsHosting && MP.InInterface && flag != __state.Shared)
		{
			SyncDamage(flag);
		}
		return __exception;
	}

	private static void SyncDamage(bool value)
	{
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.ApplySettings(SessionServices.SettingsForGame("aoba.deadmanswitch.expedition.combat", Current.Game), new Snapshot
			{
				Damage = value
			});
		}
	}

	internal static IExposable State(Game game)
	{
		return SessionServices.SettingsForGame("aoba.deadmanswitch.expedition.combat", game);
	}

	private static bool SharedDamage(object settings)
	{
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			SessionServices.EnsureSettings(State(Current.Game));
		}
		return (bool)damage.GetValue(settings);
	}

	private static void DamageCheckbox(Listing_Standard listing, string label, ref bool value, string tooltip, float height, float labelPct)
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
		MethodInfo checkbox = AccessTools.DeclaredMethod(typeof(Listing_Standard), "CheckboxLabeled", new Type[5]
		{
			typeof(string),
			typeof(bool).MakeByRefType(),
			typeof(string),
			typeof(float),
			typeof(float)
		});
		bool selected = false;
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.opcode == OpCodes.Ldflda && object.Equals(instruction.operand, damage))
			{
				selected = true;
			}
			if (selected && instruction.Calls(checkbox))
			{
				selected = false;
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DmsExpeditionCombatSettings), "DamageCheckbox")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if ((count != 1) | selected)
		{
			throw new InvalidOperationException($"DMSE debris damage control changed: {count}");
		}
	}

	private static IEnumerable<CodeInstruction> DamageSettingIl(IEnumerable<CodeInstruction> instructions)
	{
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.LoadsField(damage))
			{
				count++;
				yield return new CodeInstruction(instruction)
				{
					opcode = OpCodes.Call,
					operand = AccessTools.DeclaredMethod(typeof(DmsExpeditionCombatSettings), "SharedDamage")
				};
			}
			else
			{
				yield return instruction;
			}
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"DMSE debris damage setting IL changed: {count}");
		}
	}
}
