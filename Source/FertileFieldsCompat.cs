using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("jamaicancastle.RF.fertilefields")]
[MpCompatFor("greysuki.RF.fertilefields")]
internal class FertileFieldsCompat
{
	public FertileFieldsCompat(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
		PatchingUtilities.PatchSystemRand("RFF_Code.Building_CompostBin:PlaceProduct");
		Type type = AccessTools.TypeByName("RFF_Code.GrowZoneManager");
		MP.RegisterSyncMethod(type, "ToggleReturnToSoil");
		MP.RegisterSyncMethod(type, "ToggleDesignateReplacements");
	}

	private static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod("RFF_Code.Building_CompostBarrel", "GetGizmos", default(int)).SetDebugOnly();
	}
}
