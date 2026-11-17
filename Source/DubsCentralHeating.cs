using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Dubwise.DubsCentralHeating")]
public class DubsCentralHeating
{
	private static AccessTools.FieldRef<Designator, ThingDef> removePlumbingRemovalModeField;

	public DubsCentralHeating(ModContentPack mod)
	{
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	private static void LatePatch()
	{
		Type type = AccessTools.TypeByName("DubsCentralHeating.Designator_RemovePlumbing");
		removePlumbingRemovalModeField = AccessTools.FieldRefAccess<ThingDef>(type, "RemovalMode");
		MP.RegisterSyncWorker<Designator>(SyncRemovePlumbingDesignator, type, isImplicit: false, shouldConstruct: true);
		MP.RegisterAll(type.Assembly);
	}

	private static void SyncRemovePlumbingDesignator(SyncWorker sync, ref Designator designator)
	{
		if (sync.isWriting)
		{
			sync.Write(removePlumbingRemovalModeField(designator));
		}
		else
		{
			removePlumbingRemovalModeField(designator) = sync.Read<ThingDef>();
		}
	}
}
