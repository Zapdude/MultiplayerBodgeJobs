using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Defi.GeneRipper")]
public class GeneRipper
{
	public GeneRipper(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("GeneRipper.Building_GeneRipper");
		MpCompat.RegisterLambdaDelegate(type, "SelectPawn", 1);
		MP.RegisterSyncMethod(type, "Finish");
		MP.RegisterSyncMethod(type, "Cancel");
	}
}
