using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Aelanna.EccentricTech.FusionPower")]
internal class EccentricTechFusionPower
{
	public EccentricTechFusionPower(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("EccentricPower.CompFusionGenerator");
		MP.RegisterSyncMethod(type, "SetDesiredOutputLevel");
		MP.RegisterSyncMethod(type, "SetPowerMode");
		MP.RegisterSyncMethod(type, "StartIgnitionCycle");
		MP.RegisterSyncMethod(type, "StartShutdownCycle");
		MP.RegisterSyncMethod(type, "StartEmergencyVent");
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	public static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("EccentricPower.CompFusionCapacitor"), "CompGetGizmosExtra", 0, 1, 2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("EccentricPower.CompFusionStorage"), "CompGetGizmosExtra", 1, 3, 4, 5, 6).Skip(2).SetDebugOnly();
		MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("EccentricPower.CompFusionGenerator"), "CompGetGizmosExtra", Enumerable.Range(0, 4).ToArray()).SetDebugOnly();
	}
}
