using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("rimfridge.kv.rw")]
public class RimFridgeCompat
{
	public RimFridgeCompat(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("RimFridge.CompRefrigerator");
		MP.RegisterSyncMethod(type, "InterfaceChangeTargetTemperature");
		MpCompat.RegisterLambdaDelegate(type, "CompGetGizmosExtra", 2);
		MpCompat.RegisterLambdaMethod("RimFridge.CompToggleGlower", "CompGetGizmosExtra", default(int));
		PatchingUtilities.ReplaceCurrentMapUsage("RimFridge.Patch_ReachabilityUtility_CanReach:Prefix", logIfNothingPatched: false, logIfMissingMethod: false);
	}
}
