using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.Temperature")]
public class VanillaTemperatureExpanded
{
	public VanillaTemperatureExpanded(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		MpCompat.RegisterLambdaDelegate("VanillaTemperatureExpanded.Comps.CompAcTempControl", "CompGetGizmosExtra", default(int));
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("VanillaTemperatureExpanded.Buildings.Building_AcControlUnit");
		MP.RegisterSyncMethod(type, "InterfaceChangeTargetNetworkTemperature");
		MP.RegisterSyncMethodLambda(type, "GetGizmos", 2);
	}
}
