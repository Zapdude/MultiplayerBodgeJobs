using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Multiplayer.Compat;

[MpCompatFor("VanillaExpanded.VCEF")]
internal class VanillaFishingExpanded
{
	private static Type commandType;

	private static AccessTools.FieldRef<object, Map> mapField;

	private static AccessTools.FieldRef<object, Zone> fishingZoneField;

	public VanillaFishingExpanded(ModContentPack mod)
	{
		if (!ModsConfig.OdysseyActive || !(AccessTools.TypeByName("VCE_Fishing.VCE_Fishing_FishingUtility_GetCatchesFor_Patch") != null))
		{
			PatchingUtilities.PatchSystemRandCtor("VCE_Fishing.JobDriver_Fish", patchPushPop: false);
			PatchingUtilities.PatchPushPopRand("VCE_Fishing.JobDriver_Fish:SelectFishToCatch");
			commandType = AccessTools.TypeByName("VCE_Fishing.Command_SetFishList");
			mapField = AccessTools.FieldRefAccess<Map>(commandType, "map");
			fishingZoneField = AccessTools.FieldRefAccess<Zone>(commandType, "zone");
			MpCompat.RegisterLambdaMethod(commandType, "ProcessInput", Enumerable.Range(0, 3).ToArray());
			MP.RegisterSyncWorker<Command>(SyncFishingZoneChange, commandType);
			MpCompat.RegisterLambdaMethod(AccessTools.TypeByName("VCE_Fishing.Zone_Fishing"), "GetGizmos", 1);
		}
	}

	private static void SyncFishingZoneChange(SyncWorker sync, ref Command command)
	{
		if (sync.isWriting)
		{
			sync.Write(mapField(command));
			sync.Write(fishingZoneField(command));
		}
		else
		{
			command = (Command)RuntimeHelpers.GetUninitializedObject(commandType);
			mapField(command) = sync.Read<Map>();
			fishingZoneField(command) = sync.Read<Zone>();
		}
	}
}
