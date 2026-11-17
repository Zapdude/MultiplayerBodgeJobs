using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Aoba.Fortress.Industrial")]
public class FortificationsIndustrial
{
	public FortificationsIndustrial(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		PatchingUtilities.PatchPushPopRand("AOBAUtilities.CompFlecker:CompTick");
		MpCompat.RegisterLambdaMethod("AOBAUtilities.CompFueledSpawner", "CompGetGizmosExtra", default(int)).SetDebugOnly();
	}

	private static void LatePatch()
	{
		PatchingUtilities.PatchPushPopRand("Fortification.CompCastFlecker:BurstFleck");
		PatchingUtilities.PatchUnityRand("Fortification.CompCastFlecker:DrawPos");
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("Fortification.Building_TurretCapacity:GetOut"));
		MpCompat.RegisterLambdaMethod("Fortification.CompExplosiveWithComposite", "CompGetGizmosExtra", default(int)).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("Fortification.MinifiedThingDeployable:Deploy"));
		MethodInfo methodInfo = AccessTools.DeclaredMethod("Fortification.Building_TurretCapacityCE:GetOut");
		if (methodInfo != null)
		{
			MP.RegisterSyncMethod(methodInfo);
		}
		Type type = AccessTools.TypeByName("Fortification.CompExplosiveWithCompositeCE");
		if (type != null)
		{
			MP.RegisterSyncDelegateLambda(type, "CompGetGizmosExtra", 0).SetDebugOnly();
		}
	}
}
