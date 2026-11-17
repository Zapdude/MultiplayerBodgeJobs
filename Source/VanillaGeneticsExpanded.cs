using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VGeneticsE")]
internal class VanillaGeneticsExpanded
{
	private static AccessTools.FieldRef<object, Map> setGenomeListMap;

	private static AccessTools.FieldRef<object, Building> setGenomeListBuilding;

	public VanillaGeneticsExpanded(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("GeneticRim.Command_SetGenomeList");
		setGenomeListMap = AccessTools.FieldRefAccess<Map>(type, "map");
		setGenomeListBuilding = AccessTools.FieldRefAccess<Building>(type, "building");
		MP.RegisterSyncWorker<Command>(SyncCommand, type, isImplicit: false, shouldConstruct: true);
		PatchingUtilities.PatchSystemRandCtor(new string[3] { "GeneticRim.CompExploder", "GeneticRim.HediffComp_PeriodicWounds", "GeneticRim.CompElectroWomb" }, patchPushPop: false);
		MpCompat.RegisterLambdaMethod("GeneticRim.Building_ArchocentipedeFormer", "GetGizmos", 0, 1)[1].SetDebugOnly();
		MpCompat.RegisterLambdaMethod("GeneticRim.Building_ArchoWomb", "GetGizmos", 1, 2)[1].SetDebugOnly();
		MpCompat.RegisterLambdaMethod("GeneticRim.Building_Mechahybridizer", "GetGizmos", default(int)).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("GeneticRim.CompApplyAgeDiseases", "GetGizmos", default(int)).SetDebugOnly();
		Type type2 = AccessTools.TypeByName("GeneticRim.ArchotechExtractableAnimals_MapComponent");
		MP.RegisterSyncMethod(type2, "AddAnimalToCarry");
		MP.RegisterSyncMethod(type2, "AddParagonToCarry");
		MpCompat.RegisterLambdaDelegate("GeneticRim.Command_SetGenomeList", "ProcessInput", 2);
		MpCompat.RegisterLambdaDelegate("GeneticRim.CompArchotechGrowthCell", "CompFloatMenuOptions", default(int));
		LongEventHandler.ExecuteWhenFinished(LatePatch);
	}

	public static void LatePatch()
	{
		MpCompat.RegisterLambdaMethod("GeneticRim.Building_DNAStorageBank", "GetGizmos", 1).SetDebugOnly();
		MpCompat.RegisterLambdaMethod("GeneticRim.CompGenomorpher", "CompGetGizmosExtra", 1, 2).SetDebugOnly();
		MP.RegisterSyncMethod(AccessTools.DeclaredMethod("GeneticRim.CompGenomorpher:Initialize"));
		MpCompat.RegisterLambdaMethod("GeneticRim.CompElectroWomb", "CompGetGizmosExtra", default(int)).SetDebugOnly();
	}

	private static void SyncCommand(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(setGenomeListMap(command));
			sync.Write(setGenomeListBuilding(command));
		}
		else
		{
			setGenomeListMap(command) = sync.Read<Map>();
			setGenomeListBuilding(command) = sync.Read<Building>();
		}
	}
}
