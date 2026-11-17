using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Merthsoft.TimeOfDaySwitch")]
internal class TimeOfDAySwitch
{
	private static bool[] tempState;

	private static AccessTools.FieldRef<bool[]> copiedStatesField;

	public TimeOfDAySwitch(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("Merthsoft.TimerSwitches.TimeOfDaySwitch");
		MP.RegisterSyncMethod(type, "SetState");
		MP.RegisterSyncMethod(type, "Paste").SetPreInvoke(PrePaste).SetPostInvoke(PostPaste)
			.TransformTarget(Serializer.New((Building_PowerSwitch building) => (building: building, states: copiedStatesField()), ((Building_PowerSwitch building, bool[] states) tuple) =>
			{
				tempState = tuple.states;
				return tuple.building;
			}), skipTypeCheck: true);
		copiedStatesField = AccessTools.StaticFieldRefAccess<bool[]>(AccessTools.DeclaredField("Merthsoft.TimerSwitches.ClipBoard:states"));
	}

	private static void PrePaste(object instance, object[] args)
	{
		ref bool[] reference = ref copiedStatesField();
		bool[] array = tempState;
		bool[] array2 = reference;
		reference = array;
		tempState = array2;
	}

	private static void PostPaste(object instance, object[] args)
	{
		copiedStatesField() = tempState;
		tempState = null;
	}
}
