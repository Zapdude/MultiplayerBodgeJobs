using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("densevoid.onebedtosleepwithall")]
internal class OneBedToSleepWithAll
{
	private static Type compType;

	public OneBedToSleepWithAll(ModContentPack mod)
	{
		compType = AccessTools.TypeByName("OneBedToSleepWithAll.CompPolygamyMode");
		MpCompat.RegisterLambdaMethod(compType, "CompGetGizmosExtra", 1);
		MP.RegisterSyncWorker<ThingComp>(SyncCompPolygamyMode, compType);
	}

	private static void SyncCompPolygamyMode(SyncWorker sync, ref ThingComp comp)
	{
		if (sync.isWriting)
		{
			sync.Write(comp.parent);
			return;
		}
		comp = sync.Read<ThingWithComps>().AllComps.FirstOrDefault((ThingComp x) => x.GetType() == compType);
	}
}
