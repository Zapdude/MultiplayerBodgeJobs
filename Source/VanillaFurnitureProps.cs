using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VFEPropsandDecor")]
public sealed class VanillaFurnitureProps
{
	internal sealed class Snapshot : IExposable
	{
		internal float Cost = 1f;

		internal float Refund = 1f;

		internal bool Valid
		{
			get
			{
				if (float.IsFinite(Cost))
				{
					float cost = Cost;
					if (cost >= 0f && cost <= 5f && float.IsFinite(Refund))
					{
						cost = Refund;
						if (cost >= 0f)
						{
							return cost <= 1f;
						}
						return false;
					}
				}
				return false;
			}
		}

		internal bool Same(Snapshot other)
		{
			if (Cost == other.Cost)
			{
				return Refund == other.Refund;
			}
			return false;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref Cost, "cost", 1f, forceSave: true);
			Scribe_Values.Look(ref Refund, "refund", 1f, forceSave: true);
		}
	}

	private sealed class SettingsEdit
	{
		internal Snapshot Before;

		internal Game Game;

		internal int Epoch;

		internal bool Enabled;

		internal Color Color;
	}

	private const string SettingsKey = "vanillaexpanded.vfepropsanddecor.settings";

	private static FieldInfo cost;

	private static FieldInfo refund;

	private static ModSettings personalSettings;

	private static int epoch;

	internal static bool Installed { get; private set; }

	public VanillaFurnitureProps(ModContentPack content)
	{
		LongEventHandler.ExecuteWhenFinished(Install);
	}

	private static void Install()
	{
		Type type = AccessTools.TypeByName("VFEProps.VFEProps_Settings") ?? throw new TypeLoadException("VFEProps.VFEProps_Settings");
		Type modType = AccessTools.TypeByName("VFEProps.VFEProps_Mod") ?? throw new TypeLoadException("VFEProps.VFEProps_Mod");
		cost = AccessTools.DeclaredField(type, "costMultiplier") ?? throw new MissingFieldException(type.FullName, "costMultiplier");
		refund = AccessTools.DeclaredField(type, "silverReturnMultiplier") ?? throw new MissingFieldException(type.FullName, "silverReturnMultiplier");
		Mod obj = LoadedModManager.ModHandles.Single((Mod mod) => mod.GetType() == modType);
		personalSettings = (ModSettings)AccessTools.DeclaredMethod(typeof(Mod), "GetSettings").MakeGenericMethod(type).Invoke(obj, null);
		SessionServices.Install();
		SessionServices.RegisterSettings("vanillaexpanded.vfepropsanddecor.settings", typeof(Snapshot), Capture, () => new Snapshot(), (IExposable value) =>
		{
			Apply((Snapshot)value);
		}, (IExposable value) => value is Snapshot snapshot && snapshot.Valid, () => personalSettings, "mpVfePropsSettings");
		SessionServices.RegisterLifecycle("vanillaexpanded.vfepropsanddecor.settings.ui", null, () =>
		{
			epoch++;
		});
		MP.RegisterSyncMethod(typeof(VanillaFurnitureProps), "SyncSettings").SetHostOnly().SetVersion(33);
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod(modType, "DoSettingsWindowContents"), Method("BeforeSettings"), null, null, Method("AfterSettings"));
		Type type2 = AccessTools.TypeByName("VFEProps.Building_SubstractsSilver") ?? throw new TypeLoadException("VFEProps.Building_SubstractsSilver");
		string[] array = new string[2] { "CheckSilverInMap", "RemoveSilverFromMap" };
		foreach (string text in array)
		{
			MpCompat.harmony.Patch(AccessTools.DeclaredMethod(type2, text) ?? throw new MissingMethodException(type2.FullName, text), null, null, Method("PaymentMap"));
		}
		MpCompat.harmony.Patch(AccessTools.DeclaredMethod("VFEProps.Utils:CostCalculator") ?? throw new MissingMethodException("VFEProps.Utils", "CostCalculator"), null, null, Method("PriceDevMode"));
		Installed = true;
	}

	private static HarmonyMethod Method(string name)
	{
		return new HarmonyMethod(typeof(VanillaFurnitureProps), name);
	}

	private static IEnumerable<CodeInstruction> PriceDevMode(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(Prefs), "DevMode");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(getter))
			{
				instruction.operand = AccessTools.DeclaredMethod(typeof(VanillaFurnitureProps), "PriceUsesDevMode");
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"VFE Props price contract changed: DevMode reads={count}");
		}
	}

	private static bool PriceUsesDevMode()
	{
		if (!MP.IsInMultiplayer)
		{
			return Prefs.DevMode;
		}
		return false;
	}

	private static IEnumerable<CodeInstruction> PaymentMap(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo getter = AccessTools.PropertyGetter(typeof(Find), "CurrentMap");
		int count = 0;
		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(getter))
			{
				CodeInstruction codeInstruction = new CodeInstruction(OpCodes.Ldarg_0);
				codeInstruction.labels.AddRange(instruction.labels);
				instruction.labels.Clear();
				codeInstruction.blocks.AddRange(instruction.blocks);
				instruction.blocks.Clear();
				yield return codeInstruction;
				instruction.operand = AccessTools.DeclaredMethod(typeof(VanillaFurnitureProps), "MapForPayment");
				count++;
			}
			yield return instruction;
		}
		if (count != 1)
		{
			throw new InvalidOperationException($"VFE Props payment contract changed: map reads={count}");
		}
	}

	private static Map MapForPayment(Thing building)
	{
		if (!MP.IsInMultiplayer)
		{
			return Find.CurrentMap;
		}
		return building.Map;
	}

	private static IExposable Capture()
	{
		return new Snapshot
		{
			Cost = (float)cost.GetValue(null),
			Refund = (float)refund.GetValue(null)
		};
	}

	private static void Apply(Snapshot selected)
	{
		cost.SetValue(null, selected.Cost);
		refund.SetValue(null, selected.Refund);
	}

	internal static IExposable State(Game game)
	{
		return SessionServices.SettingsForGame("vanillaexpanded.vfepropsanddecor.settings", game);
	}

	private static void BeforeSettings(out SettingsEdit __state)
	{
		__state = null;
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			Current.Game.GetComponent<VanillaFurniturePropsSettings>().Ensure();
			__state = new SettingsEdit
			{
				Before = (Snapshot)Capture(),
				Game = Current.Game,
				Epoch = epoch,
				Enabled = GUI.enabled,
				Color = GUI.contentColor
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
			if (!MP.IsInMultiplayer || Current.Game != __state.Game || epoch != __state.Epoch)
			{
				return __exception;
			}
			Snapshot snapshot;
			try
			{
				snapshot = (Snapshot)Capture();
			}
			finally
			{
				Apply(__state.Before);
			}
			if (__exception == null && MP.IsHosting && MP.InInterface && snapshot.Valid && !snapshot.Same(__state.Before))
			{
				SyncSettings(snapshot.Cost, snapshot.Refund);
			}
			return __exception;
		}
		finally
		{
			GUI.enabled = __state.Enabled;
			GUI.contentColor = __state.Color;
		}
	}

	private static void SyncSettings(float selectedCost, float selectedRefund)
	{
		if (MP.IsInMultiplayer && Current.Game != null)
		{
			Snapshot snapshot = new Snapshot
			{
				Cost = selectedCost,
				Refund = selectedRefund
			};
			if (snapshot.Valid)
			{
				SessionServices.ApplySettings(State(Current.Game), snapshot);
			}
		}
	}
}
