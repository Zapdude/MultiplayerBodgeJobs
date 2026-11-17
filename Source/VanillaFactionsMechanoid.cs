using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("OskarPotocki.VFE.Mechanoid")]
public class VanillaFactionsMechanoid
{
	public VanillaFactionsMechanoid(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("VFEMech.MissileSilo");
		MP.RegisterSyncMethod(type, "StartFire");
		MethodInfo methodInfo = AccessTools.Method(type, "ConfigureNewTarget");
		MP.RegisterSyncMethod(methodInfo);
		ISyncMethod[] array = MpCompat.RegisterLambdaMethod(type, "GetGizmos", 2, 3);
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SetDebugOnly();
		}
		MpCompat.harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(VanillaFactionsMechanoid), "CloseWorldTargetter"));
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("VFE.Mechanoids.Buildings.Building_AutoPlant"), "GetGizmos", 0, 2, 3);
		Type type2 = AccessTools.TypeByName("VFEMech.Building_IndoctrinationPod");
		MP.RegisterSyncMethod(type2, "EjectContents");
		MpCompat.RegisterLambdaDelegate(type2, "GetGizmos", 1, 3);
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("VFEMech.Building_IndustrialApiary"), "GetGizmos", 0, 1).SetDebugOnly();
		MpCompat.RegisterLambdaDelegate(AccessTools.TypeByName("VFEMech.CompPropaganda"), "GetGizmos", 1, 3);
	}

	private static void CloseWorldTargetter(bool __result)
	{
		if ((MP.IsInMultiplayer & __result) && Find.WorldTargeter.IsTargeting && Find.WorldTargeter.closeWorldTabWhenFinished)
		{
			Find.WorldTargeter.StopTargeting();
		}
	}
}
