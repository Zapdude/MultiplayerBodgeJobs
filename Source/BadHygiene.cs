using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("Dubwise.DubsBadHygiene")]
[MpCompatFor("Dubwise.DubsBadHygiene.Lite")]
public class BadHygiene
{
	private static AccessTools.FieldRef<Designator, ThingDef> removePlumbingRemovalModeField;

	private static AccessTools.FieldRef<Designator, bool> placeFertilizerAddingField;

	public BadHygiene(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("DubsBadHygiene.Comp_SaunaHeater");
		PatchingUtilities.PatchPushPopRand(new MethodInfo[2]
		{
			AccessTools.Method(type, "SteamyNow"),
			AccessTools.Method(type, "CompTick")
		});
		Type type2 = AccessTools.TypeByName("DubsBadHygiene.Designator_AreaPlaceFertilizer");
		placeFertilizerAddingField = AccessTools.FieldRefAccess<bool>(type2, "Adding");
		MP.RegisterSyncWorker<Designator>(SyncFertilizerAreaDesignator, type2, isImplicit: false, shouldConstruct: true);
		type2 = AccessTools.TypeByName("DubsBadHygiene.Designator_RemovePlumbing");
		removePlumbingRemovalModeField = AccessTools.FieldRefAccess<ThingDef>(type2, "RemovalMode");
		MP.RegisterSyncWorker<Designator>(SyncRemovePlumbingDesignator, type2, isImplicit: false, shouldConstruct: true);
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

	private static void SyncFertilizerAreaDesignator(SyncWorker sync, ref Designator designator)
	{
		if (sync.isWriting)
		{
			sync.Write(placeFertilizerAddingField(designator));
		}
		else
		{
			placeFertilizerAddingField(designator) = sync.Read<bool>();
		}
	}
}
