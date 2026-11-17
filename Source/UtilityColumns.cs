using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("nephlite.orbitaltradecolumn")]
public class UtilityColumns
{
	private static AccessTools.FieldRef<object, IList> claymoreColumnChargesField;

	private static AccessTools.FieldRef<object, bool[]> claymoreChargeSettingsField;

	private static AccessTools.FieldRef<object, Building> gizmoClaymoreReferenceField;

	private static AccessTools.FieldRef<object, Gizmo> gizmoInnerClassParentField;

	public UtilityColumns(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("RimWorldColumns.Building_ClaymoreColumn");
		claymoreColumnChargesField = AccessTools.FieldRefAccess<IList>(type, "Charges");
		MpCompat.RegisterLambdaMethod(type, "GetGizmos", 0, 1).SetDebugOnly();
		claymoreChargeSettingsField = AccessTools.FieldRefAccess<bool[]>("RimWorldColumns.ClaymoreCharge:settings");
		MP.RegisterSyncMethod(typeof(UtilityColumns), "SyncSettings");
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("RimWorldColumns.Gizmo_ClaymoreSafetySettings");
		MethodInfo lambda = MpMethodUtil.GetLambda(type, "GizmoOnGUI");
		gizmoClaymoreReferenceField = AccessTools.FieldRefAccess<Building>(type, "claymoreReference");
		gizmoInnerClassParentField = AccessTools.FieldRefAccess<Gizmo>(lambda.DeclaringType, "<>4__this");
		MpCompat.harmony.Patch(lambda, new HarmonyMethod(typeof(UtilityColumns), "PreDraw"), new HarmonyMethod(typeof(UtilityColumns), "PostDraw"));
	}

	private static void PreDraw(object __instance, ref bool[][] __state)
	{
		if (MP.IsInMultiplayer)
		{
			Gizmo instance = gizmoInnerClassParentField(__instance);
			Building instance2 = gizmoClaymoreReferenceField(instance);
			IList list = claymoreColumnChargesField(instance2);
			__state = new bool[4][];
			for (int i = 0; i < 4; i++)
			{
				bool[] array = claymoreChargeSettingsField(list[i]);
				__state[i] = new bool[3]
				{
					array[0],
					array[1],
					array[2]
				};
			}
		}
	}

	private static void PostDraw(object __instance, ref bool[][] __state)
	{
		if (!MP.IsInMultiplayer)
		{
			return;
		}
		Gizmo instance = gizmoInnerClassParentField(__instance);
		Building building = gizmoClaymoreReferenceField(instance);
		IList list = claymoreColumnChargesField(building);
		bool flag = true;
		for (int i = 0; i < 4; i++)
		{
			bool[] array = claymoreChargeSettingsField(list[i]);
			bool[] array2 = __state[i];
			if (array[0] != array2[0] || array[1] != array2[1] || array[2] != array2[2])
			{
				flag = false;
				break;
			}
		}
		if (!flag)
		{
			bool[][] array3 = new bool[4][];
			for (int j = 0; j < 4; j++)
			{
				array3[j] = claymoreChargeSettingsField(list[j]);
				claymoreChargeSettingsField(list[j]) = __state[j];
			}
			SyncSettings(building, array3);
		}
	}

	private static void SyncSettings(Building column, bool[][] settings)
	{
		IList list = claymoreColumnChargesField(column);
		for (int i = 0; i < 4; i++)
		{
			claymoreChargeSettingsField(list[i]) = settings[i];
		}
	}
}
