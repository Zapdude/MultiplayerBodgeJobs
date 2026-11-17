using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("vanillaracesexpanded.sanguophage")]
public class VanillaRacesSanguophage
{
	private static Type singleUseAbilitiesCommandType;

	private static AccessTools.FieldRef<Command, Building> singleUseAbilitiesCommandBuildingField;

	public VanillaRacesSanguophage(ModContentPack mod)
	{
		Type type = (singleUseAbilitiesCommandType = AccessTools.TypeByName("VanillaRacesExpandedSanguophage.Command_SingleUseAbilities"));
		singleUseAbilitiesCommandBuildingField = AccessTools.FieldRefAccess<Building>(type, "building");
		MP.RegisterSyncWorker<Command>(SyncSingleUseAbilitiesCommand, type);
		MpCompat.RegisterLambdaDelegate(type, "ProcessInput", default(int));
		type = AccessTools.TypeByName("VanillaRacesExpandedSanguophage.CompDraincasket");
		MP.RegisterSyncMethod(type, "EjectContents");
		MpCompat.RegisterLambdaDelegate(type, "AddCarryToBatteryJobs", default(int));
	}

	private static void SyncSingleUseAbilitiesCommand(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(singleUseAbilitiesCommandBuildingField(command));
			return;
		}
		command = (Command)Activator.CreateInstance(singleUseAbilitiesCommandType, sync.Read<Building>());
	}
}
