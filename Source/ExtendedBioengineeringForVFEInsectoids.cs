using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("turnovus.submod.extendedbioengineering")]
public class ExtendedBioengineeringForVFEInsectoids
{
	public ExtendedBioengineeringForVFEInsectoids(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("extendedbioengineering.Building_Inducer");
		MP.RegisterSyncMethod(type, "TryFireInducerNow");
		MP.RegisterSyncMethod(type, "SetCountdownToNearZero").SetDebugOnly();
		PatchingUtilities.PatchSystemRand(AccessTools.Method(AccessTools.FirstInner(AccessTools.TypeByName("extendedbioengineering.OutputWorker_Dissect"), (Type _) => true), "MoveNext"), patchPushPop: false);
		PatchingUtilities.PatchSystemRand(AccessTools.Method(AccessTools.FirstInner(AccessTools.TypeByName("extendedbioengineering.OutputWorker_Genes"), (Type _) => true), "MoveNext"), patchPushPop: false);
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("extendedbioengineering.ThingComp_JellySynthesizer");
		MP.RegisterSyncMethod(type, "TrySetNewSetting");
		MP.RegisterSyncMethod(AccessTools.PropertySetter(type, "TargetFuel"));
		MpCompat.RegisterLambdaMethod(type, "CompGetGizmosExtra", 1);
	}
}
