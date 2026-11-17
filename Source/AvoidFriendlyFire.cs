using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("falconne.AFF")]
public class AvoidFriendlyFire
{
	private static IDictionary extendedPawnDataDictionary;

	private static IDictionary ExtendedPawnDataDictionary
	{
		get
		{
			if (extendedPawnDataDictionary == null)
			{
				Type type = AccessTools.TypeByName("AvoidFriendlyFire.ExtendedDataStorage");
				WorldComponent component = Find.World.GetComponent(type);
				extendedPawnDataDictionary = AccessTools.Field(type, "_store").GetValue(component) as IDictionary;
			}
			return extendedPawnDataDictionary;
		}
	}

	public AvoidFriendlyFire(ModContentPack mod)
	{
		Type targetType = AccessTools.TypeByName("AvoidFriendlyFire.ExtendedPawnData");
		MP.RegisterSyncWorker<object>(SyncWorkerFor, targetType);
		MpCompat.RegisterLambdaDelegate("AvoidFriendlyFire.Pawn_DraftController_GetGizmos_Patch", "Postfix", 1);
	}

	private static int GetIdFromExtendedPawnData(object extendedPawnData)
	{
		foreach (object key in ExtendedPawnDataDictionary.Keys)
		{
			if (ExtendedPawnDataDictionary[key] == extendedPawnData)
			{
				return (int)key;
			}
		}
		return 0;
	}

	private static void SyncWorkerFor(SyncWorker sw, ref object extendedPawnData)
	{
		if (sw.isWriting)
		{
			sw.Write(GetIdFromExtendedPawnData(extendedPawnData));
			return;
		}
		int num = sw.Read<int>();
		extendedPawnData = ExtendedPawnDataDictionary[num];
	}
}
