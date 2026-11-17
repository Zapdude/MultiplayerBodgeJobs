using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("sarg.alphabiomes")]
internal class AlphaBiomes
{
	private static AccessTools.FieldRef<object, Building> buildingField;

	public AlphaBiomes(ModContentPack mod)
	{
		Type type = AccessTools.TypeByName("AlphaBiomes.Command_SetStoneType");
		buildingField = AccessTools.FieldRefAccess<Building>(type, "building");
		MP.RegisterSyncWorker<Command>(SyncSetStoneType, type, isImplicit: false, shouldConstruct: true);
		MpCompat.RegisterLambdaMethod(type, "ProcessInput", Enumerable.Range(0, 6).ToArray());
		PatchingUtilities.PatchPushPopRand(new string[3] { "AlphaBiomes.CompGasProducer:CompTick", "AlphaBiomes.TarSprayer:SteamSprayerTick", "AlphaBiomes.GameCondition_AcidRain:DoCellSteadyEffects" });
	}

	private static void SyncSetStoneType(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(buildingField(command));
		}
		else
		{
			buildingField(command) = sync.Read<Building>();
		}
	}
}
