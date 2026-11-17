using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("hatti.cleaningarea")]
[MpCompatFor("s1.cleaningareatemp")]
public class CleaningArea
{
	private static Type CleaningAreaMapComponentType;

	public CleaningArea(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("CleaningArea.CleaningArea_MapComponent") ?? AccessTools.TypeByName("CleaningAreaTemp.CleaningArea_MapComponent");
		CleaningAreaMapComponentType = type;
		Type type2 = type;
		MP.RegisterSyncMethod(AccessTools.PropertySetter(type2, "cleaningArea"));
		MP.RegisterSyncWorker<MapComponent>(SyncWorkerForCleaningArea, type2);
	}

	private static void SyncWorkerForCleaningArea(SyncWorker sw, ref MapComponent comp)
	{
		if (sw.isWriting)
		{
			sw.Write(comp.map);
		}
		else
		{
			comp = sw.Read<Map>().GetComponent(CleaningAreaMapComponentType);
		}
	}
}
